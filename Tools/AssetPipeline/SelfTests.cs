using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Ra2AssetPipeline;

internal static class SelfTests
{
    private static int assertions;

    public static void Run()
    {
        assertions = 0;
        string tempRoot = Environment.GetEnvironmentVariable("TMPDIR")
            ?? throw new InvalidDataException("--self-test requires TMPDIR; it never falls back to /tmp.");
        ShpTs.Require(Path.IsPathFullyQualified(tempRoot) && Directory.Exists(tempRoot), "TMPDIR must be an existing absolute directory.");
        string root = Path.Combine(Path.GetFullPath(tempRoot), $"ra2-asset-selftest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            byte[] shp = Fixture();
            byte[] pal = PaletteFixture();
            var sprite = ShpTs.Read(shp);
            var palette = Palette.Read(pal);
            Check(sprite.CanvasWidth == 16 && sprite.CanvasHeight == 12 && sprite.Frames.Count == 8, "canvas and frame count");
            Check(sprite.Frames[0].X == 2 && sprite.Frames[0].Y == 3, "nonzero crop offset");
            Check(sprite.Frames[0].Width == 3 && sprite.Frames[0].Height == 2, "odd width is preserved without padding");
            Bytes(sprite.Frames[0].Indices, [0, 16, 31, 1, 7, 255], "format 0");
            Bytes(sprite.Frames[1].Indices, [4, 5], "format 1");
            Bytes(sprite.Frames[2].Indices, [2, 3, 4, 0, 6, 7, 8, 0], "format 2 fixed stride and zero fill");
            Bytes(sprite.Frames[3].Indices, [16, 0, 0, 31, 7, 0, 0, 1, 0, 0], "format 3 zero runs and implicit row tail");
            Bytes(sprite.Frames[5].Indices, [0, 0], "inclusive terminal transparent skip clips safely");
            Check(!sprite.Frames[6].HasData && sprite.Frames[6].Width == 2 && sprite.Frames[6].Height == 2, "empty pointer retains dimensions");
            Check(!sprite.Frames[7].HasData && sprite.Frames[7].Indices.Length == 0, "zero sized frame");
            Check(palette.Colors[63].R == 252 && palette.Colors[63].G == 0, "VGA6 palette expansion");
            Check(Palette.Read(Enumerable.Repeat((byte)255, 768).ToArray(), 8).Colors[0].R == 255, "RGB8 palette");
            Check(PngWriter.Crc32(Encoding.ASCII.GetBytes("123456789"), []) == 0xcbf43926, "independent standard CRC32 vector");
            Check(PngWriter.Crc32(Encoding.ASCII.GetBytes("IEND"), []) == 0xae426082, "PNG IEND CRC known vector");

            string shpPath = Path.Combine(root, "fixture.shp"), palPath = Path.Combine(root, "fixture.pal");
            File.WriteAllBytes(shpPath, shp);
            File.WriteAllBytes(palPath, pal);
            string atlas = Path.Combine(root, "atlas");
            var manifest = Exporter.Export(sprite, palette, new ExportOptions(atlas, Columns: 2, ShadowLayout: "second-half"), shpPath, shp, palPath);
            Check(manifest.Images.Count == 1 && manifest.Images[0].Width == 10 && manifest.Images[0].Height == 8, "atlas geometry");
            Check(manifest.Frames[0].SourceRect == new IntRect(2, 3, 3, 2), "source rect metadata");
            Check(manifest.Frames[0].CenterOffset == new FloatPoint(-4.5f, -2), "half pixel center offset");
            Check(manifest.Frames[0].Role == "color" && manifest.Frames[0].PairedFrame == 4, "color-shadow pairing");
            Check(manifest.Frames[4].Role == "shadow" && manifest.Frames[4].PairedFrame == 0, "shadow-color pairing");
            Check(manifest.Frames[0].ReservedHeaderBase64 == Convert.ToBase64String(sprite.Frames[0].ReservedHeaderBytes), "reserved header metadata");
            Check(Convert.FromBase64String(manifest.Palette.RawRgbBase64).SequenceEqual(pal), "lossless original palette");

            var rgba = ReadPng(Path.Combine(atlas, "atlas.png"));
            var indices = ReadPng(Path.Combine(atlas, "atlas.indices.png"));
            var mask = ReadPng(Path.Combine(atlas, "atlas.team-mask.png"));
            Check(rgba.Channels == 4 && indices.Channels == 1 && mask.Channels == 1, "PNG color types");
            Check(rgba.At(0, 0, 3) == 0 && rgba.At(1, 0, 3) == 255, "transparent index");
            Check(rgba.At(1, 0, 0) == palette.Colors[16].R, "palette color render");
            Check(indices.At(1, 0) == 16 && indices.At(2, 0) == 31 && indices.At(2, 1) == 255, "PNG preserves all original index values");
            Check(mask.At(1, 0) == 1 && mask.At(2, 0) == 16 && mask.At(2, 1) == 0, "team mask 16..31");
            var shadowRect = manifest.Frames[4].ImageRect;
            Check(rgba.At(shadowRect.X, shadowRect.Y, 0) == 0 && rgba.At(shadowRect.X, shadowRect.Y, 3) == 128,
                "black shadow with configurable alpha");
            Check(rgba.At(shadowRect.X + 1, shadowRect.Y, 3) == 0, "shadow transparent pixels");
            foreach (var frame in sprite.Frames.Where(f => f.HasData))
            {
                var rect = manifest.Frames[frame.Index].ImageRect;
                for (int y = 0; y < frame.Height; y++)
                for (int x = 0; x < frame.Width; x++)
                    Check(indices.At(rect.X + x, rect.Y + y) == frame.Indices[y * frame.Width + x], "atlas indexed pixel roundtrip");
            }
            using (var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(atlas, "manifest.json"))))
            {
                Check(json.RootElement.GetProperty("schemaVersion").GetInt32() == 1, "serialized schema version");
                Check(json.RootElement.GetProperty("frames")[4].GetProperty("role").GetString() == "shadow", "serialized shadow metadata");
                Check(json.RootElement.GetProperty("palette").GetProperty("colors").GetArrayLength() == 256, "serialized palette");
            }

