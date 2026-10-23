using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace VoxelBaker;

// Independently implemented from the published Westwood layout. All offsets are checked
// before dereferencing; malformed files fail with InvalidDataException, not partial output.
internal sealed class Bytes(byte[] data)
{
    public readonly byte[] Data = data;
    public void Range(long offset, long count)
    {
        if (offset < 0 || count < 0 || offset > Data.Length || count > Data.Length - offset)
            throw new InvalidDataException($"Range {offset}+{count} exceeds file length {Data.Length}.");
    }
    public int I32(long p) { Range(p, 4); return BinaryPrimitives.ReadInt32LittleEndian(Data.AsSpan((int)p, 4)); }
    public uint U32(long p) { Range(p, 4); return BinaryPrimitives.ReadUInt32LittleEndian(Data.AsSpan((int)p, 4)); }
    public float F32(long p)
    {
        float value = BitConverter.Int32BitsToSingle(I32(p));
        if (!float.IsFinite(value)) throw new InvalidDataException("Non-finite float.");
        return value;
    }
    public byte U8(long p) { Range(p, 1); return Data[(int)p]; }
    public string Text(long p, int count)
    {
        Range(p, count);
        return Encoding.ASCII.GetString(Data, (int)p, count).Split('\0')[0];
    }
    public static Bytes Load(string path)
    {
        if (new FileInfo(path).Length > 128 * 1024 * 1024) throw new InvalidDataException("Input exceeds 128 MiB limit.");
        return new Bytes(File.ReadAllBytes(path));
    }
}

