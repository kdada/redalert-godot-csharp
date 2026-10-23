namespace RedAlert.Simulation;

public sealed partial class BattleMap
{
    public int Width { get; }
    public int Height { get; }
    public Terrain[] Ground { get; }
    public int[] Ore { get; }
    public int[] Buildings { get; }
    public int NavigationVersion;
    // Continuous tile-corner coordinates. Their projected U/V rectangle is aligned
    // with the battlefield screen; the surrounding array remains decorative land.
    public int PlayableMinU { get; private set; } = -44;
    public int PlayableMaxU { get; private set; } = 44;
    public int PlayableMinV { get; private set; } = 48;
    public int PlayableMaxV { get; private set; } = 144;
    public Cell[] Spawns { get; private set; } = [new(17, 47), new(77, 47), new(47, 77), new(47, 17), new(32, 32), new(62, 62), new(32, 62), new(62, 32)];

    public BattleMap(MatchSettings settings)
    {
        var original=string.IsNullOrWhiteSpace(settings.MapId)?null:ClassicMapRepository.Get(settings.MapId);
        Width=original?.Width??settings.Width;Height=original?.Height??settings.Height;
        Ground = new Terrain[Width * Height]; Ore = new int[Ground.Length]; Buildings = new int[Ground.Length];
        if(original is not null)
        {
            if(settings.Players.Length>original.Spawns.Length)throw new ArgumentException("玩家数量超出地图起点数量");
            ApplyOriginalMap(original);return;
        }
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
        {
            var value = Noise(x, y, settings.Seed);
            Ground[Index(x, y)] = value % 9 == 0 ? Terrain.Dirt : Terrain.Grass;
            if (x < 2 || y < 2 || x >= Width - 2 || y >= Height - 2) Ground[Index(x, y)] = Terrain.Water;
            // Sparse central rocks leave multiple wide routes; starting zones remain clear.
            if (x is > 30 and < 65 && y is > 30 and < 65 && value % 31 == 0) Ground[Index(x, y)] = Terrain.Rock;
        }
        GenerateLandscape(settings);
        // Clear every potential start before placing resources, including the extra
        // build pad and refinery approach. Clear the whole in-array reserve even
        // across the playable boundary: two-cell shoreline art also samples those
        // corners, and must not put wet ground under an edge start's minerals.
        foreach (var spawn in Spawns)
            for (var y=spawn.Y-4;y<=spawn.Y+10;y++)
            for (var x=spawn.X-5;x<=spawn.X+12;x++)
                if(Inside(x,y)){var index=Index(x,y);Ground[index]=Terrain.Grass;Elevation[index]=0;Bridges[index]=false;Ramps[index]=RampDirection.None;BridgeIds[index]=0;}
        foreach (var spawn in Spawns)
        {
            // The starting miner at (+7,+4) is on this patch and reveals all of it;
            // the refinery's eastern edge at +5 leaves an open unloading approach.
            for (var dy = -2; dy <= 2; dy++)
            for (var dx = -2; dx <= 2; dx++)
            {
                var x = spawn.X + 8 + dx; var y = spawn.Y + 4 + dy;
                if (IsPlayable(x, y)) { Ground[Index(x, y)] = Terrain.Dirt; Ore[Index(x, y)] = 900 + (int)(Noise(x, y, settings.Seed) % 700); }
            }
        }
        for (var y = 43; y <= 52; y++)
        for (var x = 43; x <= 52; x++)
            if (IsPlayable(x,y) && Ground[Index(x,y)] is not (Terrain.Water or Terrain.Rock) && !Bridges[Index(x,y)] && Noise(x, y, settings.Seed) % 3 != 0)
            { Ground[Index(x, y)] = Terrain.Dirt; Ore[Index(x, y)] = 2000; }
        // Full shoreline templates span two cells, and mineral sprites include
        // ground clusters. Keep real deposits inland instead of drawing mineable
        // resources over the wet portion of an otherwise dry shoreline cell.
        for(var y=0;y<Height;y++)for(var x=0;x<Width;x++)
        {
            var index=Index(x,y);if(Ore[index]==0)continue;
            for(var dy=-2;dy<=2 && Ore[index]>0;dy++)for(var dx=-2;dx<=2;dx++)
                if(!Inside(x+dx,y+dy) || Ground[Index(x+dx,y+dy)]==Terrain.Water){Ore[index]=0;break;}
        }
    }
    public static uint Noise(int x, int y, uint seed)
    {
        var n = unchecked(seed ^ ((uint)x * 374761393U) ^ ((uint)y * 668265263U));
        n = unchecked((n ^ (n >> 13)) * 1274126177U);
        return n ^ (n >> 16);
    }
    public int Index(int x, int y) => y * Width + x;
    public bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
    // Integer arguments identify cells; boundaries describe their continuous centers.
    public bool IsPlayable(int x,int y) => Inside(x,y) && (long)x-y>=PlayableMinU && (long)x-y<=PlayableMaxU
        && (long)x+y+1>=PlayableMinV && (long)x+y+1<=PlayableMaxV && (OriginalDefinition is null || OriginalDefinition.IsPlayable(x,y));
    public bool IsPlayablePoint(double x,double y) => x>=0 && y>=0 && x<=Width && y<=Height
        && x-y>=PlayableMinU && x-y<=PlayableMaxU && x+y>=PlayableMinV && x+y<=PlayableMaxV;
    bool LandTerrainPassable(int x,int y) => !IsCollapsedBridgeCell(x,y) && (OriginalDefinition is not null
        ?OriginalCellAt(x,y) is { } cell && (cell.LandPassable || OriginalTreeHasUnderlyingLand(cell))
        :Ground[Index(x,y)]!=Terrain.Rock && (Ground[Index(x,y)]!=Terrain.Water || Bridges[Index(x,y)]));
    public bool Walkable(int x, int y) => IsPlayable(x, y) && Buildings[Index(x,y)]==0
        && !OriginalObjectBlocksMovement(x,y,MovementDomain.Land) && LandTerrainPassable(x,y);
    // Sweep the unit's fixed-point ground body against occupied cells. Testing
    // only its destination cell lets a wide vehicle clip a wall or building corner.
    public bool BuildingBlocksMovement(int fromX,int fromY,int toX,int toY,int radius)
    {
        var minX=Math.Max(0,(Math.Min(fromX,toX)-radius)/Rules.Scale);
        var minY=Math.Max(0,(Math.Min(fromY,toY)-radius)/Rules.Scale);
        var maxX=Math.Min(Width-1,(Math.Max(fromX,toX)+radius)/Rules.Scale);
        var maxY=Math.Min(Height-1,(Math.Max(fromY,toY)+radius)/Rules.Scale);
        for(var y=minY;y<=maxY;y++)for(var x=minX;x<=maxX;x++)
        {
            if(Buildings[Index(x,y)]==0 && !OriginalObjectBlocksMovement(x,y,MovementDomain.Land)
                && !OriginalObjectBlocksMovement(x,y,MovementDomain.Water))continue;
            var left=x*Rules.Scale-radius;var top=y*Rules.Scale-radius;
            var right=(x+1)*Rules.Scale+radius;var bottom=(y+1)*Rules.Scale+radius;
            if(Math.Max(fromX,toX)<left || Math.Min(fromX,toX)>right || Math.Max(fromY,toY)<top || Math.Min(fromY,toY)>bottom)continue;
            var dx=(long)toX-fromX;var dy=(long)toY-fromY;
            var a=dx*(top-fromY)-dy*(left-fromX);var b=dx*(top-fromY)-dy*(right-fromX);
            var c=dx*(bottom-fromY)-dy*(right-fromX);var d=dx*(bottom-fromY)-dy*(left-fromX);
            if(Math.Min(Math.Min(a,b),Math.Min(c,d))<=0 && Math.Max(Math.Max(a,b),Math.Max(c,d))>=0)return true;
        }
        return false;
    }

