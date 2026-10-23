namespace RedAlert.Simulation;

public sealed partial class World
{
    public MatchSettings Settings { get; }
    public BattleMap Map { get; }
    public PlayerState[] Players { get; }
    public List<Unit> Units { get; } = [];
    public List<Building> Buildings { get; } = [];
    public List<CombatEvent> Events { get; } = [];
    public int Tick { get; private set; }
    public bool Finished { get; private set; }
    public int WinningTeam { get; private set; } = -1;
    public int RejectedCommands { get; private set; }
    public int AcceptedCommands { get; private set; }
    public int PathSearches { get; private set; }
    readonly Dictionary<int, Entity> entities = [];
    readonly GridPathfinder pathfinder;
    readonly int[] occupied;
    readonly Dictionary<int,int[]> sightRows=[];
    int nextId = 1;
    int pathsThisTick;
    bool simulatingUnits;

    public World(MatchSettings settings)
    {
        settings.Validate(); Settings = settings; Map = new BattleMap(settings); pathfinder = new GridPathfinder(Map);
        occupied = new int[Map.Width * Map.Height];
        Players = settings.Players.Select((p,i) => new PlayerState(i,p,settings.StartingCredits,Map.Width*Map.Height)).ToArray();
        foreach (var p in Players)
        {
            var s = Map.Spawns[p.Id];
            if(settings.ClassicStart){SpawnClassicStartingForce(p);continue;}
            SpawnBuilding(p.Id, BuildingType.ConstructionYard, s.X, s.Y);
            SpawnBuilding(p.Id, BuildingType.PowerPlant, s.X-4, s.Y+1);
            SpawnBuilding(p.Id, BuildingType.Refinery, s.X+3, s.Y+7);
            SpawnBuilding(p.Id, BuildingType.Barracks, s.X-3, s.Y-3);
            SpawnUnit(p.Id, Rules.Miner(p.Setup.Country), s.X+7, s.Y+4).Order = UnitOrder.Harvest;
            SpawnUnit(p.Id, Rules.BasicTank(p.Setup.Country), s.X+1, s.Y-2);
            SpawnUnit(p.Id, Rules.BasicTank(p.Setup.Country), s.X+2, s.Y-2);
            for (var i = 0; i < 3; i++) SpawnUnit(p.Id, Rules.BasicInfantry(p.Setup.Country), s.X-1+i, s.Y+4);
        }
        UpdatePower(); UpdateVisibility();
    }
    public Entity? Find(int id) => entities.TryGetValue(id, out var e) && e.Alive ? e : ClassicProjectiles.FirstOrDefault(projectile=>projectile.Id==id && projectile.Alive);
    public bool Allied(int a, int b) => a == b || Players[a].Setup.Team == Players[b].Setup.Team;
    // Vanilla has shroud, but FogOfWar=no: known terrain and ordinary actors
    // remain visible outside current unit sight. Gap and satellite update this mask.
    public bool Visible(int player,int x,int y)=>Explored(player,x,y);
    public bool Explored(int player,int x,int y)=>player>=0 && player<Players.Length && Map.IsPlayable(x,y)
        && (Players[player].Visibility[Map.Index(x,y)]&(ClassicVisibilityRules.Explored|ClassicVisibilityRules.Satellite))!=0;
    public bool HasBuilding(int player, BuildingType type) => Buildings.Any(b => b.Alive && b.Owner==player && b.Type==type && b.Complete);
    public int EntityCount => Units.Count + Buildings.Count;