            string framesPath = Path.Combine(root, "frames");
            var perFrame = Exporter.Export(sprite, palette, new ExportOptions(framesPath, "frames"), shpPath, shp, palPath);
            Check(perFrame.Images.Count == 8, "per-frame export count");
            Check(perFrame.Frames.All(f => f.Role == "unspecified" && f.PairedFrame == null), "no implicit shadow guesses");
            var cropped = ReadPng(Path.Combine(framesPath, "frame-00000.indices.png"));
            Check(cropped.Width == 3 && cropped.Height == 2, "per-frame crop dimensions");
            Bytes(cropped.Pixels, sprite.Frames[0].Indices, "per-frame indexed pixel roundtrip");
            var empty = ReadPng(Path.Combine(framesPath, "frame-00007.png"));
            Check(empty.Width == 1 && empty.Height == 1 && empty.At(0, 0, 3) == 0, "empty frame PNG placeholder");
            var unclassified = ReadPng(Path.Combine(framesPath, "frame-00004.png"));
            Check(unclassified.At(0, 0, 3) == 255, "unspecified shadows retain palette rendering");

            var alphaPath = Path.Combine(root, "alpha");
            var alpha = Exporter.Export(sprite, palette, new ExportOptions(alphaPath, "frames", ShadowLayout: "second-half", ShadowAlpha: 77), shpPath, shp, palPath);
            Check(ReadPng(Path.Combine(alphaPath, alpha.Images[4].Color)).At(0, 0, 3) == 77, "custom shadow alpha");
            var transparentPath = Path.Combine(root, "transparent");
            var custom = Exporter.Export(sprite, palette, new ExportOptions(transparentPath, "frames", TransparentIndex: 16), shpPath, shp, palPath);
            var transparent = ReadPng(Path.Combine(transparentPath, custom.Images[0].Color));
            Check(transparent.At(0, 0, 3) == 255 && transparent.At(1, 0, 3) == 0, "custom transparent index");

