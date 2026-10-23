using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace VoxelBaker;

internal static class Png
{
    static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
    public static void Write(string path, int width, int height, byte[] rgba)
    {
        if (width <= 0 || height <= 0 || rgba.Length != checked(width * height * 4)) throw new ArgumentException("Invalid RGBA dimensions.");
        using var output = File.Create(path);
        output.Write(Signature);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; header[9] = 6;
        Chunk(output, "IHDR", header);
        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.SmallestSize, true))
            for (int y = 0; y < height; y++) { z.WriteByte(0); z.Write(rgba, y * width * 4, width * 4); }
        Chunk(output, "IDAT", compressed.ToArray()); Chunk(output, "IEND", []);
    }
    static uint Crc(ReadOnlySpan<byte> data, uint crc = 0xffffffff)
    {
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320);
        }
        return crc;
    }
    static void Chunk(Stream s, string type, byte[] data)
    {
        var tag = Encoding.ASCII.GetBytes(type); Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length); s.Write(word); s.Write(tag); s.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(word, Crc(data, Crc(tag)) ^ 0xffffffff); s.Write(word);
    }
    // Decoder is intentionally bounded and independent of Write's compression path; used by self-test.
    public static (int Width, int Height, byte[] Pixels) ReadOwn(string path)
    {
        var b = new Bytes(File.ReadAllBytes(path)); b.Range(0, 8);
        if (!b.Data.AsSpan(0, 8).SequenceEqual(Signature)) throw new InvalidDataException("Invalid PNG signature.");
        int width = 0, height = 0; long p = 8; bool ended = false;
        using var compressed = new MemoryStream();
        while (p < b.Data.Length)
        {
            b.Range(p, 12); int count = BinaryPrimitives.ReadInt32BigEndian(b.Data.AsSpan((int)p, 4));
            b.Range(p + 8, count + 4L); var tag = b.Data.AsSpan((int)p + 4, 4); var data = b.Data.AsSpan((int)p + 8, count);
            uint crc = BinaryPrimitives.ReadUInt32BigEndian(b.Data.AsSpan((int)p + 8 + count, 4));
            if ((Crc(data, Crc(tag)) ^ 0xffffffff) != crc) throw new InvalidDataException("PNG CRC mismatch.");
            string name = Encoding.ASCII.GetString(tag);
            if (name == "IHDR")
            {
                if (count != 13 || data[8] != 8 || data[9] != 6 || data[12] != 0) throw new InvalidDataException("Unexpected PNG format.");
                width = BinaryPrimitives.ReadInt32BigEndian(data); height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                if (width <= 0 || height <= 0 || (long)width * height > 64_000_000) throw new InvalidDataException("PNG dimensions too large.");
            }
            else if (name == "IDAT") compressed.Write(data);
            else if (name == "IEND") { ended = true; break; }
            p += 12L + count;
        }
        if (!ended || width == 0 || height == 0) throw new InvalidDataException("Incomplete PNG.");
        compressed.Position = 0; using var z = new ZLibStream(compressed, CompressionMode.Decompress);
        var pixels = new byte[checked(width * height * 4)];
        for (int y = 0; y < height; y++)
        {
            if (z.ReadByte() != 0) throw new InvalidDataException("Unexpected PNG filter.");
            z.ReadExactly(pixels.AsSpan(y * width * 4, width * 4));
        }
        if (z.ReadByte() != -1) throw new InvalidDataException("Extra PNG scanline data.");
        return (width, height, pixels);
    }
}