    public Unit SpawnUnit(int owner, UnitType type, int x, int y,Country? originCountry=null)
    {
        if (!Enum.IsDefined(type) || owner<0 || owner>=Players.Length || EntityCount >= Rules.MaxEntities) throw new ArgumentException("无法生成单位");
        var country=originCountry??Players[owner].Setup.Country;var definition=Rules.Unit(type,country);
        var cell=NearestFree(x,y,true,domain:definition.Domain,amphibious:definition.Amphibious);
        if (cell is null) throw new InvalidOperationException("地图上没有可用的单位出生位置");
        var u = new Unit { Id=nextId++, Owner=owner, Type=type,OriginCountry=country, Health=definition.Health,Ammo=definition.Ammo,
            X=cell.Value.X*Rules.Scale+Rules.Scale/2, Y=cell.Value.Y*Rules.Scale+Rules.Scale/2,
            GoalX=cell.Value.X, GoalY=cell.Value.Y,Altitude=definition.Domain==MovementDomain.Air && !definition.AirportBound?CruisingAltitude(definition):0,
            Kills=0 };
        if(ClassicCanGainVeterancy(u) && (definition.Infantry?Players[owner].VeteranInfantry:definition.Producer==BuildingType.WarFactory && Players[owner].VeteranVehicles))u.Kills=3;
        Units.Add(u); entities.Add(u.Id,u);if(OccupiesSurface(u))occupied[Map.Index(u.CellX,u.CellY)]++; return u;
    }
    public Building SpawnBuilding(int owner, BuildingType type, int x, int y, bool complete=true,Country? foundationCountry=null)
    {
        if(owner<0 || owner>=Players.Length || !Enum.IsDefined(type))throw new ArgumentException("建筑类型或归属无效");
        var country=foundationCountry??Players[owner].Setup.Country;var d = Rules.Building(type,country);
        if (!Map.FootprintFree(d,x,y) || BuildingAreaHasUnits(x,y,d.Width,d.Height) || EntityCount >= Rules.MaxEntities) throw new ArgumentException("建筑占地不可用");
        var b = new Building { Id=nextId++, Owner=owner, Type=type, FoundationCountry=country, Health=complete?d.Health:Math.Max(1,d.Health/4),
            X=x*Rules.Scale+d.Width*Rules.Scale/2, Y=y*Rules.Scale+d.Height*Rules.Scale/2,
            CellLeft=x, CellTop=y, RemainingBuildTicks=complete?0:d.BuildTicks, RallyX=x+d.Width+1, RallyY=y+d.Height+1 };
        Buildings.Add(b); entities.Add(b.Id,b);
        for (var dy=0;dy<d.Height;dy++) for(var dx=0;dx<d.Width;dx++) Map.Buildings[Map.Index(x+dx,y+dy)]=b.Id;
        Map.NavigationVersion++; return b;
    }
    public Cell? NearestFree(int x,int y, bool avoidUnits=false, HashSet<int>? reserved=null,MovementDomain domain=MovementDomain.Land,bool amphibious=false)
    {
        for(var r=0;r<16;r++)
        for(var dy=-r;dy<=r;dy++)
        for(var dx=-r;dx<=r;dx++)
        {
            if(r>0 && Math.Abs(dx)!=r && Math.Abs(dy)!=r) continue;
            var cx=x+dx;var cy=y+dy;
            if(!Map.Navigable(cx,cy,domain,amphibious)) continue;
            var index=Map.Index(cx,cy);
            if(reserved?.Contains(index)==true) continue;
            if(avoidUnits && domain!=MovementDomain.Air && (simulatingUnits?occupied[index]>0:Units.Any(u=>OccupiesSurface(u) && u.CellX==cx && u.CellY==cy)))continue;
            return new Cell(cx,cy);
        }
        return null;
    }

    public void Step(IEnumerable<GameCommand>? commands=null, Action<GameCommand,bool>? commandApplied=null)
    {
        Events.Clear(); pathsThisTick=0;
        if(commands is not null)
            foreach(var c in commands.OrderBy(c=>c.Player).ThenBy(c=>c.Sequence))
            {
                var accepted=c.Tick==Tick && Apply(c);
                if(accepted)AcceptedCommands++;else RejectedCommands++;
                commandApplied?.Invoke(c,accepted);
            }
        if(!Finished)
        {
            UpdateClassicWorldState();UpdateTerrainBombs();UpdateClassicEconomy();UpdatePower();
            Array.Clear(occupied);
            foreach(var u in Units) if(OccupiesSurface(u) && Map.Inside(u.CellX,u.CellY)) occupied[Map.Index(u.CellX,u.CellY)]++;
            // Creation during production is applied before unit iteration, removal after all combat.
            simulatingUnits=true;
            UpdateBuildings();
            var unitCount=Units.Count;var firstUnit=unitCount==0?0:(int)((long)Tick*48%unitCount);
            for(var i=0;i<unitCount;i++)
            {
                // Rotate deterministic service order so an unreachable early army cannot monopolize the path budget.
                var u=Units[(firstUnit+i)%unitCount];
                if(!u.Alive || Players[u.Owner].Defeated)continue;
                UpdateClassicUnitState(u);UpdateClassicMobility(u);
                if(!u.Alive || u.TransportId!=0 || u.ParasiteHostId!=0)continue;
                if(u.Reload>0)u.Reload--;
                if(u.Definition.Miner && u.Order==UnitOrder.Harvest)UpdateHarvester(u);
                UpdateUnitCombat(u);
                if(ClassicUnitCanMove(u) && (u.Order==UnitOrder.Move || u.Order==UnitOrder.AttackMove && !InAttackRange(u,Find(u.TargetId)) || u.Order==UnitOrder.Harvest || u.Order==UnitOrder.Attack && !InAttackRange(u,Find(u.TargetId)) || u.Order==UnitOrder.AttackGround && !TerrainInAttackRange(u))) MoveUnit(u);
            }
            RemoveDestroyed();UpdatePower();CheckVictory();simulatingUnits=false;
            if(Tick%5==0)UpdateVisibility();
        }
        Tick++;
    }

