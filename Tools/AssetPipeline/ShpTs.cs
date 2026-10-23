using System.Buffers.Binary;

namespace Ra2AssetPipeline;

public sealed record ShpFrame(int Index, int X, int Y, int Width, int Height,
    byte Compression, uint DataOffset, byte[] ReservedHeaderBytes, byte[] Indices,
    int EncodedLength)
{
    public bool HasData => DataOffset != 0;
}

public sealed record ShpSprite(int CanvasWidth, int CanvasHeight, IReadOnlyList<ShpFrame> Frames);

/// <summary>Strict, bounded reader for the TS/RA2 SHP container; no TD/RA1 support.</summary>
public static class ShpTs
{
    public const int MaxInputBytes = 256 * 1024 * 1024;
    public const int MaxFramePixels = 16 * 1024 * 1024;
    public const int MaxTotalPixels = 64 * 1024 * 1024;

    public static ShpSprite Read(ReadOnlySpan<byte> source)
    {
        Require(source.Length is >= 8 and <= MaxInputBytes, "SHP size is outside supported bounds.");
        Require(U16(source, 0) == 0, "Expected TS/RA2 SHP signature (first word must be zero).");
        int canvasWidth = U16(source, 2), canvasHeight = U16(source, 4), count = U16(source, 6);
        Require(canvasWidth > 0 && canvasHeight > 0, "SHP canvas dimensions must be positive.");
        Require(count > 0, "SHP contains no frames.");
        int tableEnd = checked(8 + count * 24);
        Require(tableEnd <= source.Length, "Truncated SHP frame header table.");
        var headers = new Header[count];
        long totalPixels = 0;
        var offsets = new SortedSet<int> { source.Length };
        for (int i = 0; i < count; i++)
        {
            int p = 8 + i * 24;
            int x = U16(source, p), y = U16(source, p + 2);
            int width = U16(source, p + 4), height = U16(source, p + 6);
            byte format = source[p + 8];
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(p + 20, 4));
            Require(format <= 3, $"Frame {i}: unsupported compression {format}.");
            long pixels = (long)width * height;
            Require(pixels <= MaxFramePixels, $"Frame {i}: pixel budget exceeded.");
            totalPixels += pixels;
            Require(totalPixels <= MaxTotalPixels, "SHP total decoded pixel budget exceeded.");
            if (offset != 0)
            {
                Require(width > 0 && height > 0, $"Frame {i}: nonempty data requires positive dimensions.");
                Require(offset >= tableEnd && offset < source.Length, $"Frame {i}: data offset is outside payload.");
                offsets.Add((int)offset);
            }
            headers[i] = new Header(x, y, width, height, format, offset, source.Slice(p + 9, 11).ToArray());
        }

        // The next distinct offset bounds a frame. Shared offsets are legal; overlap into a
        // different frame is rejected even when the whole file still has readable bytes.
        var sortedOffsets = offsets.ToArray();
        var frames = new ShpFrame[count];
        for (int i = 0; i < count; i++)
        {
            var h = headers[i];
            var pixels = new byte[h.Width * h.Height];
            int consumed = 0;
            if (h.Offset != 0)
            {
                int position = Array.BinarySearch(sortedOffsets, (int)h.Offset);
                int end = sortedOffsets[position + 1];
                var payload = source.Slice((int)h.Offset, end - (int)h.Offset);
                consumed = Decode(payload, pixels, h.Width, h.Height, h.Format, i);
            }
            frames[i] = new ShpFrame(i, h.X, h.Y, h.Width, h.Height, h.Format, h.Offset,
                h.Reserved, pixels, consumed);
        }
        return new ShpSprite(canvasWidth, canvasHeight, frames);
    }

    private static int Decode(ReadOnlySpan<byte> payload, Span<byte> pixels,
        int width, int height, byte format, int frame)
    {
        int p = 0;
        if (format is 0 or 1)
        {
            Require(payload.Length >= pixels.Length, $"Frame {frame}: truncated raw pixels.");
            payload[..pixels.Length].CopyTo(pixels);
            return pixels.Length;
        }
        if (format == 2)
        {
            Require(payload.Length >= 2, $"Frame {frame}: missing format-2 row length.");
            int storedLength = U16(payload, 0);
            Require(storedLength >= 2, $"Frame {frame}: invalid format-2 row length.");
            int rowBytes = storedLength - 2;
            Require(rowBytes <= width, $"Frame {frame}: format-2 row exceeds frame width.");
            Require(2L + (long)rowBytes * height <= payload.Length, $"Frame {frame}: truncated format-2 rows.");
            p = 2;
            for (int y = 0; y < height; y++, p += rowBytes)
                payload.Slice(p, rowBytes).CopyTo(pixels.Slice(y * width, rowBytes));
            return p;
        }
        for (int y = 0; y < height; y++)
        {
            Require(p <= payload.Length - 2, $"Frame {frame}, row {y}: missing RLE row length.");
            int length = U16(payload, p);
            Require(length >= 2, $"Frame {frame}, row {y}: invalid RLE row length.");
            Require(length <= payload.Length - p, $"Frame {frame}, row {y}: truncated RLE row.");
            int rowEnd = p + length;
            p += 2;
            int x = 0;
            while (p < rowEnd)
            {
                byte value = payload[p++];
                if (value != 0)
                {
                    Require(x < width, $"Frame {frame}, row {y}: literal exceeds row width.");
                    pixels[y * width + x++] = value;
                    continue;
                }
                Require(p < rowEnd, $"Frame {frame}, row {y}: dangling zero-run marker.");
                int run = payload[p++];
                Require(run > 0, $"Frame {frame}, row {y}: zero-length RLE run.");
                int remaining = width - x;
                if (run > remaining)
                {
                    // Some TS/RA2 writers encode an inclusive terminal transparent skip
                    // ending at width+1. Clip exactly that final zero only; never write it
                    // into the next row or accept nonterminal / larger overruns.
                    Require(p == rowEnd && run == remaining + 1,
                        $"Frame {frame}, row {y}: zero-run exceeds row width.");
                    x = width;
                }
                else x += run;
            }
            // Some writers omit trailing transparent pixels; new arrays already contain zero.
        }
        return p;
    }

    private static int U16(ReadOnlySpan<byte> data, int p) => BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(p, 2));
    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    private sealed record Header(int X, int Y, int Width, int Height, byte Format, uint Offset, byte[] Reserved);
}
