using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Ra2AssetPipeline;

namespace Ra2TerrainPipeline;

internal static class SelfTests
{
    private static int checks;
    public static void Run()
    {
        checks = 0;
        byte[] fixture = Fixture();
        var tmp = TmpReader.Read(fixture);
        Check(tmp.BlocksX == 2 && tmp.BlocksY == 1 && tmp.Frames.Count == 1 && tmp.EmptyIndices.SequenceEqual([1]), "grid and empty entries");
        var block = tmp.Frames[0].Block;
        Check(tmp.Frames[0].SourceOffset == 24 && block.X == 30 && block.Y == 15, "source and signed drawing offsets");
        Check(block.Height == 2 && block.TerrainType == 7 && block.RampType == 4, "terrain header fields");
        Check(block.RadarLeft.SequenceEqual(new byte[] { 1, 2, 3 }) && block.Reserved.SequenceEqual(new byte[] { 7, 8, 9 }), "all 52 header bytes accounted for");
        Check(block.ExtraIndices!.SequenceEqual(new byte[] { 0, 1, 2, 3, 4, 5 }) && block.ExtraZData!.Length == 6, "separate extra and extra Z payloads");
        Check(Enumerable.Range(0, 30).Sum(TmpReader.RowWidth) == 900, "diamond contains exactly 900 samples");
        var palette = Palette.Read(PaletteBytes());
        byte[] rgba = Converter.RenderDiamond(block.DiamondIndices, palette);
        Check(rgba.Length == 60 * 30 * 4, "diamond canvas 60x30 RGBA");
        Check(Enumerable.Range(0, 1800).Count(i => rgba[i * 4 + 3] == 255) == 900, "900 opaque encoded samples including index zero");
        Check(rgba[(0 * 60 + 27) * 4 + 3] == 0 && rgba[(0 * 60 + 28) * 4 + 3] == 255 && rgba[(0 * 60 + 31) * 4] == 12, "top row placement/order");
        Check(rgba[(14 * 60) * 4] == (420 % 64) * 4 && rgba[(14 * 60 + 59) * 4] == (479 % 64) * 4, "middle row spans exactly 60 samples");
        Check(rgba[(28 * 60 + 28) * 4] == (896 % 64) * 4 && rgba.AsSpan(29 * 60 * 4).IndexOfAnyExcept((byte)0) < 0, "bottom diamond row and empty final row");
        Check(palette.Colors[63].R == 252, "6-bit PAL channel expansion");
        byte[] extra = Converter.RenderExtra(block.ExtraIndices, palette);
        Check(extra[3] == 0 && extra[7] == 255 && extra[4] == 4, "extra index-zero transparency");
        Reject(() => Palette.Read(new byte[767]), "truncated palette");
        Reject(() => Palette.Read(Enumerable.Repeat((byte)64, 768).ToArray()), "invalid 6-bit palette channel");
        Check(Palette.Read(Enumerable.Repeat((byte)255, 768).ToArray(), 8).Colors[0].R == 255, "explicit 8-bit palette");

        for (int length = 0; length < fixture.Length; length++)
        {
            int cutoff = length;
            Reject(() => TmpReader.Read(fixture.AsSpan(0, cutoff)), "truncation at byte " + cutoff);
        }
        Bad(fixture, 0, 0, "zero block dimension");
        Bad(fixture, 4, -1, "negative block dimension");
        Bad(fixture, 0, int.MaxValue, "grid product budget");
        Bad(fixture, 8, 48, "TS/non-RA2 dimensions");
        Bad(fixture, 12, -30, "negative tile height");
        Bad(fixture, 16, 20, "offset into table");
        Bad(fixture, 16, int.MaxValue, "offset beyond input");
        Bad(fixture, 16, -1, "unsigned offset wraparound");
        Bad(fixture, 24 + 12, 51, "Z overlapping block header");
        Bad(fixture, 24 + 8, 1851, "extra overlapping base Z");
        Bad(fixture, 24 + 8, int.MaxValue, "extra offset plus size overflow");
        Bad(fixture, 24 + 16, 1852, "extra Z overlapping color");
        Bad(fixture, 24 + 28, -1, "negative extra width");
        Bad(fixture, 24 + 32, 0, "zero extra height");
        Bad(fixture, 24 + 28, int.MaxValue, "extra pixel budget");
        Bad(fixture, 20, 24 + 60, "cross-block payload truncation");

        var empty = new byte[20];
        Set(empty, 0, 1); Set(empty, 4, 1); Set(empty, 8, 60); Set(empty, 12, 30);
        Check(TmpReader.Read(empty).Frames.Count == 0, "entirely empty TMP is bounded and valid");
        var aliases = (byte[])fixture.Clone(); Set(aliases, 20, 24);
        var aliasDoc = TmpReader.Read(aliases);
        Check(aliasDoc.Frames.Count == 2 && ReferenceEquals(aliasDoc.Frames[0].Block, aliasDoc.Frames[1].Block), "aliased offsets do not amplify allocations");
        var unordered = new byte[fixture.Length + fixture.Length - 24];
        fixture.CopyTo(unordered, 0); fixture.AsSpan(24).CopyTo(unordered.AsSpan(fixture.Length));
        Set(unordered, 16, fixture.Length); Set(unordered, 20, 24);
        Check(TmpReader.Read(unordered).Frames.Select(f => f.SourceOffset).SequenceEqual(new uint[] { (uint)fixture.Length, 24 }), "offset table need not be sorted");
        var inferred = (byte[])fixture.Clone(); Set(inferred, 24 + 8, 0); Set(inferred, 24 + 16, 0);
        Check(TmpReader.Read(inferred).Frames[0].Block.Warnings.Count == 2, "zero extra pointers explicitly report inferred sequential layout");
        var colorOnly = inferred.AsSpan(0, inferred.Length - 6).ToArray();
        Check(TmpReader.Read(colorOnly).Frames[0].Block.ExtraZData is null, "extra depth may be unavailable without losing color layer");
        for (int length = colorOnly.Length + 1; length < inferred.Length; length++)
        {
            int cutoff = length;
            Reject(() => TmpReader.Read(inferred.AsSpan(0, cutoff)), "partial inferred extra Z rectangle");
        }
        var groundOnly = fixture.AsSpan(0, 24 + 1852).ToArray();
        Set(groundOnly, 24 + 36, 2);
        Check(TmpReader.Read(groundOnly).Frames[0].Block.ExtraIndices is null, "clear extra flag does not allocate from unused fields");
        RoundTrip(fixture, palette, rgba);
        Console.WriteLine($"PASS: {checks} format/export checks (including every truncated fixture prefix).");
    }

