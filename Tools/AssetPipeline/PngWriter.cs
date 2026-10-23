using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Ra2AssetPipeline;

/// <summary>PNG encoder using only BCL zlib; RGBA8 or grayscale8, filter type 0.</summary>
public static class PngWriter
{
    public const int MaxImagePixels = 16 * 1024 * 1024;
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static void Write(string path, int width, int height, ReadOnlySpan<byte> pixels, int channels)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        Write(stream, width, height, pixels, channels);
    }

    public static void Write(Stream output, int width, int height, ReadOnlySpan<byte> pixels, int channels)
    {
        ShpTs.Require(width > 0 && height > 0 && (long)width * height <= MaxImagePixels, "PNG dimensions exceed supported budget.");
        ShpTs.Require(channels is 1 or 4, "PNG supports one grayscale or four RGBA channels.");
        ShpTs.Require((long)width * height * channels == pixels.Length, "PNG sample buffer length mismatch.");
        output.Write(Signature);
        Span<byte> header = stackalloc byte[13];
        header.Clear();
        BinaryPrimitives.WriteUInt32BigEndian(header[..4], (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.Slice(4, 4), (uint)height);
        header[8] = 8;
        header[9] = channels == 1 ? (byte)0 : (byte)6;
        WriteChunk(output, "IHDR", header);
        using (var chunks = new IdatStream(output))
        {
            using (var zlib = new ZLibStream(chunks, CompressionLevel.Optimal, leaveOpen: true))
            {
                int stride = width * channels;
                for (int y = 0; y < height; y++)
                {
                    zlib.WriteByte(0);
                    zlib.Write(pixels.Slice(y * stride, stride));
                }
            }
        }
        WriteChunk(output, "IEND", []);
    }

    internal static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        uint crc = uint.MaxValue;
        foreach (byte b in type) crc = Step(crc, b);
        foreach (byte b in data) crc = Step(crc, b);
        return ~crc;
    }

    private static uint Step(uint crc, byte b)
    {
        crc ^= b;
        for (int i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320U : 0);
        return crc;
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> bytes)
    {
        Span<byte> size = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(size, (uint)bytes.Length);
        output.Write(size);
        Span<byte> name = stackalloc byte[4];
        Encoding.ASCII.GetBytes(type, name);
        output.Write(name);
        output.Write(bytes);
        BinaryPrimitives.WriteUInt32BigEndian(size, Crc32(name, bytes));
        output.Write(size);
    }

    private sealed class IdatStream(Stream output) : Stream
    {
        private readonly byte[] buffer = new byte[65536];
        private int count;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Write(byte[] bytes, int offset, int length) => Write(bytes.AsSpan(offset, length));
        public override void Write(ReadOnlySpan<byte> bytes)
        {
            while (!bytes.IsEmpty)
            {
                int take = Math.Min(buffer.Length - count, bytes.Length);
                bytes[..take].CopyTo(buffer.AsSpan(count));
                count += take;
                bytes = bytes[take..];
                if (count == buffer.Length) Flush();
            }
        }
        public override void Flush()
        {
            if (count == 0) return;
            WriteChunk(output, "IDAT", buffer.AsSpan(0, count));
            count = 0;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) Flush();
            base.Dispose(disposing);
        }
        public override int Read(byte[] bytes, int offset, int length) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