            // Force multiple IDAT chunks with deterministic incompressible samples.
            byte[] noise = new byte[257 * 129 * 4];
            new Random(5021).NextBytes(noise);
            string noisePath = Path.Combine(root, "noise.png");
            PngWriter.Write(noisePath, 257, 129, noise, 4);
            var noisePng = ReadPng(noisePath);
            Check(noisePng.IdatChunks > 1, "streamed IDAT chunk boundary");
            Bytes(noisePng.Pixels, noise, "multi-chunk PNG roundtrip");

            Bad(() => ShpTs.Read([]), "truncated file header");
            Bad(() => ShpTs.Read(shp.AsSpan(0, 100)), "truncated frame table");
            Mutation(shp, b => Put16(b, 0, 1), "wrong SHP dialect signature");
            Mutation(shp, b => Put16(b, 2, 0), "zero canvas");
            Mutation(shp, b => Put16(b, 6, 0), "zero frame count");
            Mutation(shp, b => b[16] = 4, "unsupported compression");
            Mutation(shp, b => Put32(b, 28, uint.MaxValue), "out of range pointer");
            Mutation(shp, b => Put32(b, 28, 8), "pointer into header table");
            Mutation(shp, b => Put32(b, 28, (uint)b.Length), "pointer at end of input");
            Mutation(shp, b => { Put16(b, 12, 65535); Put16(b, 14, 65535); }, "decoded allocation budget");
            Mutation(shp, b => Put16(b, 12, 0), "data with zero width");
            Mutation(shp, b => Put32(b, 52, sprite.Frames[0].DataOffset + 1), "raw frame crossing next frame offset");
            int f2 = (int)sprite.Frames[2].DataOffset, f3 = (int)sprite.Frames[3].DataOffset;
            Mutation(shp, b => Put16(b, f2, 1), "format2 length below prefix");
            Mutation(shp, b => Put16(b, f2, 7), "format2 row too wide");
            Mutation(shp, b => Put16(b, f2, 6), "format2 truncated payload");
            Mutation(shp, b => Put16(b, f3, 1), "RLE length below prefix");
            Mutation(shp, b => Put16(b, f3, 65535), "RLE row beyond payload");
            Mutation(shp, b => Put16(b, f3, 4), "dangling RLE run marker");
            Mutation(shp, b => b[f3 + 4] = 0, "zero-length RLE run");
            Mutation(shp, b => b[f3 + 4] = 6, "RLE run beyond row");
            Mutation(shp, b => b[f3 + 4] = 5, "nonterminal transparent overrun");
            Mutation(shp, b => b[(int)sprite.Frames[5].DataOffset + 3] = 4, "terminal transparent overrun beyond one pixel");
            Mutation(shp, b => { Put16(b, 8 + 3 * 24 + 4, 1); b[f3 + 3] = 17; }, "RLE literal beyond row");
            Bad(() => ShpTs.Read(shp.AsSpan(0, shp.Length - 1)), "truncated final compressed frame");
            Bad(() => Palette.Read(pal.AsSpan(0, 767)), "truncated palette");
            Bad(() => Palette.Read(new byte[769]), "oversized palette");
            byte[] invalidPal = (byte[])pal.Clone(); invalidPal[20] = 64;
            Bad(() => Palette.Read(invalidPal), "invalid 6-bit component");
            Bad(() => Palette.Read(pal, 7), "invalid palette depth");
            Bad(() => Exporter.Export(sprite, palette, new ExportOptions(atlas), shpPath, shp, palPath), "existing output is protected");
            Bad(() => Exporter.Export(sprite, palette, new ExportOptions(Path.Combine(root, "bad"), Columns: 9), shpPath, shp, palPath), "columns beyond count");
            Bad(() => Exporter.Export(sprite with { Frames = sprite.Frames.Take(7).ToArray() }, palette,
                new ExportOptions(Path.Combine(root, "bad"), ShadowLayout: "second-half"), shpPath, shp, palPath), "odd shadow layout");
            Bad(() => PngWriter.Write(Stream.Null, 0, 1, [], 1), "zero PNG dimensions");
            Bad(() => PngWriter.Write(Stream.Null, 1, 1, [0], 4), "wrong PNG sample count");