internal sealed class Limb
{
    public required string Name { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }
    public required int Z { get; init; }
    public required float Scale { get; init; }
    public required Vector3 Min { get; init; }
    public required Vector3 Max { get; init; }
    public required byte NormalType { get; init; }
    public required byte[] Colors { get; init; }
    public required byte[] Normals { get; init; }
    public required bool[] Occupied { get; init; }
    public int Count { get; set; }
    public int Index(int x, int y, int z) => (z * Y + y) * X + x;
    public bool Has(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < X && y < Y && z < Z && Occupied[Index(x, y, z)];
    public Vector3 Step => (Max - Min) / new Vector3(X, Y, Z);
}

internal static class Vxl
{
    public static Limb[] Read(Bytes b)
    {
        b.Range(0, 802);
        if (b.Text(0, 16) != "Voxel Animation") throw new InvalidDataException("Invalid VXL signature.");
        uint count = b.U32(20), tails = b.U32(24), bodySize = b.U32(28);
        if (count == 0 || count > 256 || tails != count) throw new InvalidDataException("Invalid VXL limb counts.");
        long body = 802 + 28L * count, footer = body + bodySize;
        b.Range(802, 28L * count); b.Range(body, bodySize); b.Range(footer, 92L * count);
        var limbs = new Limb[count];
        long totalCells = 0;
        for (int n = 0; n < count; n++)
        {
            long f = footer + 92L * n;
            long startTable = b.U32(f), endTable = b.U32(f + 4), spanData = b.U32(f + 8);
            int sx = b.U8(f + 88), sy = b.U8(f + 89), sz = b.U8(f + 90), columns = sx * sy;
            if (sx == 0 || sy == 0 || sz == 0) throw new InvalidDataException("Empty VXL dimensions.");
            totalCells += (long)columns * sz;
            if (totalCells > 32_000_000) throw new InvalidDataException("VXL exceeds 32 million cell budget.");
            void BodyRange(long p, long length)
            {
                if (p < 0 || p > bodySize || length < 0 || length > bodySize - p)
                    throw new InvalidDataException("VXL limb offset exceeds body.");
            }
            BodyRange(startTable, columns * 4L); BodyRange(endTable, columns * 4L); BodyRange(spanData, 0);
            var min = new Vector3(b.F32(f + 64), b.F32(f + 68), b.F32(f + 72));
            var max = new Vector3(b.F32(f + 76), b.F32(f + 80), b.F32(f + 84));
            float scale = b.F32(f + 12);
            if (scale <= 0 || scale > 1000 || max.X <= min.X || max.Y <= min.Y || max.Z <= min.Z)
                throw new InvalidDataException("Invalid VXL bounds or scale.");
            // Validate all twelve embedded affine transform values even though HVA supplies poses.
            for (int t = 0; t < 12; t++) _ = b.F32(f + 16 + t * 4);
            var limb = new Limb { Name = b.Text(802 + n * 28L, 16), X = sx, Y = sy, Z = sz, Scale = scale,
                Min = min, Max = max, NormalType = b.U8(f + 91), Colors = new byte[columns * sz],
                Normals = new byte[columns * sz], Occupied = new bool[columns * sz] };
            for (int c = 0; c < columns; c++)
            {
                int start = b.I32(body + startTable + 4L * c), end = b.I32(body + endTable + 4L * c);
                if (start == -1 && end == -1) continue;
                if (start < 0 || end < start) throw new InvalidDataException("Invalid VXL column offsets.");
                BodyRange(spanData + start, (long)end - start + 1);
                long p = body + spanData + start, limit = body + spanData + end + 1;
                int z = 0;
                while (z < sz)
                {
                    if (p + 2 > limit) throw new InvalidDataException("Truncated VXL span header.");
                    int skip = b.U8(p++), run = b.U8(p++);
                    if (skip + run == 0 || z + skip + run > sz || p + 2L * run + 1 > limit)
                        throw new InvalidDataException("Invalid VXL span length.");
                    z += skip;
                    for (int k = 0; k < run; k++, z++)
                    {
                        int i = limb.Index(c % sx, c / sx, z);
                        limb.Colors[i] = b.U8(p++); limb.Normals[i] = b.U8(p++); limb.Occupied[i] = true; limb.Count++;
                    }
                    if (b.U8(p++) != run) throw new InvalidDataException("VXL span duplicate count mismatch.");
                }
            }
            limbs[n] = limb;
        }
        return limbs;
    }
}

internal sealed record Pose(string[] Names, float[][] Matrices, int Frames)
{
    public static readonly float[] Identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0];
    public float[] For(int limb, int frame)
    {
        // Retail VXL/HVA pairs bind sections by position; labels sometimes differ (e.g. htk).
        if (limb < 0 || limb >= Names.Length) throw new InvalidDataException("HVA limb index outside section table.");
        if (frame < 0 || frame >= Frames) throw new InvalidDataException($"HVA frame {frame} outside 0..{Frames - 1}.");
        return Matrices[frame * Names.Length + limb];
    }
    public static Pose Read(Bytes b)
    {
        b.Range(0, 24);
        uint frames = b.U32(16), limbs = b.U32(20);
        if (frames == 0 || frames > 4096 || limbs == 0 || limbs > 256) throw new InvalidDataException("Invalid HVA counts.");
        long data = 24 + limbs * 16L;
        b.Range(24, limbs * 16L); b.Range(data, frames * limbs * 48L);
        var names = new string[limbs];
        for (int i = 0; i < limbs; i++) names[i] = b.Text(24 + 16L * i, 16);
        var matrices = new float[frames * limbs][];
        for (int i = 0; i < matrices.Length; i++)
        {
            var m = new float[12];
            for (int j = 0; j < 12; j++) m[j] = b.F32(data + i * 48L + j * 4);
            float determinant = m[0] * (m[5] * m[10] - m[6] * m[9]) - m[1] * (m[4] * m[10] - m[6] * m[8]) + m[2] * (m[4] * m[9] - m[5] * m[8]);
            if (!float.IsFinite(determinant) || Math.Abs(determinant) < 1e-8) throw new InvalidDataException("Singular HVA transform.");
            matrices[i] = m;
        }
        return new Pose(names, matrices, (int)frames);
    }
}

internal static class Palette
{
    public static byte[] Read(string path, int bits)
    {
        byte[] input = File.ReadAllBytes(path);
        if (input.Length != 768) throw new InvalidDataException("PAL must contain exactly 256 RGB triples (768 bytes).");
        int max = (1 << bits) - 1;
        if (input.Any(x => x > max)) throw new InvalidDataException($"PAL channel exceeds {bits}-bit range; use --palette-bits 8 for RGB888.");
        return input.Select(x => (byte)((x * 255 + max / 2) / max)).ToArray();
    }
}
