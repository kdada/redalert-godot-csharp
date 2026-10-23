using System.Security.Cryptography;
using System.Text.Json;
using Ra2AssetPipeline;

namespace Ra2TerrainPipeline;

public sealed record PixelOffset(long X, long Y);
public sealed record PixelSize(int Width, int Height);
public sealed record LayerManifest(string Kind, PixelSize Size, PixelOffset Offset,
    PixelOffset OffsetFromDiamond, bool Exported, string? RgbaPng, string? IndicesFile,
    string IndicesSha256, int IndexCount, string? ZFile, string? ZSha256);
public sealed record FrameManifest(int Index, int CellX, int CellY, uint SourceOffset,
    PixelOffset Offset, PixelOffset GridOffset, PixelOffset HeightAdjustedOffset,
    byte Height, byte TerrainType, byte RampType, uint Flags, string FlagsHex,
    uint ExtraOffset, uint ZOffset, uint ExtraZOffset, byte[] RadarLeft, byte[] RadarRight,
    byte[] Reserved, LayerManifest Diamond, LayerManifest? Extra, IReadOnlyList<string> Warnings);

public static class Converter
{
    public static readonly string[] FormatSources =
    [
        "https://github.com/tcrs/ra2ff/blob/3800e3636c43d2a8a7331b2d7dbcd888c215d6af/ra2ff.txt",
        "https://github.com/OpenRA/OpenRA/blob/e180e3225f3930272dd451917467631618f8976f/OpenRA.Mods.Cnc/SpriteLoaders/TmpTSLoader.cs"
    ];
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IReadOnlyList<FrameManifest> Convert(string tmpPath, string palPath, string output,
        int paletteDepth = 6, bool exportExtra = true)
    {
        byte[] input = TmpReader.ReadFile(tmpPath);
        var tmp = TmpReader.Read(input);
        byte[] palBytes = TmpReader.ReadFile(palPath, 768);
        var palette = Palette.Read(palBytes, paletteDepth);
        string destination = Path.GetFullPath(output);
        EnsureEmptyOutput(destination);
        var manifests = new List<FrameManifest>();
        int maxHeight = tmp.Frames.Count == 0 ? 0 : tmp.Frames.Max(frame => frame.Block.Height);
        foreach (var frame in tmp.Frames)
        {
            var block = frame.Block;
            string stem = $"tile-{frame.Index:D4}";
            var offset = new PixelOffset(block.X, block.Y);
            var ground = WriteLayer(destination, stem + ".diamond", "diamond", TmpReader.TileWidth,
                TmpReader.TileHeight, offset, new PixelOffset(0, 0), block.DiamondIndices, block.ZData,
                RenderDiamond(block.DiamondIndices, palette), export: true);
            LayerManifest? extra = null;
            if (block.ExtraIndices is not null)
            {
                extra = WriteLayer(destination, stem + ".extra", "extra", block.ExtraWidth, block.ExtraHeight,
                    new PixelOffset(block.ExtraX, block.ExtraY),
                    new PixelOffset((long)block.ExtraX - block.X, (long)block.ExtraY - block.Y),
                    block.ExtraIndices, block.ExtraZData,
                    exportExtra ? RenderExtra(block.ExtraIndices, palette) : null, exportExtra);
            }
            manifests.Add(new FrameManifest(frame.Index, frame.CellX, frame.CellY, frame.SourceOffset,
                offset, new PixelOffset((long)(frame.CellX - frame.CellY) * 30, (long)(frame.CellX + frame.CellY) * 15),
                new PixelOffset(block.X, (long)block.Y + (maxHeight - block.Height) * 15),
                block.Height, block.TerrainType, block.RampType, block.Flags, $"0x{block.Flags:X8}",
                block.ExtraOffset, block.ZOffset, block.ExtraZOffset, block.RadarLeft, block.RadarRight,
                block.Reserved, ground, extra, block.Warnings));
        }
        var manifest = new
        {
            schemaVersion = 1,
            format = "RA2 TMP 60x30",
            source = new { fileName = Path.GetFileName(tmpPath), byteLength = input.Length, sha256 = Hash(input) },
            palette = new { fileName = Path.GetFileName(palPath), componentBits = paletteDepth, sha256 = Hash(palBytes), expansion = paletteDepth == 6 ? "channel << 2 (63 -> 252)" : "identity" },
            template = new { tmp.BlocksX, tmp.BlocksY, tileWidth = 60, tileHeight = 30, maxHeight, frameCount = tmp.Frames.Count, emptyIndices = tmp.EmptyIndices },
            conventions = new
            {
                indexHash = "SHA256 over original packed indices, in source order; diamond exactly 900 bytes, extra width*height bytes.",
                diamondAlpha = "All 900 encoded diamond pixels are opaque, including palette index 0; outside the shape is transparent black. Last row is empty.",
                extraAlpha = "Index 0 is transparent black; other extra pixels are opaque. Extra is a separate layer, not composited onto the diamond.",
                offset = "Raw signed top-left pixel coordinates from block header, relative to template origin; unmodified.",
                gridOffset = "Isometric top-left implied by table cell: ((cellX-cellY)*30, (cellX+cellY)*15).",
                heightAdjustedOffset = "Raw offset with Y += (template.maxHeight - height)*15, per ra2ff description; supplied separately, no resampling.",
                extraOffset = "Raw template pixel coordinates; offsetFromDiamond = extra.offset - diamond.offset. Same height adjustment as base.",
                terrainType = "Raw block terrain index byte; theater tile-set IDs and terrain name/rules mappings are not present in a standalone TMP.",
                zData = "Raw depth bytes preserved; no claims about renderer depth or damaged-data semantics."
            },
            formatSources = FormatSources,
            frames = manifests
        };
        using var manifestFile = new FileStream(Path.Combine(destination, "manifest.json"), FileMode.CreateNew, FileAccess.Write);
        JsonSerializer.Serialize(manifestFile, manifest, JsonOptions);
        return manifests;
    }