            Check(Program.Main(["convert", "--shp", shpPath, "--pal", palPath, "--out", Path.Combine(root, "cli"), "--layout", "frames"]) == 0, "CLI full conversion");
            Check(Program.Main(["convert", "--shp", shpPath, "--pal", palPath, "--out", Path.Combine(root, "cli-bad"), "--transparent-index", "256"]) == 1, "CLI validates byte option");
            Check(Program.Main(["convert", "--shp", shpPath, "--shp", shpPath]) == 1, "CLI rejects duplicate option");
            Check(Program.Main(["inspect", "--shp"]) == 1, "CLI rejects missing value");
            Check(Program.Main(["inspect", "--shp", shpPath, "--unknown", "x"]) == 1, "CLI rejects unknown option");
            Check(Program.Main(["inspect", "--shp", shpPath, "--pal", palPath]) == 1, "CLI rejects ambiguous input");
            Console.WriteLine($"SELF-TEST PASS: {assertions} assertions; formats 0/1/2/3, atlas/frames, palette/index/mask/shadow metadata, PNG CRC/zlib, malformed inputs, CLI. No original game assets used.");
        }
        finally
        {
            // root is created above from an existing absolute TMPDIR plus a fixed prefix and
            // random basename; verify the resolved parent and prefix before recursive cleanup.
            string resolved = Path.GetFullPath(root);
            if (Path.GetDirectoryName(resolved) != Path.GetFullPath(tempRoot).TrimEnd(Path.DirectorySeparatorChar)
                || !Path.GetFileName(resolved).StartsWith("ra2-asset-selftest-", StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing cleanup outside the generated self-test directory.");
            Directory.Delete(resolved, recursive: true);
        }
    }

    private sealed record FixtureFrame(ushort X, ushort Y, ushort Width, ushort Height, byte Format, byte[]? Payload);
    private static byte[] Fixture()
    {
        FixtureFrame[] frames =
        [
            new(2, 3, 3, 2, 0, [0, 16, 31, 1, 7, 255]),
            new(1, 5, 2, 1, 1, [4, 5]),
            new(0, 1, 4, 2, 2, [5, 0, 2, 3, 4, 6, 7, 8]),
            new(4, 6, 5, 2, 3, [7, 0, 16, 0, 2, 31, 7, 5, 0, 0, 2, 1]),
            new(2, 3, 2, 2, 1, [1, 0, 0, 1]),
            new(1, 5, 2, 1, 3, [4, 0, 0, 3]),
            new(0, 1, 2, 2, 1, null),
            new(0, 0, 0, 0, 1, null)
        ];
        using var data = new MemoryStream();
        using var writer = new BinaryWriter(data, Encoding.ASCII, leaveOpen: true);
        writer.Write((ushort)0); writer.Write((ushort)16); writer.Write((ushort)12); writer.Write((ushort)frames.Length);
        int offset = 8 + 24 * frames.Length;
        foreach (var frame in frames)
        {
            writer.Write(frame.X); writer.Write(frame.Y); writer.Write(frame.Width); writer.Write(frame.Height);
            writer.Write(frame.Format);
            writer.Write(Enumerable.Range(0, 11).Select(i => (byte)(0xa0 + i)).ToArray());
            writer.Write(frame.Payload is null ? 0U : (uint)offset);
            offset += frame.Payload?.Length ?? 0;
        }
        foreach (var frame in frames) if (frame.Payload is not null) writer.Write(frame.Payload);
        return data.ToArray();
    }

    private static byte[] PaletteFixture()
    {
        var data = new byte[768];
        for (int i = 0; i < 256; i++)
        {
            data[i * 3] = (byte)(i % 64);
            data[i * 3 + 1] = (byte)((i + 1) % 64);
            data[i * 3 + 2] = (byte)((i * 3) % 64);
        }
        return data;
    }

    private sealed record Png(int Width, int Height, int Channels, byte[] Pixels, int IdatChunks)
    {
        public byte At(int x, int y, int channel = 0) => Pixels[(y * Width + x) * Channels + channel];
    }

    // Small independent PNG verifier: check framing, known CRC polynomial, zlib and scanlines.
    private static Png ReadPng(string path)
    {
        byte[] png = File.ReadAllBytes(path);
        Bytes(png.AsSpan(0, 8).ToArray(), [137, 80, 78, 71, 13, 10, 26, 10], "PNG signature");
        int p = 8, width = 0, height = 0, channels = 0, idatChunks = 0;
        bool ended = false;
        using var compressed = new MemoryStream();
        while (p < png.Length)
        {
            Check(png.Length - p >= 12, "PNG chunk framing");
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(p, 4)));
            Check(length <= png.Length - p - 12, "PNG chunk length");
            var type = png.AsSpan(p + 4, 4);
            var payload = png.AsSpan(p + 8, length);
            uint crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(p + 8 + length, 4));
            Check(PngWriter.Crc32(type, payload) == crc, "PNG chunk CRC");
            switch (Encoding.ASCII.GetString(type))
            {
                case "IHDR":
                    Check(length == 13 && p == 8, "PNG IHDR placement");
                    width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(payload[..4]));
                    height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4, 4)));
                    Check(payload[8] == 8 && payload[9] is 0 or 6, "PNG depth/type");
                    Check(payload[10] == 0 && payload[11] == 0 && payload[12] == 0, "PNG compression/filter/interlace");
                    channels = payload[9] == 0 ? 1 : 4;
                    break;
                case "IDAT": compressed.Write(payload); idatChunks++; break;
                case "IEND": Check(length == 0, "PNG IEND length"); ended = true; break;
                default: throw new InvalidOperationException("Unexpected PNG chunk");
            }
            p += 12 + length;
            if (ended) break;
        }
        Check(ended && p == png.Length && idatChunks > 0, "PNG complete container");
        compressed.Position = 0;
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        zlib.CopyTo(raw);
        int stride = width * channels;
        byte[] filtered = raw.ToArray();
        Check(filtered.Length == (stride + 1) * height, "PNG exact inflated size");
        var samples = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            Check(filtered[y * (stride + 1)] == 0, "PNG row filter");
            filtered.AsSpan(y * (stride + 1) + 1, stride).CopyTo(samples.AsSpan(y * stride));
        }
        return new Png(width, height, channels, samples, idatChunks);
    }

    private static void Mutation(byte[] original, Action<byte[]> mutate, string message)
    {
        var bytes = (byte[])original.Clone();
        mutate(bytes);
        Bad(() => ShpTs.Read(bytes), message);
    }
    private static void Bad(Action action, string message)
    {
        try { action(); }
        catch (InvalidDataException) { assertions++; return; }
        throw new InvalidOperationException($"SELF-TEST FAIL (accepted malformed input): {message}");
    }
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException($"SELF-TEST FAIL: {message}");
    }
    private static void Bytes(byte[] actual, byte[] expected, string message) => Check(actual.SequenceEqual(expected), message);
    private static void Put16(byte[] bytes, int p, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(p, 2), value);
    private static void Put32(byte[] bytes, int p, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(p, 4), value);
}