    void SetGoal(Unit u,int x,int y,UnitOrder order)
    {
        var cell=NearestFree(x,y,domain:u.Definition.Domain,amphibious:u.Definition.Amphibious);
        if(cell is null)return;
        if(u.GoalX!=cell.Value.X || u.GoalY!=cell.Value.Y || u.Order!=order)
        {
            u.GoalX=cell.Value.X;u.GoalY=cell.Value.Y;u.Path.Clear();u.PathIndex=0;u.PathVersion=-1;u.BlockedTicks=0;u.NextPathTick=0;
        }
        u.Order=order;
    }
    void MoveUnit(Unit u)
    {
        if(u.Definition.Domain==MovementDomain.Air){MoveAirUnit(u);return;}
        if(u.CellX==u.GoalX && u.CellY==u.GoalY)
        {
            if(u.Order==UnitOrder.Move)u.Order=UnitOrder.Idle;
            if(u.Order==UnitOrder.AttackMove && u.TargetId==0)u.Order=UnitOrder.Idle;
            return;
        }
        if(u.PathVersion!=Map.NavigationVersion || u.PathIndex>=u.Path.Count)
        {
            if(u.PathVersion==Map.NavigationVersion && Tick<u.NextPathTick)return;
            if(pathsThisTick>=48)return;
            var goal=NearestFree(u.GoalX,u.GoalY,domain:u.Definition.Domain,amphibious:u.Definition.Amphibious);
            if(goal is null)return;
            u.GoalX=goal.Value.X;u.GoalY=goal.Value.Y;
            u.Path=pathfinder.Find(u.CellX,u.CellY,u.GoalX,u.GoalY,u.BlockedTicks>=12?occupied:null,u.Definition.Domain,u.Definition.Amphibious);
            u.PathIndex=0;u.PathVersion=Map.NavigationVersion;pathsThisTick++;PathSearches++;
            if(u.Path.Count==0){u.NextPathTick=Tick+10+u.Id%7;u.BlockedTicks++;return;}
            // A* edges run from cell centers. A blocked unit can be off-center
            // inside its start cell; joining the first new diagonal directly can
            // cross the occupied neighbor it is trying to avoid. Finish this safe
            // in-cell leg before following any new center-to-center path edges.
            var start=Map.Index(u.CellX,u.CellY);
            if(u.X!=u.CellX*Rules.Scale+Rules.Scale/2 || u.Y!=u.CellY*Rules.Scale+Rules.Scale/2)u.Path.Insert(0,start);
            u.NextPathTick=Tick;
        }
        var cell=u.Path[u.PathIndex];var tx=(cell%Map.Width)*Rules.Scale+Rules.Scale/2;var ty=(cell/Map.Width)*Rules.Scale+Rules.Scale/2;
        var dx=tx-u.X;var dy=ty-u.Y;var length=IntegerSqrt((long)dx*dx+(long)dy*dy);
        var speed=u.Definition.Speed;var nx=length<=speed?tx:u.X+(int)((long)dx*speed/length);var ny=length<=speed?ty:u.Y+(int)((long)dy*speed/length);
        var oldCell=Map.Index(u.CellX,u.CellY);var newCell=Map.Index(nx/Rules.Scale,ny/Rules.Scale);
        if(!CanOccupyUnitPosition(u,nx,ny) || Map.BuildingBlocksMovement(u.X,u.Y,nx,ny,UnitCollisionRadius(u))
            || newCell!=oldCell && (!Map.CanTraverseMovement(new Cell(u.CellX,u.CellY),new Cell(nx/Rules.Scale,ny/Rules.Scale),u.Definition.Domain,u.Definition.Amphibious) || occupied[newCell] >= (u.Definition.Infantry?3:1)))
        {
            u.BlockedTicks++;
            if(u.BlockedTicks%16==0)u.PathVersion=-1;
            if(u.BlockedTicks>=80)
            {
                var alt=NearestFree(u.GoalX,u.GoalY,true,domain:u.Definition.Domain,amphibious:u.Definition.Amphibious);
                if(alt is not null){u.GoalX=alt.Value.X;u.GoalY=alt.Value.Y;u.PathVersion=-1;}
            }
            return;
        }
        if(newCell!=oldCell){occupied[oldCell]=Math.Max(0,occupied[oldCell]-1);occupied[newCell]++;}
        u.X=nx;u.Y=ny;u.BodyFacingX=dx;u.BodyFacingY=dy;u.FacingX=dx;u.FacingY=dy;u.BlockedTicks=0;
        if(nx==tx && ny==ty)u.PathIndex++;
    }
    public static int IntegerSqrt(long value)
    {
        if(value<=0)return 0;
        ulong n=(ulong)value;ulong result=0;ulong bit=1UL<<62;
        while(bit>n)bit>>=2;
        while(bit!=0){if(n>=result+bit){n-=result+bit;result=(result>>1)+bit;}else result>>=1;bit>>=2;}
        return (int)result;
    }
    public static long DistanceSquared(Entity a,Entity b){var dx=(long)a.X-b.X;var dy=(long)a.Y-b.Y;return dx*dx+dy*dy;}