    private static void RoundTrip(byte[] fixture, Palette palette, byte[] expectedRgba)
    {
        string root = Environment.GetEnvironmentVariable("TMPDIR") ?? throw new IOException("Set TMPDIR to a writable temporary directory for --self-test.");
        string path = Path.GetFullPath(Path.Combine(root, "terrain-selftest-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(path);
        try
        {
            string source = Path.Combine(path, "synthetic.tem"), pal = Path.Combine(path, "synthetic.pal");
            string output = Path.Combine(path, "converted");
            File.WriteAllBytes(source, fixture); File.WriteAllBytes(pal, palette.RawBytes);
            var frames = Converter.Convert(source, pal, output);
            Check(frames.Count == 1 && frames[0].Extra!.Kind == "extra" && frames[0].Extra!.OffsetFromDiamond == new PixelOffset(-2, -23), "manifest separates raw extra offset from diamond");
            byte[] exported = File.ReadAllBytes(Path.Combine(output, frames[0].Diamond.IndicesFile!));
            Check(exported.SequenceEqual(fixture.AsSpan(24 + 52, 900).ToArray()) && Converter.Hash(exported) == frames[0].Diamond.IndicesSha256, "original packed indices and SHA256 survive conversion");
            Check(DecodePng(File.ReadAllBytes(Path.Combine(output, frames[0].Diamond.RgbaPng!)), 60, 30).SequenceEqual(expectedRgba), "actual PNG inflates to expected RGBA samples");
            using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output, "manifest.json")));
            Check(manifest.RootElement.GetProperty("frames")[0].GetProperty("terrainType").GetInt32() == 7 && manifest.RootElement.GetProperty("template").GetProperty("emptyIndices")[0].GetInt32() == 1, "serialized manifest contains terrain index and empty table slots");
            Reject(() => Converter.Convert(source, pal, output), "nonempty output directory refusal");
            Check(File.ReadAllBytes(Path.Combine(output, frames[0].Diamond.IndicesFile!)).SequenceEqual(exported), "refusal leaves prior output intact");
            var noExtra = Converter.Convert(source, pal, Path.Combine(path, "ground-only"), exportExtra: false);
            Check(noExtra[0].Extra!.Exported == false && noExtra[0].Extra!.RgbaPng is null, "optional extra export retains metadata");
        }
        finally
        {
            // Verify the absolute target before removing only our own test directory.
            string expectedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(expectedRoot, StringComparison.Ordinal) || !Path.GetFileName(path).StartsWith("terrain-selftest-", StringComparison.Ordinal))
                throw new IOException("Refusing to remove unexpected self-test path.");
            Directory.Delete(path, recursive: true);
        }
    }

    private static byte[] DecodePng(byte[] png, int width, int height)
    {
        Check(png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "PNG signature");
        using var compressed = new MemoryStream();
        int offset = 8;
        bool header = false, ended = false;
        while (offset < png.Length)
        {
            int count = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset, 4)));
            string type = Encoding.ASCII.GetString(png, offset + 4, 4);
            var bytes = png.AsSpan(offset + 8, count);
            uint crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8 + count, 4));
            Check(crc == PngWriter.Crc32(png.AsSpan(offset + 4, 4), bytes), "PNG chunk CRC " + type);
            if (type == "IHDR")
            {
                Check(BinaryPrimitives.ReadUInt32BigEndian(bytes) == width && BinaryPrimitives.ReadUInt32BigEndian(bytes[4..]) == height && bytes[8] == 8 && bytes[9] == 6, "PNG IHDR RGBA8 dimensions");
                header = true;
            }
            if (type == "IDAT") compressed.Write(bytes);
            if (type == "IEND") ended = true;
            offset += count + 12;
        }
        Check(header && ended && offset == png.Length, "PNG complete chunk stream");
        compressed.Position = 0;
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        var raw = new byte[height * (1 + width * 4)];
        zlib.ReadExactly(raw);
        Check(zlib.ReadByte() == -1, "PNG decompressed stream exact length");
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            Check(raw[y * (width * 4 + 1)] == 0, "PNG filter zero");
            raw.AsSpan(y * (width * 4 + 1) + 1, width * 4).CopyTo(pixels.AsSpan(y * width * 4));
        }
        return pixels;
    }

    private static byte[] Fixture()
    {
        var bytes = new byte[24 + 1852 + 12];
        Set(bytes, 0, 2); Set(bytes, 4, 1); Set(bytes, 8, 60); Set(bytes, 12, 30); Set(bytes, 16, 24);
        int b = 24;
        Set(bytes, b, 30); Set(bytes, b + 4, 15); Set(bytes, b + 8, 1852); Set(bytes, b + 12, 952); Set(bytes, b + 16, 1858);
        Set(bytes, b + 20, 28); Set(bytes, b + 24, -8); Set(bytes, b + 28, 3); Set(bytes, b + 32, 2); Set(bytes, b + 36, unchecked((int)0xCDCDCDCB));
        bytes[b + 40] = 2; bytes[b + 41] = 7; bytes[b + 42] = 4;
        for (int i = 0; i < 9; i++) bytes[b + 43 + i] = (byte)(i + 1);
        for (int i = 0; i < 900; i++) { bytes[b + 52 + i] = (byte)(i % 64); bytes[b + 952 + i] = (byte)(i % 32); }
        for (int i = 0; i < 12; i++) bytes[b + 1852 + i] = (byte)i;
        return bytes;
    }
    private static byte[] PaletteBytes()
    {
        var bytes = new byte[768];
        for (int i = 0; i < 256; i++) bytes[i * 3] = bytes[i * 3 + 1] = bytes[i * 3 + 2] = (byte)(i % 64);
        return bytes;
    }
    private static void Set(byte[] bytes, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), value);
    private static void Bad(byte[] fixture, int offset, int value, string message)
    {
        var bytes = (byte[])fixture.Clone(); Set(bytes, offset, value); Reject(() => TmpReader.Read(bytes), message);
    }
    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException) { checks++; return; }
        throw new InvalidOperationException("Self-test expected rejection: " + message);
    }
    private static void Check(bool result, string message)
    {
        checks++;
        if (!result) throw new InvalidOperationException("Self-test failed: " + message);
    }
}
