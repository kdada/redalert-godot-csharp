namespace RedAlert.Simulation;

public sealed partial class BattleMap
{
    // Elevation is a flat top's level, or the LOW edge of an explicit ramp.
    // Ordinary shores, water and low bridges share the original level-zero plane.
    public byte[] Elevation { get; private set; } = [];
    public bool[] Bridges { get; private set; } = [];

    public int ElevationAt(int x, int y) => Inside(x, y) ? Elevation[Index(x, y)] : 0;
    public int ElevationAt(Cell cell) => ElevationAt(cell.X, cell.Y);
    public bool HasBridge(int x, int y) => Inside(x, y) && Bridges[Index(x, y)];
    public bool HasBridge(Cell cell) => HasBridge(cell.X, cell.Y);

    // Occupancy belongs to Walkable. Terrain edges must meet along their whole
    // width; even a one-level vertical cliff is impassable without a real ramp.
    public bool CanTraverseTerrain(Cell from, Cell to)
    {
        if (!SupportsLand(from.X, from.Y) || !SupportsLand(to.X, to.Y)) return false;
        var dx = to.X - from.X; var dy = to.Y - from.Y;
        if (Math.Abs(dx) > 1 || Math.Abs(dy) > 1) return false;
        if (dx == 0 || dy == 0) return SurfaceEdgeMatches(from, to);
        var sideX = new Cell(to.X, from.Y); var sideY = new Cell(from.X, to.Y);
        return SupportsLand(sideX.X, sideX.Y) && SupportsLand(sideY.X, sideY.Y)
            && SurfaceEdgeMatches(from, sideX) && SurfaceEdgeMatches(from, sideY)
            && SurfaceEdgeMatches(sideX, to) && SurfaceEdgeMatches(sideY, to);
    }

    bool SupportsLand(int x, int y) => IsPlayable(x, y) && (IsBridgeCellIntact(x,y)
        || Ground[Index(x, y)] is not (Terrain.Rock or Terrain.Water));

    void GenerateLandscape(MatchSettings settings)
    {
        Elevation = new byte[Ground.Length]; Bridges = new bool[Ground.Length];
        Ramps = new RampDirection[Ground.Length]; BridgeIds = new int[Ground.Length];
        BridgeSpans = [];
        var seed = settings.Seed;
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
        {
            var i = Index(x, y); var u = x - y; var v = x + y + 1;
            Elevation[i] = 0;
            var coast = 134 + (int)(Noise((u + 128) / 6, 2, seed ^ 0x51EAU) % 5);
            var westCoast = -39 + (int)(Noise(3, v / 8, seed ^ 0x51EAU) % 3);
            var eastCoast = 41 - (int)(Noise(4, v / 8, seed ^ 0x51EAU) % 3);
            if (v >= coast || u <= westCoast || u >= eastCoast || Math.Abs(x - RiverCenter(y, seed)) <= 2
                || x < 2 || y < 2 || x >= Width - 2 || y >= Height - 2)
            {
                Ground[i] = Terrain.Water; Elevation[i] = 0; Ore[i] = 0;
            }
        }
        foreach (var spawn in Spawns)
        for (var y = spawn.Y - 4; y <= spawn.Y + 10; y++)
        for (var x = spawn.X - 5; x <= spawn.X + 12; x++)
            if (IsPlayable(x, y)) PaintLandscapeLand(x, y, Terrain.Grass, 0);

        // Dry roads never manufacture bridge tiles. A crossing is created only
        // below, after its river banks and rectangular span have been resolved.
        var hub = new Cell(47, 47);
        foreach (var spawn in Spawns)
        {
            PaintLandscapeRoad(spawn.X, spawn.Y, hub.X, spawn.Y);
            PaintLandscapeRoad(hub.X, spawn.Y, hub.X, hub.Y);
        }
        var spans = new List<BridgeSpan>();
        foreach (var y in new[] { 32, 47, 62 }) BuildLandscapeBridge(y, seed, spans);
        BridgeSpans = spans.ToArray();

        // Two broad level-two mesas have vertical outer cliffs. Their only
        // entrances are wide two-cell ramps: 0->1 followed by 1->2. Both are
        // outside every starting base, mining reserve and bridge approach.
        var left = 31 + (int)(Noise(1, 1, seed) % 3);
        var right = 42 - (int)(Noise(2, 1, seed) % 2);
        var top = 51 + (int)(Noise(3, 1, seed) % 2);
        PaintLandscapePlateau(left, top, right, 55);
        var rampX = (left + right) / 2;
        for (var x = rampX - 2; x <= rampX + 2; x++)
        {
            PaintLandscapeRamp(x, top - 2, 0, RampDirection.RiseSouth);
            PaintLandscapeRamp(x, top - 1, 1, RampDirection.RiseSouth);
            PaintLandscapeLand(x, top - 3, Terrain.Dirt, 0);
        }
        var east = 69 + (int)(Noise(4, 1, seed) % 3);
        PaintLandscapeRoad(56, 48, 56, 54);
        PaintLandscapePlateau(60, 51, east, 55);
        for (var y = 52; y <= 54; y++)
        {
            PaintLandscapeRamp(58, y, 0, RampDirection.RiseEast);
            PaintLandscapeRamp(59, y, 1, RampDirection.RiseEast);
            PaintLandscapeLand(57, y, Terrain.Dirt, 0);
        }
        // Shared mining stays on one plane, without filling the river channel.
        for (var y = 43; y <= 52; y++)
        for (var x = 43; x <= 52; x++)
            if (IsPlayable(x, y) && Ground[Index(x, y)] != Terrain.Water)
                PaintLandscapeLand(x, y, Terrain.Dirt, 0);
    }