    public bool FootprintFree(int x, int y, int w, int h)
    {
        // A building occupies whole tiles, so all four footprint corners must fit.
        if(w<=0 || h<=0 || !IsPlayablePoint(x,y) || !IsPlayablePoint((double)x+w,y)
            || !IsPlayablePoint(x,(double)y+h) || !IsPlayablePoint((double)x+w,(double)y+h))return false;
        var level=Elevation[Index(x,y)];
        for (var dy = 0; dy < h; dy++)
        for (var dx = 0; dx < w; dx++)
        {
            var cx=x+dx;var cy=y+dy;
            if(!IsPlayable(cx,cy) || Buildings[Index(cx,cy)]!=0 || !LandTerrainPassable(cx,cy)
                || OriginalObjectBlocksPlacement(cx,cy) || !OriginalTerrainBuildable(cx,cy))return false;
            var index=Index(cx,cy);
            if(Ground[index]==Terrain.Water || Bridges[index] || IsRamp(cx,cy) || Elevation[index]!=level || Ore[index]!=0)return false;
        }
        return true;
    }
}

// All tie breaks and neighbor ordering are explicit; no engine navigation/float math enters simulation.
public sealed class GridPathfinder(BattleMap map)
{
    static readonly (int X, int Y, int Cost)[] Neighbors = [(0,-1,10),(1,0,10),(0,1,10),(-1,0,10),(1,-1,14),(1,1,14),(-1,1,14),(-1,-1,14)];
    readonly int[] costs = new int[map.Width * map.Height];
    readonly int[] parents = new int[map.Width * map.Height];
    readonly bool[] closed = new bool[map.Width * map.Height];
    readonly PriorityQueue<int, (int Score, int Cell)> open = new();
    public List<int> Find(int sx, int sy, int gx, int gy, int[]? occupied = null,MovementDomain domain=MovementDomain.Land,bool amphibious=false)
    {
        if (!map.IsPlayable(sx, sy) || !map.Navigable(gx, gy,domain,amphibious)) return [];
        var start = map.Index(sx, sy); var goal = map.Index(gx, gy);
        if (start == goal) return [];
        Array.Fill(costs, int.MaxValue); Array.Fill(parents, -1); Array.Clear(closed); open.Clear();
        costs[start] = 0; open.Enqueue(start, (Heuristic(sx,sy,gx,gy), start));
        var expanded = 0;
        // Count finalized cells, not heap pops: improved routes leave stale entries.
        // A connected original-map coast can require more than 5000 real expansions.
        // Octile distance is consistent with 10/14 edges and nonnegative penalties,
        // so each cell closes once. At most 8 edges per closed cell can enqueue work.
        while (expanded < costs.Length && open.TryDequeue(out var current, out var priority))
        {
            var x = current % map.Width; var y = current / map.Width;
            if (closed[current] || priority.Score != costs[current] + Heuristic(x, y, gx, gy)) continue;
            closed[current] = true; expanded++;
            if (current == goal)
            {
                var path = new List<int>();
                for (var c = goal; c != start; c = parents[c]) { if (c < 0) return []; path.Add(c); }
                path.Reverse(); return path;
            }
            foreach (var n in Neighbors)
            {
                var nx = x + n.X; var ny = y + n.Y;
                if (!map.Navigable(nx,ny,domain,amphibious) || !map.CanTraverseMovement(new Cell(x,y),new Cell(nx,ny),domain,amphibious)) continue;
                if (n.X != 0 && n.Y != 0 && (!map.Navigable(x+n.X,y,domain,amphibious) || !map.Navigable(x,y+n.Y,domain,amphibious))) continue;
                var ni = map.Index(nx, ny);
                if (closed[ni]) continue;
                var penalty = occupied is not null && ni != goal && occupied[ni] > 0 ? 45 : 0;
                var cost = costs[current] + n.Cost + penalty;
                if (cost >= costs[ni]) continue;
                costs[ni] = cost; parents[ni] = current;
                open.Enqueue(ni, (cost + Heuristic(nx,ny,gx,gy), ni));
            }
        }
        return [];
    }
    static int Heuristic(int x, int y, int gx, int gy)
    {
        var dx = Math.Abs(x-gx); var dy = Math.Abs(y-gy);
        return 10 * Math.Max(dx,dy) + 4 * Math.Min(dx,dy);
    }
}
