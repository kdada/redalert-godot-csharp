using System.Buffers.Binary;

namespace Ra2TerrainPipeline;

public sealed record TmpBlock(
    int X, int Y, uint ExtraOffset, uint ZOffset, uint ExtraZOffset,
    int ExtraX, int ExtraY, int ExtraWidth, int ExtraHeight, uint Flags,
    byte Height, byte TerrainType, byte RampType, byte[] RadarLeft, byte[] RadarRight,
    byte[] Reserved, byte[] DiamondIndices, byte[] ZData, byte[]? ExtraIndices,
    byte[]? ExtraZData, IReadOnlyList<string> Warnings);

public sealed record TmpFrame(int Index, int CellX, int CellY, uint SourceOffset, TmpBlock Block);

public sealed record TmpDocument(int BlocksX, int BlocksY, IReadOnlyList<TmpFrame> Frames,
    IReadOnlyList<int> EmptyIndices);

/// <summary>Independent little-endian reader for the documented RA2 60x30 TMP layout.</summary>
public static class TmpReader
{
    public const int TileWidth = 60;
    public const int TileHeight = 30;
    public const int BlockHeaderSize = 52;
    public const int DiamondLength = 900;
    public const int CanonicalZOffset = BlockHeaderSize + DiamondLength;
    public const int CanonicalExtraOffset = CanonicalZOffset + DiamondLength;
    public const int MaxFileBytes = 256 * 1024 * 1024;
    public const int MaxBlocks = 65536;
    public const int MaxExtraPixels = 16 * 1024 * 1024;

