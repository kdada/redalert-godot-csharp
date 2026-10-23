using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VoxelBaker;

internal static class Program
{
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    const string Help = """
VoxelBaker: independent .NET 10 / BCL-only Westwood voxel atlas baker
  --self-test
  bake --palette unittem.pal --output DIR --name grizzly
       --part body=gtnk.vxl --part turret=gtnktur.vxl --part barrel=gtnkbarl.vxl
       [--hva body=gtnk.hva] [--frame 0] [--directions 32] [--columns 8]
       [--scale 12] [--elevation 30] [--azimuth 0] [--padding 4] [--cell 128]
       [--palette-bits 6] [--team-start 16] [--team-count 16]
HVA defaults to the VXL sibling .hva; missing sibling uses identity. --hva NAME=- forces identity.
Scale is pixels per world unit; VXL footer scale is included. All parts share a fitted canvas/anchor.
Outputs NAME.PART.png, .indices.png/.bin, .normals.png/.bin, .team.png/.bin, .alpha.bin,
NAME.composite.png and NAME.json. Raw channels use full atlas row-major layout, one byte/pixel.
""";
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "--help" or "-h") { Console.WriteLine(Help); return 0; }
            if (args.Length == 1 && args[0] == "--self-test") { SelfTest.Run(); return 0; }
            if (args[0] != "bake") throw new ArgumentException("Expected bake or --self-test. Use --help.");
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var specs = new List<(string Name, string Path)>(); var hvas = new Dictionary<string, string>();
            var allowed = new HashSet<string>(["palette", "output", "name", "frame", "directions", "columns", "scale", "elevation", "azimuth", "padding", "cell", "palette-bits", "team-start", "team-count"]);
            for (int i = 1; i < args.Length; i += 2)
            {
                if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length) throw new ArgumentException("Options require --name value pairs.");
                string key = args[i][2..], value = args[i + 1];
                if (key is "part" or "hva")
                {
                    int split = value.IndexOf('=');
                    if (split < 1 || split == value.Length - 1) throw new ArgumentException($"--{key} requires NAME=PATH.");
                    string name = value[..split]; ValidName(name);
                    if (key == "part") specs.Add((name, Path.GetFullPath(value[(split + 1)..])));
                    else hvas.Add(name, value[(split + 1)..]);
                }
                else
                {
                    if (!allowed.Contains(key)) throw new ArgumentException($"Unknown option --{key}.");
                    if (!values.TryAdd(key, value)) throw new ArgumentException($"Duplicate --{key}.");
                }
            }
            string Required(string key) => values.TryGetValue(key, out var value) ? value : throw new ArgumentException($"Missing --{key}.");
            int Int(string key, int fallback) => values.TryGetValue(key, out var value) ? int.Parse(value, CultureInfo.InvariantCulture) : fallback;
            float Float(string key, float fallback) => values.TryGetValue(key, out var value) ? float.Parse(value, CultureInfo.InvariantCulture) : fallback;
            string prefix = Required("name"); ValidName(prefix);
            if (specs.Count == 0 || specs.Count > 16 || specs.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != specs.Count)
                throw new ArgumentException("Provide 1..16 uniquely named --part entries.");
            if (hvas.Keys.Any(k => !specs.Any(s => s.Name == k))) throw new ArgumentException("--hva names must match --part names.");
            var o = new RenderOptions(Int("directions", 32), Float("scale", 12), Float("elevation", 30), Float("azimuth", 0), Int("columns", 8), Int("padding", 4), Int("team-start", 16), Int("team-count", 16), Int("frame", 0));
            if (o.Directions < 1 || o.Directions > 128 || o.Columns < 1 || o.Columns > 32 || !float.IsFinite(o.Scale) || o.Scale <= 0 || o.Scale > 256 || !float.IsFinite(o.Elevation) || o.Elevation <= 0 || o.Elevation >= 90 || !float.IsFinite(o.Azimuth) || o.Padding < 1 || o.Padding > 128 || o.TeamCount < 1 || o.TeamCount > 255 || o.TeamStart < 1 || o.TeamStart + o.TeamCount > 256 || o.Frame < 0)
                throw new ArgumentException("Invalid render option range.");
            int bits = Int("palette-bits", 6);
            if (bits is not (6 or 8)) throw new ArgumentException("--palette-bits must be 6 or 8.");
            string palettePath = Path.GetFullPath(Required("palette")); byte[] palette = Palette.Read(palettePath, bits);
            var parts = new List<Part>();
            foreach (var spec in specs)
            {
                string? hva = hvas.TryGetValue(spec.Name, out var explicitHva) ? explicitHva == "-" ? null : Path.GetFullPath(explicitHva) : Path.ChangeExtension(spec.Path, ".hva");
                if (!hvas.ContainsKey(spec.Name) && !File.Exists(hva)) hva = null;
                parts.Add(Renderer.LoadPart(spec.Name, spec.Path, hva, o.Frame));
            }
            int? cell = values.ContainsKey("cell") ? Int("cell", 0) : null;
            if (cell is < 8 or > 1024) throw new ArgumentException("--cell must be 8..1024.");
            Canvas canvas = Renderer.Fit(parts, o, cell);
            Bake(Path.GetFullPath(Required("output")), prefix, parts, palette, palettePath, bits, canvas, o);
            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or OverflowException or FormatException)
        { Console.Error.WriteLine($"VoxelBaker: {ex.Message}"); return 1; }
    }
    static void ValidName(string name)
    {
        if (!Regex.IsMatch(name, "^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant)) throw new ArgumentException("Names use 1..64 ASCII letters, digits, underscores or hyphens.");
    }
    internal static void Bake(string output, string prefix, List<Part> parts, byte[] palette, string? palettePath, int bits, Canvas canvas, RenderOptions o)
    {
        int rows = (o.Directions + o.Columns - 1) / o.Columns;
        int width = checked(canvas.Width * o.Columns), height = checked(canvas.Height * rows), pixels = checked(width * height);
        if (pixels > 16_000_000) throw new InvalidDataException("Atlas exceeds 16 million pixel budget.");
        Directory.CreateDirectory(output);
        var partMetadata = new List<object>();
        foreach (Part part in parts)
        {
            string stem = prefix + "." + part.Name;
            var rgba = new byte[pixels * 4]; var indices = new byte[pixels]; var normals = new byte[pixels]; var team = new byte[pixels]; var alpha = new byte[pixels];
            var frames = new List<object>();
            for (int d = 0; d < o.Directions; d++)
            {
                Tile tile = Renderer.Render(part.Faces, canvas, o, d, palette);
                if (tile.OpaquePixels == 0) throw new InvalidDataException($"Empty facing {d} in {part.Name}; increase --scale.");
                Copy(tile.Rgba, rgba, d, 4); Copy(tile.Indices, indices, d, 1); Copy(tile.Normals, normals, d, 1); Copy(tile.Team, team, d, 1); Copy(tile.Alpha, alpha, d, 1);
                frames.Add(new { direction = d, degrees = o.Azimuth + d * 360f / o.Directions,
                    rect = new[] { d % o.Columns * canvas.Width, d / o.Columns * canvas.Height, canvas.Width, canvas.Height },
                    anchor = new[] { canvas.AnchorX, canvas.AnchorY }, tile.OpaquePixels });
            }
            Png.Write(Path.Combine(output, stem + ".png"), width, height, rgba);
            Channel("indices", indices, alpha, false); Channel("normals", normals, alpha, false); Channel("team", team, alpha, true);
            File.WriteAllBytes(Path.Combine(output, stem + ".alpha.bin"), alpha);
            partMetadata.Add(new { part.Name, source = Path.GetFileName(part.Source), sourceSha256 = Hash(part.Source),
                hva = part.Hva == null ? null : Path.GetFileName(part.Hva), hvaSha256 = part.Hva == null ? null : Hash(part.Hva),
                identityPose = part.Hva == null, png = stem + ".png", indicesPng = stem + ".indices.png", indicesRaw = stem + ".indices.bin",
                normalsPng = stem + ".normals.png", normalsRaw = stem + ".normals.bin", teamPng = stem + ".team.png", teamRaw = stem + ".team.bin", alphaRaw = stem + ".alpha.bin",
                limbs = part.Limbs.Select(l => new { l.Name, size = new[] { l.X, l.Y, l.Z }, l.Scale, min = V(l.Min), max = V(l.Max), l.NormalType, voxels = l.Count }), frames });
            void Channel(string name, byte[] data, byte[] coverage, bool isTeam)
            {
                var image = new byte[pixels * 4];
                for (int i = 0; i < pixels; i++)
                {
                    byte value = isTeam ? data[i] == 0 ? (byte)0 : (byte)(1 + (data[i] - 1) * 254 / Math.Max(1, o.TeamCount - 1)) : data[i];
                    image[i * 4] = image[i * 4 + 1] = image[i * 4 + 2] = value;
                    image[i * 4 + 3] = isTeam ? data[i] == 0 ? (byte)0 : coverage[i] : coverage[i];
                }
                Png.Write(Path.Combine(output, stem + "." + name + ".png"), width, height, image);
                File.WriteAllBytes(Path.Combine(output, stem + "." + name + ".bin"), data);
            }
        }
        // Composite shares the exact camera and depth buffer across all parts, useful for inspection.
        var allFaces = parts.SelectMany(p => p.Faces).ToList(); var composite = new byte[pixels * 4];
        for (int d = 0; d < o.Directions; d++) Copy(Renderer.Render(allFaces, canvas, o, d, palette).Rgba, composite, d, 4);
        Png.Write(Path.Combine(output, prefix + ".composite.png"), width, height, composite);
        var metadata = new
        {
            schema = "westwood-voxel-atlas/v1", name = prefix, directions = o.Directions, animationFrame = o.Frame,
            atlas = new { width, height, columns = o.Columns, rows, cellWidth = canvas.Width, cellHeight = canvas.Height, anchor = new[] { canvas.AnchorX, canvas.AnchorY } },
            projection = new { kind = "orthographic", elevationDegrees = o.Elevation, azimuthDegrees = o.Azimuth, pixelsPerWorldUnit = o.Scale,
                handedness = "VXL X preserved, Y reflected, Z up", angle = "positive counterclockwise in world XY; direction 0 projects +X right",
                screenX = "rotatedWorldX * scale", screenY = "(-sin(elevation)*rotatedWorldY-cos(elevation)*worldZ)*scale",
                origin = "VXL/HVA model origin; shared across parts and facings; no per-part recentering",
                modelTransform = "world = footerScale * reflectY(HVA.linear * (boundsMin + grid * (boundsMax-boundsMin)/size) + HVA.translation * footerScale * (boundsMax-boundsMin)/size)" },
            lighting = new { kind = "exposed voxel face normals from neighbor occupancy", ambient = 0.38, diffuse = 0.62, lightDirection = new[] { -0.6, -0.8, 1.2 }, vxlNormalIndicesUsedForLighting = false },
            palette = new { source = palettePath == null ? "synthetic" : Path.GetFileName(palettePath), sha256 = palettePath == null ? null : Hash(palettePath), inputChannelBits = bits, expandedRgb = Convert.ToHexString(palette).ToLowerInvariant(), transparentIndex = 0 },
            teamColor = new { startIndex = o.TeamStart, count = o.TeamCount, rawEncoding = "0=not team, 1..count=source ramp offset+1", pngEncoding = "RGB ramp 1..255; alpha 255 only on team pixels; source ramp order retained" },
            raw = new { layout = "full atlas, row-major, one byte per pixel, no header", indices = "original PAL index; use alpha.bin for coverage", normals = "original VXL normal index; use alpha.bin for coverage", alpha = "0=transparent, 255=covered" },
            composite = prefix + ".composite.png", parts = partMetadata
        };
        File.WriteAllText(Path.Combine(output, prefix + ".json"), JsonSerializer.Serialize(metadata, JsonOptions) + "\n");
        Console.WriteLine($"Baked {prefix}: {parts.Count} parts x {o.Directions} facings, cell {canvas.Width}x{canvas.Height}, atlas {width}x{height}, anchor ({canvas.AnchorX},{canvas.AnchorY}) -> {output}");
        void Copy(byte[] source, byte[] dest, int facing, int channels)
        {
            int x = facing % o.Columns * canvas.Width, y = facing / o.Columns * canvas.Height;
            for (int row = 0; row < canvas.Height; row++) Buffer.BlockCopy(source, row * canvas.Width * channels, dest, ((y + row) * width + x) * channels, canvas.Width * channels);
        }
    }
    static float[] V(System.Numerics.Vector3 p) => [p.X, p.Y, p.Z];
    static string Hash(string path) { using var f = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant(); }
}
