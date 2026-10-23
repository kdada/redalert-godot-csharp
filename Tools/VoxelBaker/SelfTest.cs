using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace VoxelBaker;

internal static class SelfTest
{
    public static void Run()
    {
        string root = Environment.GetEnvironmentVariable("TMPDIR") ?? Environment.GetEnvironmentVariable("XDG_CACHE_HOME") ?? throw new InvalidOperationException("Set TMPDIR or XDG_CACHE_HOME.");
        root = Path.GetFullPath(root);
        string temp = Path.Combine(root, "voxel-baker-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        int checks = 0;
        try
        {
            byte[] valid = Fixture();
            var limbs = Vxl.Read(new Bytes(valid));
            Check(limbs.Length == 1 && limbs[0].Count > 40 && limbs[0].Colors.Contains((byte)16), "VXL roundtrip data");
            for (int length = 0; length < valid.Length; length += 17) Reject(() => Vxl.Read(new Bytes(valid[..length])), "VXL truncation");
            byte[] bad = (byte[])valid.Clone(); Put(bad, 28, uint.MaxValue); Reject(() => Vxl.Read(new Bytes(bad)), "VXL body overflow");
            bad = (byte[])valid.Clone(); Put(bad, 802 + 28, int.MaxValue); Reject(() => Vxl.Read(new Bytes(bad)), "VXL column overflow");
            bad = (byte[])valid.Clone(); bad[802 + 28 + 7 * 5 * 8 + 1] = 255; Reject(() => Vxl.Read(new Bytes(bad)), "VXL span overflow");
            byte[] hva = HvaFixture(); var pose = Pose.Read(new Bytes(hva));
            Check(pose.For(0, 0)[3] == 2 && pose.Frames == 1, "HVA names/matrix order");
            Reject(() => pose.For(0, 1), "HVA frame bounds");
            Reject(() => Pose.Read(new Bytes(hva[..^1])), "HVA truncation");
            bad = (byte[])hva.Clone(); Put(bad, 16, uint.MaxValue); Reject(() => Pose.Read(new Bytes(bad)), "HVA count overflow");
            bad = (byte[])hva.Clone(); Array.Clear(bad, 40, 48); Reject(() => Pose.Read(new Bytes(bad)), "HVA singular matrix");
            bad = (byte[])hva.Clone(); Put(bad, 40, 0x7fc00000); Reject(() => Pose.Read(new Bytes(bad)), "HVA nonfinite matrix");
            var faces = Renderer.BuildFaces(limbs, pose, 0);
            Reject(() => pose.For(-1, 0), "HVA section bounds");
            Check(Renderer.BuildFaces(limbs, pose with {Names=["MDUMMY01"]}, 0).SequenceEqual(faces), "HVA binds by section index despite different retail labels");
            var part = new Part("body", Path.Combine(temp, "fixture.vxl"), Path.Combine(temp, "fixture.hva"), limbs, faces);
            File.WriteAllBytes(part.Source, valid); File.WriteAllBytes(part.Hva!, hva);
            var palette = new byte[768];
            for (int i = 1; i < 256; i++) { palette[i * 3] = (byte)i; palette[i * 3 + 1] = (byte)(255 - i); palette[i * 3 + 2] = (byte)(i / 2); }
            var o = new RenderOptions(32, 12, 30, 0, 8, 4, 16, 16, 0);
            Canvas canvas = Renderer.Fit([part], o, null);
            var signatures = new HashSet<string>();
            for (int d = 0; d < 32; d++)
            {
                var tile = Renderer.Render(faces, canvas, o, d, palette);
                Check(tile.OpaquePixels > 0, $"facing {d} nonempty");
                signatures.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(tile.Rgba)));
            }
            Check(signatures.Count >= 24, "facings contain distinct 3D projections");
            Program.Bake(temp, "test", [part], palette, null, 8, canvas, o);
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(temp, "test.json")));
            var atlas = json.RootElement.GetProperty("atlas"); int w = atlas.GetProperty("width").GetInt32(), h = atlas.GetProperty("height").GetInt32();
            var decoded = Png.ReadOwn(Path.Combine(temp, "test.body.png"));
            Check(decoded.Width == w && decoded.Height == h, "PNG/metadata dimensions");
            var indexImage = Png.ReadOwn(Path.Combine(temp, "test.body.indices.png"));
            var maskImage = Png.ReadOwn(Path.Combine(temp, "test.body.team.png"));
            byte[] indices = File.ReadAllBytes(Path.Combine(temp, "test.body.indices.bin"));
            byte[] normals = File.ReadAllBytes(Path.Combine(temp, "test.body.normals.bin"));
            byte[] alpha = File.ReadAllBytes(Path.Combine(temp, "test.body.alpha.bin"));
            byte[] team = File.ReadAllBytes(Path.Combine(temp, "test.body.team.bin"));
            Check(indices.Length == w * h && normals.Length == indices.Length && alpha.Length == indices.Length, "raw/metadata lengths");
            for (int i = 0; i < indices.Length; i++)
            {
                if (indexImage.Pixels[i * 4] != indices[i] || indexImage.Pixels[i * 4 + 3] != alpha[i] || decoded.Pixels[i * 4 + 3] != alpha[i]) throw new InvalidOperationException("Index/alpha PNG/raw mismatch.");
                byte expected = alpha[i] != 0 && indices[i] >= 16 && indices[i] <= 31 ? (byte)(indices[i] - 15) : (byte)0;
                if (team[i] != expected || (maskImage.Pixels[i * 4 + 3] != 0) != (expected != 0)) throw new InvalidOperationException("Team mask mismatch.");
            }
            Check(true, "PNG index/mask payloads match raw bytes");
            Check(json.RootElement.GetProperty("parts")[0].GetProperty("frames").GetArrayLength() == 32, "metadata facing count");
            Console.WriteLine($"SELF-TEST PASS: {checks} checks; 32 nonempty facings, {signatures.Count} distinct projections; bounded parsers, PNG CRC/roundtrip, metadata and raw masks consistent.");
        }
        finally
        {
            string verified = Path.GetFullPath(temp);
            if (Path.GetDirectoryName(verified) == root.TrimEnd(Path.DirectorySeparatorChar) && Path.GetFileName(verified).StartsWith("voxel-baker-selftest-", StringComparison.Ordinal)) Directory.Delete(verified, true);
        }
        void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException("Self-test failed: " + label); checks++; }
        void Reject(Action action, string label)
        {
            try { action(); } catch (InvalidDataException) { checks++; return; }
            throw new InvalidOperationException("Self-test failed to reject: " + label);
        }
    }
    static void Put(byte[] b, int p, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(p, 4), v);
    static byte[] HvaFixture()
    {
        using var s = new MemoryStream(); using var w = new BinaryWriter(s);
        w.Write(new byte[16]); w.Write(1); w.Write(1); var name = new byte[16]; Encoding.ASCII.GetBytes("body").CopyTo(name, 0); w.Write(name);
        foreach (float v in new float[] { 1, 0, 0, 2, 0, 1, 0, 0, 0, 0, 1, 0 }) w.Write(v);
        return s.ToArray();
    }
    static byte[] Fixture()
    {
        const int sx = 7, sy = 5, sz = 4, columns = sx * sy;
        using var spans = new MemoryStream(); using var sw = new BinaryWriter(spans);
        var starts = new int[columns]; var ends = new int[columns];
        for (int y = 0; y < sy; y++) for (int x = 0; x < sx; x++)
        {
            int c = y * sx + x;
            if (x > 4 && y > 1) { starts[c] = ends[c] = -1; continue; }
            int height = x < 3 ? 4 : 2;
            starts[c] = (int)spans.Position; sw.Write((byte)0); sw.Write((byte)height);
            for (int z = 0; z < height; z++) { sw.Write((byte)(16 + (x + y + z) % 16)); sw.Write((byte)(x + z)); }
            sw.Write((byte)height);
            if (height < sz) { sw.Write((byte)(sz - height)); sw.Write((byte)0); sw.Write((byte)0); }
            ends[c] = (int)spans.Position - 1;
        }
        using var s = new MemoryStream(); using var w = new BinaryWriter(s);
        var signature = new byte[16]; Encoding.ASCII.GetBytes("Voxel Animation").CopyTo(signature, 0); w.Write(signature);
        w.Write(1); w.Write(1); w.Write(1); w.Write(columns * 8 + (int)spans.Length); w.Write(new byte[770]);
        var name = new byte[16]; Encoding.ASCII.GetBytes("body").CopyTo(name, 0); w.Write(name); w.Write(0); w.Write(1); w.Write(2);
        foreach (int n in starts) w.Write(n); foreach (int n in ends) w.Write(n); w.Write(spans.ToArray());
        w.Write(0); w.Write(columns * 4); w.Write(columns * 8); w.Write(1f / 12);
        foreach (float value in Pose.Identity) w.Write(value);
        foreach (float value in new float[] { -3.5f, -2.5f, 0, 3.5f, 2.5f, 4 }) w.Write(value);
        w.Write((byte)sx); w.Write((byte)sy); w.Write((byte)sz); w.Write((byte)4);
        return s.ToArray();
    }
}
