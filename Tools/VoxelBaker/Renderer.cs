using System.Numerics;

namespace VoxelBaker;

internal sealed record Face(Vector3 A, Vector3 B, Vector3 C, Vector3 D, Vector3 Normal, byte Index, byte NormalIndex);
internal sealed record Part(string Name, string Source, string? Hva, Limb[] Limbs, List<Face> Faces);
internal sealed record Canvas(int Width, int Height, float AnchorX, float AnchorY);
internal sealed record RenderOptions(int Directions, float Scale, float Elevation, float Azimuth, int Columns, int Padding, int TeamStart, int TeamCount, int Frame);
internal sealed record Tile(byte[] Rgba, byte[] Indices, byte[] Normals, byte[] Team, byte[] Alpha, int OpaquePixels);

internal static class Renderer
{
    static readonly (int X, int Y, int Z, Vector3[] Corners)[] Sides =
    [
        (-1,0,0, [new(0,0,0), new(0,0,1), new(0,1,1), new(0,1,0)]),
        (1,0,0, [new(1,0,0), new(1,1,0), new(1,1,1), new(1,0,1)]),
        (0,-1,0, [new(0,0,0), new(1,0,0), new(1,0,1), new(0,0,1)]),
        (0,1,0, [new(0,1,0), new(0,1,1), new(1,1,1), new(1,1,0)]),
        (0,0,-1, [new(0,0,0), new(0,1,0), new(1,1,0), new(1,0,0)]),
        (0,0,1, [new(0,0,1), new(1,0,1), new(1,1,1), new(0,1,1)])
    ];
    public static Part LoadPart(string name, string vxlPath, string? hvaPath, int frame)
    {
        var limbs = Vxl.Read(Bytes.Load(vxlPath));
        Pose? pose = hvaPath == null ? null : Pose.Read(Bytes.Load(hvaPath));
        if (pose != null && pose.Names.Length != limbs.Length) throw new InvalidDataException("VXL/HVA limb counts differ.");
        return new Part(name, vxlPath, hvaPath, limbs, BuildFaces(limbs, pose, frame));
    }
    public static List<Face> BuildFaces(Limb[] limbs, Pose? pose, int frame)
    {
        var result = new List<Face>();
        for (int section = 0; section < limbs.Length; section++)
        {
            var l = limbs[section];
            float[] m = pose?.For(section, frame) ?? Pose.Identity;
            Vector3 step = l.Step;
            Vector3 Transform(Vector3 grid)
            {
                Vector3 p = l.Min + grid * step;
                // HVA translations are grid units, adjusted by limb scale and bounds/grid ratio.
                Vector3 translation = new Vector3(m[3], m[7], m[11]) * step * l.Scale;
                return new Vector3(
                    (m[0] * p.X + m[1] * p.Y + m[2] * p.Z + translation.X) * l.Scale,
                    -(m[4] * p.X + m[5] * p.Y + m[6] * p.Z + translation.Y) * l.Scale,
                    (m[8] * p.X + m[9] * p.Y + m[10] * p.Z + translation.Z) * l.Scale);
            }
            Vector3 TransformNormal(Vector3 n)
            {
                // Inverse transpose of the linear affine component, including Y reflection.
                var matrix = new Matrix4x4(m[0], -m[4], m[8], 0, m[1], -m[5], m[9], 0, m[2], -m[6], m[10], 0, 0, 0, 0, 1);
                if (!Matrix4x4.Invert(matrix, out var inverse)) throw new InvalidDataException("Singular normal transform.");
                return Vector3.Normalize(Vector3.TransformNormal(n, Matrix4x4.Transpose(inverse)));
            }
            for (int z = 0; z < l.Z; z++) for (int y = 0; y < l.Y; y++) for (int x = 0; x < l.X; x++)
            {
                if (!l.Has(x, y, z)) continue;
                int index = l.Index(x, y, z);
                if (l.Colors[index] == 0) continue;
                var center = new Vector3(x, y, z);
                foreach (var side in Sides)
                {
                    if (l.Has(x + side.X, y + side.Y, z + side.Z)) continue;
                    var n = TransformNormal(new Vector3(side.X, side.Y, side.Z));
                    result.Add(new Face(Transform(center + side.Corners[0]), Transform(center + side.Corners[1]),
                        Transform(center + side.Corners[2]), Transform(center + side.Corners[3]), n, l.Colors[index], l.Normals[index]));
                    if (result.Count > 3_000_000) throw new InvalidDataException("Surface exceeds three million face budget.");
                }
            }
        }
        if (result.Count == 0) throw new InvalidDataException("VXL has no nonzero visible colors.");
        return result;
    }
    static float Radians(float degrees) => degrees * MathF.PI / 180;
    static Vector3 Rotate(Vector3 p, float angle)
    {
        float c = MathF.Cos(angle), s = MathF.Sin(angle);
        return new Vector3(c * p.X - s * p.Y, s * p.X + c * p.Y, p.Z);
    }
    static Vector3 Project(Vector3 p, RenderOptions o, int direction)
    {
        p = Rotate(p, Radians(o.Azimuth + direction * 360f / o.Directions));
        float e = Radians(o.Elevation), s = MathF.Sin(e), c = MathF.Cos(e);
        return new Vector3(p.X * o.Scale, (-s * p.Y - c * p.Z) * o.Scale, -c * p.Y + s * p.Z);
    }
    public static Canvas Fit(IEnumerable<Part> parts, RenderOptions o, int? fixedCell)
    {
        float minX = 0, maxX = 0, minY = 0, maxY = 0;
        foreach (var part in parts) foreach (var f in part.Faces) foreach (var v in new[] { f.A, f.B, f.C, f.D })
            for (int d = 0; d < o.Directions; d++)
            {
                var p = Project(v, o, d);
                minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
            }
        int w = ((int)MathF.Ceiling(maxX - minX) + o.Padding * 2 + 7) / 8 * 8;
        int h = ((int)MathF.Ceiling(maxY - minY) + o.Padding * 2 + 7) / 8 * 8;
        if (w > 1024 || h > 1024) throw new InvalidDataException("Projected canvas exceeds 1024 pixels; lower --scale.");
        float ax = MathF.Ceiling(-minX) + o.Padding, ay = MathF.Ceiling(-minY) + o.Padding;
        if (fixedCell is int size)
        {
            if (size < w || size < h) throw new InvalidDataException($"--cell {size} cannot contain required {w}x{h} canvas.");
            ax += (size - w) / 2; ay += (size - h) / 2; w = h = size;
        }
        return new Canvas(w, h, ax, ay);
    }
    public static Tile Render(List<Face> faces, Canvas canvas, RenderOptions o, int direction, byte[] palette)
    {
        int pixels = checked(canvas.Width * canvas.Height);
        var rgba = new byte[pixels * 4]; var indices = new byte[pixels]; var normals = new byte[pixels];
        var team = new byte[pixels]; var alpha = new byte[pixels];
        var depth = new float[pixels]; Array.Fill(depth, float.NegativeInfinity);
        float angle = Radians(o.Azimuth + direction * 360f / o.Directions), elevation = Radians(o.Elevation);
        var camera = new Vector3(0, -MathF.Cos(elevation), MathF.Sin(elevation));
        var light = Vector3.Normalize(new Vector3(-0.6f, -0.8f, 1.2f));
        foreach (var f in faces)
        {
            var n = Rotate(f.Normal, angle);
            if (Vector3.Dot(n, camera) <= 0) continue;
            float shade = 0.38f + 0.62f * Math.Max(0, Vector3.Dot(n, light));
            Vector3 P(Vector3 v) { var p = Project(v, o, direction); return new Vector3(p.X + canvas.AnchorX, p.Y + canvas.AnchorY, p.Z); }
            var a = P(f.A); var b = P(f.B); var c = P(f.C); var d = P(f.D);
            Triangle(a, b, c, f, shade); Triangle(a, c, d, f, shade);
        }
        return new Tile(rgba, indices, normals, team, alpha, alpha.Count(x => x != 0));
        void Triangle(Vector3 a, Vector3 b, Vector3 c, Face f, float shade)
        {
            float area = Edge(a, b, c.X, c.Y);
            if (Math.Abs(area) < 1e-8f) return;
            int minX = Math.Clamp((int)MathF.Floor(Math.Min(a.X, Math.Min(b.X, c.X))), 0, canvas.Width - 1);
            int maxX = Math.Clamp((int)MathF.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))), 0, canvas.Width - 1);
            int minY = Math.Clamp((int)MathF.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y))), 0, canvas.Height - 1);
            int maxY = Math.Clamp((int)MathF.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))), 0, canvas.Height - 1);
            for (int y = minY; y <= maxY; y++) for (int x = minX; x <= maxX; x++)
            {
                float u = Edge(b, c, x + 0.5f, y + 0.5f) / area;
                float v = Edge(c, a, x + 0.5f, y + 0.5f) / area, w = 1 - u - v;
                if (u < -1e-5 || v < -1e-5 || w < -1e-5) continue;
                float z = u * a.Z + v * b.Z + w * c.Z;
                int i = y * canvas.Width + x;
                if (z <= depth[i]) continue;
                depth[i] = z; indices[i] = f.Index; normals[i] = f.NormalIndex; alpha[i] = 255;
                team[i] = f.Index >= o.TeamStart && f.Index < o.TeamStart + o.TeamCount ? (byte)(f.Index - o.TeamStart + 1) : (byte)0;
                for (int channel = 0; channel < 3; channel++) rgba[i * 4 + channel] = (byte)Math.Clamp((int)(palette[f.Index * 3 + channel] * shade + 0.5f), 0, 255);
                rgba[i * 4 + 3] = 255;
            }
        }
    }
    static float Edge(Vector3 a, Vector3 b, float x, float y) => (x - a.X) * (b.Y - a.Y) - (y - a.Y) * (b.X - a.X);
}