    void UpdateVisibility()
    {
        PrepareClassicVisibility();
        foreach(var p in Players)
        {
            if(p.Defeated)continue;
            foreach(var u in Units)if(u.Alive && u.TransportId==0 && u.ParasiteHostId==0 && Allied(p.Id,u.Owner))Reveal(p,u.CellX,u.CellY,u.Sight);
            foreach(var b in Buildings)if(b.Alive && Allied(p.Id,b.Owner))Reveal(p,b.CellX,b.CellY,b.Sight);
        }
        ApplyClassicEconomyVisibility();
    }
    void Reveal(PlayerState p,int x,int y,int radius)
    {
        if(!sightRows.TryGetValue(radius,out var rows))
        {
            rows=new int[radius*2+1];for(var dy=-radius;dy<=radius;dy++)rows[dy+radius]=IntegerSqrt((long)radius*radius-(long)dy*dy);sightRows.Add(radius,rows);
        }
        // Intersect each sight-circle row with the projected rectangle, using cell
        // centers U=cx-cy and V=cx+cy+1. Decorative exterior never receives fog bits.
        for(var cy=Math.Max(0,y-radius);cy<=Math.Min(Map.Height-1,y+radius);cy++)
        {
            var extent=rows[cy-y+radius];
            var left=Math.Max(Math.Max(0,x-extent),Math.Max(cy+Map.PlayableMinU,Map.PlayableMinV-cy-1));
            var right=Math.Min(Math.Min(Map.Width-1,x+extent),Math.Min(cy+Map.PlayableMaxU,Map.PlayableMaxV-cy-1));
            for(var cx=left;cx<=right;cx++)p.Visibility[cy*Map.Width+cx]|=ClassicVisibilityRules.Explored|ClassicVisibilityRules.DirectSight;
        }
    }
    void RemoveDestroyed()
    {
        foreach(var b in Buildings)
        {
            if(b.Alive)continue;
            ApplyClassicDestroyed(b);
            for(var dy=0;dy<b.Definition.Height;dy++)for(var dx=0;dx<b.Definition.Width;dx++)Map.Buildings[Map.Index(b.CellLeft+dx,b.CellTop+dy)]=0;
            Map.NavigationVersion++;entities.Remove(b.Id);
        }
        Buildings.RemoveAll(b=>!b.Alive);
        var removed=new HashSet<int>();
        while(true)
        {
            var dead=Units.Where(u=>!u.Alive && !removed.Contains(u.Id)).ToArray();if(dead.Length==0)break;
            foreach(var u in dead){removed.Add(u.Id);ApplyClassicDestroyed(u);ApplyMobilityDestroyed(u);entities.Remove(u.Id);}
        }
        Units.RemoveAll(u=>!u.Alive);
    }
    void DefeatPlayer(PlayerState player)
    {
        player.Defeated=true;ClearClassicBuildingProduction(player);
        foreach(var asset in Buildings.Cast<Entity>().Concat(Units).Where(e=>e.Owner==player.Id && e.Alive))
        {
            if(asset is Building producer)ClearClassicProducerQueue(producer);
            asset.Health=0;
            Events.Add(new CombatEvent(Tick,0,asset.Id,asset.X,asset.Y,0,true,asset.X,asset.Y,
                TargetInfantry:asset is Unit{Definition.Infantry:true},Secondary:true));
        }
    }
    void CheckVictory()
    {
        foreach(var p in Players)
        {
            if(p.Defeated)continue;
            var hasBuildings=Buildings.Any(b=>b.Alive && b.Owner==p.Id);
            var hasSurvivors=Settings.ShortGame?Units.Any(u=>u.Alive && u.Owner==p.Id && u.Type==UnitType.Mcv):Units.Any(u=>u.Alive && u.Owner==p.Id);
            if(!hasBuildings && !hasSurvivors)DefeatPlayer(p);
        }
        var teams=Players.Where(p=>!p.Defeated).Select(p=>p.Setup.Team).Distinct().ToArray();
        if(teams.Length<=1){Finished=true;WinningTeam=teams.Length==1?teams[0]:-1;}
    }
}
