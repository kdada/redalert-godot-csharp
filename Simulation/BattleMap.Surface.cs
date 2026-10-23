namespace RedAlert.Simulation;

// Directions refer to grid axes, independently of the isometric camera.
public enum RampDirection : byte { None, RiseEast, RiseSouth, RiseWest, RiseNorth }
public enum BridgeAxis : byte { X, Y }

// Corner coordinates are (x,y), (x+1,y), (x+1,y+1), (x,y+1).
// All heights use Rules.Scale units per elevation level.
public readonly record struct SurfaceCorners(int NorthWest, int NorthEast, int SouthEast, int SouthWest)
{
    public bool IsFlat => NorthWest == NorthEast && NorthWest == SouthEast && NorthWest == SouthWest;
}
public readonly record struct SurfaceNormal(int X, int Y, int Z);

// Origin is the minimum grid X/Y deck cell. Length follows Axis; Width is
// perpendicular to it. Banks identify the central dry cell at either end.
public readonly record struct BridgeSpan(int Id, BridgeAxis Axis, Cell Origin, int Length, int Width,
    int DeckHeightFixed, Cell BankA, Cell BankB)
{
    public int SizeX => Axis == BridgeAxis.X ? Length : Width;
    public int SizeY => Axis == BridgeAxis.Y ? Length : Width;
    public bool Contains(int x, int y) => x >= Origin.X && y >= Origin.Y
        && x < Origin.X + SizeX && y < Origin.Y + SizeY;
}

public sealed partial class BattleMap
{
    public RampDirection[] Ramps { get; private set; } = [];
    // Zero means no span; positive IDs index BridgeSpans at id-1.
    public int[] BridgeIds { get; private set; } = [];
    public BridgeSpan[] BridgeSpans { get; private set; } = [];

    public RampDirection RampAt(int x, int y) => Inside(x, y) ? Ramps[Index(x, y)] : RampDirection.None;
    public RampDirection RampAt(Cell cell) => RampAt(cell.X, cell.Y);
    public bool IsRamp(int x, int y) => OriginalCellAt(x,y)?.RampType>0 || RampAt(x,y)!=RampDirection.None;
    public bool IsRamp(Cell cell) => IsRamp(cell.X, cell.Y);
    public BridgeSpan? BridgeAt(int x, int y)
    {
        if (!HasBridge(x, y)) return null;
        var id = BridgeIds[Index(x, y)];
        return id > 0 && id <= BridgeSpans.Length ? BridgeSpans[id - 1] : null;
    }
    public BridgeSpan? BridgeAt(Cell cell) => BridgeAt(cell.X, cell.Y);

    public SurfaceCorners SurfaceCornersAt(int x, int y)
    {
        var low = ElevationAt(x, y) * Rules.Scale;
        var high = low + Rules.Scale;
        if(OriginalCellAt(x,y) is {RampType:>0} original && ClassicMapRamps.TryGetCorners(original.RampType,out var corners))
            return new SurfaceCorners(low+(int)(corners.NorthWest*Rules.Scale),low+(int)(corners.NorthEast*Rules.Scale),low+(int)(corners.SouthEast*Rules.Scale),low+(int)(corners.SouthWest*Rules.Scale));
        return RampAt(x, y) switch
        {
            RampDirection.RiseEast => new(low, high, high, low),
            RampDirection.RiseSouth => new(low, low, high, high),
            RampDirection.RiseWest => new(high, low, low, high),
            RampDirection.RiseNorth => new(high, high, low, low),
            _ => new(low, low, low, low)
        };
    }
    public SurfaceCorners SurfaceCornersAt(Cell cell) => SurfaceCornersAt(cell.X, cell.Y);

    // Authoritative sampler: world X/Y and the result are fixed-point integers.
    // It interpolates only an explicitly sloped tile. Flat cliff tops never
    // borrow the height of their lower neighbors.
    public int SurfaceHeightFixed(int worldX, int worldY)
    {
        if (worldX < 0 || worldY < 0 || worldX >= Width * Rules.Scale || worldY >= Height * Rules.Scale) return 0;
        var x = worldX / Rules.Scale; var y = worldY / Rules.Scale;
        var localX = worldX % Rules.Scale; var localY = worldY % Rules.Scale;
        var height = ElevationAt(x, y) * Rules.Scale;
        if(OriginalCellAt(x,y) is {RampType:>0})
        {
            var c=SurfaceCornersAt(x,y);
            var north=c.NorthWest+(long)(c.NorthEast-c.NorthWest)*localX/Rules.Scale;
            var south=c.SouthWest+(long)(c.SouthEast-c.SouthWest)*localX/Rules.Scale;
            return (int)(north+(south-north)*localY/Rules.Scale);
        }
        return height + (RampAt(x, y) switch
        {
            RampDirection.RiseEast => localX,
            RampDirection.RiseSouth => localY,
            RampDirection.RiseWest => Rules.Scale - localX,
            RampDirection.RiseNorth => Rules.Scale - localY,
            _ => 0
        });
    }

    // Rendering convenience; all decisions and stored state stay integer-only.
    public double SurfaceHeightAt(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || x < 0 || y < 0 || x >= Width || y >= Height) return 0;
        return SurfaceHeightFixed((int)Math.Floor(x * Rules.Scale), (int)Math.Floor(y * Rules.Scale)) / (double)Rules.Scale;
    }

    public Cell SurfaceSlopeFixedAt(int x,int y)
    {
        if(OriginalCellAt(x,y) is {RampType:>0})
        {
            var c=SurfaceCornersAt(x,y);
            return new Cell((c.NorthEast-c.NorthWest+c.SouthEast-c.SouthWest)/2,(c.SouthWest-c.NorthWest+c.SouthEast-c.NorthEast)/2);
        }
        return RampAt(x,y) switch
        {
            RampDirection.RiseEast=>new(Rules.Scale,0),RampDirection.RiseSouth=>new(0,Rules.Scale),
            RampDirection.RiseWest=>new(-Rules.Scale,0),RampDirection.RiseNorth=>new(0,-Rules.Scale),_=>new(0,0)
        };
    }
    public Cell SurfaceSlopeFixedAt(Cell cell) => SurfaceSlopeFixedAt(cell.X, cell.Y);
    // Unnormalized upward normal: callers can normalize for presentation.
    public SurfaceNormal SurfaceNormalFixedAt(int x, int y)
    {
        var slope = SurfaceSlopeFixedAt(x, y);
        return new(-slope.X, -slope.Y, Rules.Scale);
    }
    public SurfaceNormal SurfaceNormalFixedAt(Cell cell) => SurfaceNormalFixedAt(cell.X, cell.Y);

    bool SurfaceEdgeMatches(Cell from, Cell to)
    {
        var a = SurfaceCornersAt(from); var b = SurfaceCornersAt(to);
        if (to.X == from.X + 1 && to.Y == from.Y) return a.NorthEast == b.NorthWest && a.SouthEast == b.SouthWest;
        if (to.X == from.X - 1 && to.Y == from.Y) return a.NorthWest == b.NorthEast && a.SouthWest == b.SouthEast;
        if (to.Y == from.Y + 1 && to.X == from.X) return a.SouthWest == b.NorthWest && a.SouthEast == b.NorthEast;
        if (to.Y == from.Y - 1 && to.X == from.X) return a.NorthWest == b.SouthWest && a.NorthEast == b.SouthEast;
        return from == to;
    }
}