    static int RiverCenter(int y, uint seed)
    {
        var segment = y / 12; var step = y % 12;
        var a = 51 + (int)(Noise(segment, 9, seed ^ 0x71AE2U) % 3);
        var b = 51 + (int)(Noise(segment + 1, 9, seed ^ 0x71AE2U) % 3);
        return (a * (12 - step) + b * step) / 12;
    }

    void PaintLandscapeLand(int x, int y, Terrain terrain, byte level)
    {
        var i = Index(x, y);
        Ground[i] = terrain; Elevation[i] = level; Ramps[i] = RampDirection.None;
        Bridges[i] = false; BridgeIds[i] = 0; Ore[i] = 0;
    }
    void PaintLandscapePlateau(int minX, int minY, int maxX, int maxY)
    {
        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++) PaintLandscapeLand(x, y, Terrain.Grass, 2);
    }
    void PaintLandscapeRamp(int x, int y, byte lowLevel, RampDirection direction)
    {
        PaintLandscapeLand(x, y, Terrain.Dirt, lowLevel);
        Ramps[Index(x, y)] = direction;
    }
    void PaintLandscapeRoad(int x, int y, int endX, int endY)
    {
        var dx = Math.Sign(endX - x); var dy = Math.Sign(endY - y);
        while (true)
        {
            for (var oy = -1; oy <= 1; oy++)
            for (var ox = -1; ox <= 1; ox++)
            {
                var nx = x + ox; var ny = y + oy;
                if (IsPlayable(nx, ny) && Ground[Index(nx, ny)] != Terrain.Water)
                    PaintLandscapeLand(nx, ny, Terrain.Dirt, 0);
            }
            if (x == endX && y == endY) break;
            x += dx; y += dy;
        }
    }
    void BuildLandscapeBridge(int centerY, uint seed, List<BridgeSpan> spans)
    {
        const int width = 3;
        var west = Width; var east = 0;
        for (var y = centerY - 1; y <= centerY + 1; y++)
        {
            west = Math.Min(west, RiverCenter(y, seed) - 2);
            east = Math.Max(east, RiverCenter(y, seed) + 2);
        }
        var id = spans.Count + 1;
        var span = new BridgeSpan(id, BridgeAxis.X, new(west, centerY - 1), east - west + 1, width,
            0, new(west - 1, centerY), new(east + 1, centerY));
        // Resolve the whole river cross-section to one rectangle; curved banks
        // cannot create little jutting deck tiles, internal rails or missing lanes.
        for (var y = centerY - 1; y <= centerY + 1; y++)
        {
            for (var x = west - 3; x < west; x++) PaintLandscapeLand(x, y, Terrain.Dirt, 0);
            for (var x = east + 1; x <= east + 3; x++) PaintLandscapeLand(x, y, Terrain.Dirt, 0);
            for (var x = west; x <= east; x++)
            {
                var i = Index(x, y);
                Ground[i] = Terrain.Water; Elevation[i] = 0; Bridges[i] = true;
                BridgeIds[i] = id; Ramps[i] = RampDirection.None; Ore[i] = 0;
            }
        }
        spans.Add(span);
    }
}
