using System.Globalization;
using System.Text.Json;

namespace Ra2AssetPipeline;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args is ["--help"] or ["-h"])
            {
                Console.WriteLine(Help);
                return 0;
            }
            if (args is ["--self-test"])
            {
                SelfTests.Run();
                return 0;
            }
            var options = Parse(args[1..]);
            switch (args[0])
            {
                case "convert":
                {
                    ValidateKeys(options, "--shp", "--pal", "--out", "--layout", "--columns", "--palette-depth", "--shadow-layout", "--shadow-alpha", "--transparent-index");
                    string shpPath = Required(options, "--shp"), palPath = Required(options, "--pal");
                    var source = ReadFile(shpPath, ShpTs.MaxInputBytes);
                    var sprite = ShpTs.Read(source);
                    var palette = Palette.Read(ReadFile(palPath, 768), Integer(options, "--palette-depth", 6));
                    var export = new ExportOptions(Required(options, "--out"), Value(options, "--layout", "atlas"),
                        Integer(options, "--columns", 0), Value(options, "--shadow-layout", "unspecified"),
                        ByteOption(options, "--shadow-alpha", 128), ByteOption(options, "--transparent-index", 0));
                    var manifest = Exporter.Export(sprite, palette, export, shpPath, source, palPath);
                    Console.WriteLine($"Converted {manifest.Frames.Count} frames, {manifest.Images.Count} image set(s): {Path.GetFullPath(export.OutputDirectory)}");
                    return 0;
                }
                case "inspect":
                {
                    ValidateKeys(options, "--shp", "--pal", "--palette-depth");
                    ShpTs.Require(options.ContainsKey("--shp") != options.ContainsKey("--pal"), "inspect needs exactly one of --shp or --pal.");
                    if (options.TryGetValue("--shp", out string? shpPath))
                    {
                        ShpTs.Require(!options.ContainsKey("--palette-depth"), "--palette-depth requires --pal.");
                        var bytes = ReadFile(shpPath, ShpTs.MaxInputBytes);
                        var sprite = ShpTs.Read(bytes);
                        var result = new
                        {
                            format = "shp-ts-ra2", sourceFile = Path.GetFileName(shpPath), sha256 = Exporter.Hash(bytes),
                            sprite.CanvasWidth, sprite.CanvasHeight, frameCount = sprite.Frames.Count,
                            frames = sprite.Frames.Select(f => new
                            {
                                f.Index, f.X, f.Y, f.Width, f.Height, f.Compression, f.DataOffset, f.HasData,
                                f.EncodedLength, indicesSha256 = Exporter.Hash(f.Indices),
                                reservedHeaderBase64 = Convert.ToBase64String(f.ReservedHeaderBytes)
                            })
                        };
                        Console.WriteLine(JsonSerializer.Serialize(result, Exporter.JsonOptions));
                    }
                    else
                    {
                        string palPath = Required(options, "--pal");
                        var palette = Palette.Read(ReadFile(palPath, 768), Integer(options, "--palette-depth", 6));
                        Console.WriteLine(JsonSerializer.Serialize(new
                        {
                            format = "westwood-pal", sourceFile = Path.GetFileName(palPath),
                            palette.ComponentBits, palette.Colors, rawRgbBase64 = Convert.ToBase64String(palette.RawBytes)
                        }, Exporter.JsonOptions));
                    }
                    return 0;
                }
                default: throw new InvalidDataException($"Unknown command: {args[0]}. Use --help.");
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or OverflowException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    internal static byte[] ReadFile(string path, int limit)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        ShpTs.Require(file.Length <= limit, $"Input exceeds {limit} byte limit: {path}");
        var data = new byte[(int)file.Length];
        file.ReadExactly(data);
        return data;
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        ShpTs.Require(args.Length % 2 == 0, "Each option needs one value; use --help.");
        for (int i = 0; i < args.Length; i += 2)
        {
            ShpTs.Require(args[i].StartsWith("--", StringComparison.Ordinal), $"Expected option, got: {args[i]}");
            ShpTs.Require(options.TryAdd(args[i], args[i + 1]), $"Duplicate option: {args[i]}");
        }
        return options;
    }

    private static void ValidateKeys(Dictionary<string, string> options, params string[] allowed)
    {
        foreach (var key in options.Keys)
            ShpTs.Require(allowed.Contains(key, StringComparer.Ordinal), $"Unknown option: {key}");
    }
    private static string Required(Dictionary<string, string> options, string key) =>
        options.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new InvalidDataException($"Missing required option: {key}");
    private static string Value(Dictionary<string, string> options, string key, string fallback) => options.GetValueOrDefault(key, fallback);
    private static int Integer(Dictionary<string, string> options, string key, int fallback)
    {
        if (!options.TryGetValue(key, out string? value)) return fallback;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
            ? number : throw new InvalidDataException($"Invalid integer for {key}: {value}");
    }
    private static byte ByteOption(Dictionary<string, string> options, string key, int fallback)
    {
        int value = Integer(options, key, fallback);
        ShpTs.Require(value is >= 0 and <= 255, $"{key} must be in 0..255.");
        return (byte)value;
    }

    private const string Help = """
        RA2 Asset Pipeline — pure .NET 10 / no external packages
        Commands:
          --self-test
          inspect --shp <file.shp>
          inspect --pal <file.pal> [--palette-depth 6|8]
          convert --shp <file.shp> --pal <file.pal> --out <new-or-empty-dir>
            [--layout atlas|frames] [--columns N] [--palette-depth 6|8]
            [--shadow-layout unspecified|second-half] [--shadow-alpha 0..255]
            [--transparent-index 0..255]
        Defaults: atlas, automatic columns, VGA6 palette, unspecified shadows,
                  shadow alpha 128, transparent index 0.
        Each image set: RGBA8 PNG + grayscale8 original indices + grayscale8 team mask.
        Team mask samples 1..16 correspond to palette indices 16..31; 0 means none.
        TS/RA2 SHP only; the input must already be extracted from MIX archives.
        """;
}