    private static LayerManifest WriteLayer(string output, string stem, string kind, int width, int height,
        PixelOffset offset, PixelOffset relativeOffset, byte[] indices, byte[]? z,
        byte[]? rgba, bool export)
    {
        string? pngName = export ? stem + ".png" : null;
        string? indicesName = export ? stem + ".indices.bin" : null;
        string? zName = export && z is not null ? stem + ".z.bin" : null;
        if (export)
        {
            PngWriter.Write(Path.Combine(output, pngName!), width, height, rgba!, 4);
            WriteNew(Path.Combine(output, indicesName!), indices);
            if (zName is not null) WriteNew(Path.Combine(output, zName), z!);
        }
        return new LayerManifest(kind, new PixelSize(width, height), offset, relativeOffset, export,
            pngName, indicesName, Hash(indices), indices.Length, zName, z is null ? null : Hash(z));
    }

    public static byte[] RenderDiamond(ReadOnlySpan<byte> indices, Palette palette)
    {
        TmpReader.Require(indices.Length == TmpReader.DiamondLength, "Diamond requires exactly 900 palette indices.");
        var rgba = new byte[TmpReader.TileWidth * TmpReader.TileHeight * 4];
        int source = 0;
        for (int y = 0; y < TmpReader.TileHeight; y++)
        {
            int width = TmpReader.RowWidth(y);
            int left = (TmpReader.TileWidth - width) / 2;
            for (int x = 0; x < width; x++)
                Put(rgba, (y * TmpReader.TileWidth + left + x) * 4, palette.Colors[indices[source++]]);
        }
        TmpReader.Require(source == TmpReader.DiamondLength, "Diamond row geometry did not consume 900 indices.");
        return rgba;
    }

    public static byte[] RenderExtra(ReadOnlySpan<byte> indices, Palette palette)
    {
        TmpReader.Require(indices.Length <= TmpReader.MaxExtraPixels, "Extra image exceeds pixel budget.");
        var rgba = new byte[indices.Length * 4];
        for (int i = 0; i < indices.Length; i++)
            if (indices[i] != 0) Put(rgba, i * 4, palette.Colors[indices[i]]);
        return rgba;
    }

    private static void Put(byte[] output, int offset, PaletteColor color)
    {
        output[offset] = color.R;
        output[offset + 1] = color.G;
        output[offset + 2] = color.B;
        output[offset + 3] = 255;
    }

    public static string Hash(ReadOnlySpan<byte> bytes) => ConvertHash(SHA256.HashData(bytes));
    private static string ConvertHash(byte[] hash) => System.Convert.ToHexString(hash).ToLowerInvariant();
    private static void WriteNew(string path, ReadOnlySpan<byte> bytes)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        file.Write(bytes);
    }
    private static void EnsureEmptyOutput(string destination)
    {
        if (File.Exists(destination)) throw new IOException("Output path is a file; require a new or empty directory.");
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            throw new IOException("Output directory is not empty; refusing to overwrite existing files.");
        Directory.CreateDirectory(destination);
        if ((File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Output directory must not be a symbolic link.");
    }
}