    public static byte[] ReadFile(string path, int maxBytes = MaxFileBytes)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Require(input.Length <= maxBytes, $"Input exceeds {maxBytes} byte limit: {path}");
        var bytes = new byte[(int)input.Length];
        input.ReadExactly(bytes);
        Require(input.ReadByte() == -1, "Input length changed while reading.");
        return bytes;
    }

    public static TmpDocument Read(ReadOnlySpan<byte> data)
    {
        Require(data.Length <= MaxFileBytes, "TMP exceeds supported file budget.");
        Range(data, 0, 16, "TMP file header");
        int blocksX = Int(data, 0);
        int blocksY = Int(data, 4);
        int width = Int(data, 8);
        int height = Int(data, 12);
        Require(blocksX > 0 && blocksY > 0 && (long)blocksX * blocksY <= MaxBlocks,
            $"Invalid TMP block grid {blocksX}x{blocksY}; supported total is 1..{MaxBlocks}.");
        Require(width == TileWidth && height == TileHeight,
            $"Unsupported TMP tile dimensions {width}x{height}; require RA2 60x30.");
        int count = blocksX * blocksY;
        int tableEnd = 16 + count * 4;
        Range(data, 16, count * 4, "TMP block offset table");
        var offsets = new uint[count];
        for (int i = 0; i < count; i++)
        {
            uint offset = UInt(data, 16 + i * 4);
            offsets[i] = offset;
            if (offset == 0) continue;
            Require(offset >= tableEnd && (long)offset + BlockHeaderSize <= data.Length,
                $"Block {i}: header offset {offset} is inside the table or outside the input.");
        }

        // Bound each block by the next distinct nonzero table entry. This catches
        // truncated payloads that would otherwise read into a later block's header.
        uint[] starts = offsets.Where(x => x != 0).Distinct().Order().ToArray();
        var decoded = new Dictionary<uint, TmpBlock>();
        for (int i = 0; i < starts.Length; i++)
        {
            int start = (int)starts[i];
            int end = i + 1 < starts.Length ? (int)starts[i + 1] : data.Length;
            decoded.Add(starts[i], ReadBlock(data.Slice(start, end - start), starts[i]));
        }

        var frames = new List<TmpFrame>();
        var empty = new List<int>();
        for (int i = 0; i < count; i++)
        {
            if (offsets[i] == 0) empty.Add(i);
            else frames.Add(new TmpFrame(i, i % blocksX, i / blocksX, offsets[i], decoded[offsets[i]]));
        }
        return new TmpDocument(blocksX, blocksY, frames, empty);
    }

    private static TmpBlock ReadBlock(ReadOnlySpan<byte> data, uint sourceOffset)
    {
        string label = $"Block at file offset {sourceOffset}";
        Range(data, 0, BlockHeaderSize, label + " header");
        int x = Int(data, 0), y = Int(data, 4);
        uint extraOffset = UInt(data, 8), zOffset = UInt(data, 12), extraZOffset = UInt(data, 16);
        int extraX = Int(data, 20), extraY = Int(data, 24);
        int extraW = Int(data, 28), extraH = Int(data, 32);
        uint flags = UInt(data, 36);
        bool hasExtra = (flags & 1) != 0;
        var warnings = new List<string>();
        Range(data, BlockHeaderSize, DiamondLength, label + " diamond indices");

        // Both format references store the base Z diamond after the color diamond.
        // Keep the canonical layout explicit rather than guessing at other formats.
        Require(zOffset == CanonicalZOffset, $"{label}: unsupported/invalid Z offset {zOffset}; expected 952.");
        Range(data, CanonicalZOffset, DiamondLength, label + " Z diamond");
        byte[]? extra = null, extraZ = null;
        if (hasExtra)
        {
            Require(extraW > 0 && extraH > 0 && (long)extraW * extraH <= MaxExtraPixels,
                $"{label}: invalid extra dimensions {extraW}x{extraH}.");
            int pixels = extraW * extraH;
            // A nonzero extra offset is interpreted relative to this block header.
            // Some tools leave the field zero: the sequential location is then used
            // with a manifest warning, as in the two published readers.
            int extraStart = extraOffset == 0 ? CanonicalExtraOffset : CheckedOffset(extraOffset, label);
            Require(extraStart >= CanonicalExtraOffset, $"{label}: extra indices overlap base image/Z data.");
            Range(data, extraStart, pixels, label + " extra indices");
            extra = data.Slice(extraStart, pixels).ToArray();
            if (extraOffset == 0) warnings.Add("extraOffset is zero: extra indices use sequential offset 1852.");

            if (extraZOffset != 0)
            {
                int zStart = CheckedOffset(extraZOffset, label);
                Require((long)zStart >= (long)extraStart + pixels, $"{label}: extra Z overlaps extra indices.");
                Range(data, zStart, pixels, label + " extra Z");
                extraZ = data.Slice(zStart, pixels).ToArray();
            }
            else if (data.Length - extraStart - pixels >= pixels)
            {
                // Published OpenRA reader consumes a second rectangular payload
                // even when the extra-Z pointer is zero. Preserve it, label the inference.
                extraZ = data.Slice(extraStart + pixels, pixels).ToArray();
                warnings.Add("extraZOffset is zero: extra Z inferred from the following rectangle; its semantics are unverified.");
            }
            else
            {
                Require(data.Length == extraStart + pixels, $"{label}: truncated sequential extra Z rectangle.");
                warnings.Add("No extra Z payload: extra color layer is valid, depth semantics are unavailable.");
            }
        }
        else
        {
            // Width/position and unused pointer fields may contain producer garbage
            // (as do high flag bits). Preserve them, but never allocate from them.
            if (extraOffset != 0 || extraZOffset != 0)
                warnings.Add("Extra flag is clear: unused extra pointer fields are retained, not dereferenced.");
        }
        if ((flags & 4) != 0)
            warnings.Add("Damaged-data flag is set; damaged variants are not decoded by this converter.");
        return new TmpBlock(x, y, extraOffset, zOffset, extraZOffset, extraX, extraY, extraW, extraH, flags,
            data[40], data[41], data[42], data.Slice(43, 3).ToArray(), data.Slice(46, 3).ToArray(),
            data.Slice(49, 3).ToArray(), data.Slice(BlockHeaderSize, DiamondLength).ToArray(),
            data.Slice(CanonicalZOffset, DiamondLength).ToArray(), extra, extraZ, warnings);
    }

    /// <summary>Widths are 4,8,...,60,...,8,4,0 (900 encoded pixels, empty final row).</summary>
    public static int RowWidth(int y)
    {
        Require(y >= 0 && y < TileHeight, "Diamond row is outside 0..29.");
        return y == TileHeight - 1 ? 0 : 4 * (15 - Math.Abs(14 - y));
    }

    private static int CheckedOffset(uint offset, string label)
    {
        Require(offset <= int.MaxValue, $"{label}: relative offset exceeds supported range.");
        return (int)offset;
    }
    private static int Int(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset, 4));
    private static uint UInt(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4));
    private static void Range(ReadOnlySpan<byte> bytes, int start, int count, string label) =>
        Require(start >= 0 && count >= 0 && (long)start + count <= bytes.Length, $"{label}: truncated or invalid byte range ({start}+{count}, available {bytes.Length}).");
    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
