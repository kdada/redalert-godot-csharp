namespace RedAlert.Simulation;

public sealed partial class Unit
{
    public int Altitude;
    public int PendingTransportId;
    public int AirReloadTick;
}

public sealed partial class World
{
    static bool OccupiesSurface(Unit unit)=>unit.Alive && unit.TransportId==0 && unit.ParasiteHostId==0 && unit.Definition.Domain!=MovementDomain.Air;
    static int CruisingAltitude(UnitDef definition)=>(definition.Type==UnitType.Kirov?7:definition.Infantry?3:5)*Rules.Scale;
    void SpawnClassicStartingForce(PlayerState player)
    {
        var start=Map.Spawns[player.Id];SpawnUnit(player.Id,UnitType.Mcv,start.X,start.Y);
        (int X,int Y)[] offsets=[(-5,-3),(-3,-5),(5,-3),(-3,5),(5,3),(3,5),(-5,3),(3,-5),(-6,0),(0,-6)];
        for(var i=0;i<Settings.StartingUnits;i++)
        {
            var offset=offsets[i%offsets.Length];var type=i%3==0?Rules.BasicTank(player.Setup.Country):Rules.BasicInfantry(player.Setup.Country);
            SpawnUnit(player.Id,type,start.X+offset.X,start.Y+offset.Y);
        }
    }
    // Original art.ini [GAAIRC] (also AMRADR via Image=GAAIRC):
    // DockingOffset0..3=(0,-128,0),(0,128,0),(256,-128,0),(256,128,0).
    // XY are relative to the foundation center, in 256 leptons per cell.
    // All source Z offsets are zero: parked aircraft remain ground targets.
    static readonly (int X,int Y)[] classicAirDockOffsets=[(0,-128),(0,128),(256,-128),(256,128)];
    public (int X,int Y)? ClassicAircraftDockPosition(Building airport,int slot)
    {
        if(airport.Definition.AirCapacity<=0 || slot<0 || slot>=airport.Definition.AirCapacity || slot>=classicAirDockOffsets.Length)return null;
        var offset=classicAirDockOffsets[slot];
        return (airport.X+offset.X*Rules.Scale/256,airport.Y+offset.Y*Rules.Scale/256);
    }
    public int ClassicAircraftDockSlot(Unit aircraft,Building airport)=>Units.Count(other=>other.Alive && other.Definition.AirportBound
        && other.Owner==airport.Owner && other.AircraftDockId==airport.Id && other.Id<aircraft.Id);
    public Cell? FindProductionExit(Building factory,UnitDef definition)
    {
        if(definition.AirportBound)
        {
            var count=Units.Count(u=>u.Alive && u.Owner==factory.Owner && u.AircraftDockId==factory.Id && u.Definition.AirportBound);
            var position=ClassicAircraftDockPosition(factory,count);
            return position is {} dock?new Cell(dock.X/Rules.Scale,dock.Y/Rules.Scale):null;
        }
        if(definition.Domain==MovementDomain.Air)
            return NearestFree(factory.CellLeft+factory.Definition.Width+1,factory.CellTop+factory.Definition.Height/2,true,domain:definition.Domain,amphibious:definition.Amphibious);
        return ClassicProductionExit(factory,definition).Cell;
    }
    public bool OrderDeploy(GameCommand command,PlayerState player)
    {
        var accepted=false;
        foreach(var id in command.Actors.Distinct().Order())
        {
            if(Find(id) is Unit unit && unit.Owner==player.Id && unit.TransportId==0)
            {
                if(unit.Type==UnitType.Mcv)accepted|=DeployConstructionVehicle(unit);
                else if(unit.Definition.Passengers>0)accepted|=UnloadTransport(unit);
                else accepted|=DeployClassicUnit(unit);
            }
            else if(Find(id) is Building{Type:BuildingType.ConstructionYard,Complete:true} yard && yard.Owner==player.Id && Settings.MCVRepacks)
                accepted|=RepackConstructionYard(yard);
        }
        return accepted;
    }
    bool DeployConstructionVehicle(Unit unit)
    {
        var definition=Rules.Building(BuildingType.ConstructionYard,unit.OriginCountry);
        // Even foundations use the northern central cell as their deploy pivot.
        var x=unit.CellX-(definition.Width-1)/2;var y=unit.CellY-(definition.Height-1)/2;
        var health=unit.Health;unit.Health=0;
        if(!Map.FootprintFree(definition,x,y) || BuildingAreaHasUnits(x,y,definition.Width,definition.Height) || EntityCount>=Rules.MaxEntities)
        {unit.Health=health;return false;}
        var building=SpawnBuilding(unit.Owner,BuildingType.ConstructionYard,x,y,false,unit.OriginCountry);
        building.RemainingBuildTicks=Rules.BuildingPlacementTicks;building.BuildDurationTicks=Rules.BuildingPlacementTicks;
        building.Health=Math.Max(1,definition.Health*health/unit.MaxHealth);return true;
    }
    bool RepackConstructionYard(Building building)
    {
        if(EntityCount>=Rules.MaxEntities)return false;
        var health=building.Health;building.Health=0;
        for(var dy=0;dy<building.Definition.Height;dy++)for(var dx=0;dx<building.Definition.Width;dx++)
            Map.Buildings[Map.Index(building.CellLeft+dx,building.CellTop+dy)]=0;
        var unit=SpawnUnit(building.Owner,UnitType.Mcv,building.CellLeft+(building.Definition.Width-1)/2,building.CellTop+(building.Definition.Height-1)/2,building.FoundationCountry);
        unit.Health=Math.Max(1,unit.MaxHealth*health/building.MaxHealth);return true;
    }
    static int PassengerSpace(Unit unit)=>unit.Definition.Infantry?1:3;
    bool CanLoad(Unit passenger,Unit transport)=>passenger.Id!=transport.Id && passenger.Alive && transport.Alive
        && Allied(passenger.Owner,transport.Owner) && passenger.TransportId==0 && passenger.Definition.Domain==MovementDomain.Land
        && passenger.Definition.Passengers==0 && !passenger.Definition.Miner
        && (transport.Type!=UnitType.Nighthawk || passenger.Definition.Infantry)
        && transport.Definition.Passengers>0
        && transport.Passengers.Select(Find).OfType<Unit>().Sum(PassengerSpace)+PassengerSpace(passenger)<=transport.Definition.Passengers;
    public bool OrderEnterTransport(GameCommand command,PlayerState player)
    {
        if(Find(command.Target) is not Unit transport || transport.Owner!=player.Id || transport.Definition.Passengers<=0)return false;
        var accepted=false;
        foreach(var id in command.Actors.Distinct().Order())
        {
            if(Find(id) is not Unit passenger || passenger.Owner!=player.Id || !CanLoad(passenger,transport))continue;
            passenger.PendingTransportId=transport.Id;passenger.TargetId=0;
            SetGoal(passenger,transport.CellX,transport.CellY,UnitOrder.Move);accepted=true;
        }
        return accepted;
    }
    public bool OrderUnload(GameCommand command,PlayerState player)
    {
        var accepted=false;
        foreach(var id in command.Actors.Distinct().Order())
            if(Find(id) is Unit transport && transport.Owner==player.Id)accepted|=UnloadTransport(transport);
        return accepted;
    }
    bool UnloadTransport(Unit transport)
    {
        var unloaded=false;
        foreach(var id in transport.Passengers.ToArray())
        {
            if(Find(id) is not Unit passenger){transport.Passengers.Remove(id);continue;}
            Cell? exit=null;
            for(var radius=0;radius<=2 && exit is null;radius++)for(var dy=-radius;dy<=radius && exit is null;dy++)for(var dx=-radius;dx<=radius;dx++)
            {
                if(radius>0 && Math.Abs(dx)!=radius && Math.Abs(dy)!=radius)continue;
                var x=transport.CellX+dx;var y=transport.CellY+dy;
                if(!Map.Walkable(x,y) || Units.Any(u=>OccupiesSurface(u) && u.CellX==x && u.CellY==y))continue;
                var fx=x*Rules.Scale+Rules.Scale/2;var fy=y*Rules.Scale+Rules.Scale/2;
                if(CanOccupyUnitPosition(passenger,fx,fy)){exit=new Cell(x,y);break;}
            }
            if(exit is not { } cell)continue;
            passenger.TransportId=0;passenger.PendingTransportId=0;passenger.Altitude=0;passenger.X=cell.X*Rules.Scale+Rules.Scale/2;passenger.Y=cell.Y*Rules.Scale+Rules.Scale/2;
            passenger.GoalX=cell.X;passenger.GoalY=cell.Y;passenger.Order=UnitOrder.Idle;passenger.Path.Clear();passenger.PathVersion=-1;
            occupied[Map.Index(cell.X,cell.Y)]++;transport.Passengers.Remove(id);unloaded=true;
        }
        return unloaded;
    }
    void UpdateClassicMobility(Unit unit)
    {
        if(unit.TransportId!=0)
        {
            if(Find(unit.TransportId) is Unit carrier){unit.X=carrier.X;unit.Y=carrier.Y;unit.Altitude=carrier.Altitude;}
            else unit.Health=0;
            return;
        }
        if(unit.PendingTransportId!=0)
        {
            if(Find(unit.PendingTransportId) is not Unit transport || !CanLoad(unit,transport))unit.PendingTransportId=0;
            else if(DistanceSquared(unit,transport)<=4L*Rules.Scale*Rules.Scale)
            {
                if(OccupiesSurface(unit)){var index=Map.Index(unit.CellX,unit.CellY);occupied[index]=Math.Max(0,occupied[index]-1);}
                unit.TransportId=transport.Id;unit.PendingTransportId=0;unit.Order=UnitOrder.Idle;unit.TargetId=0;unit.Path.Clear();
                transport.Passengers.Add(unit.Id);unit.X=transport.X;unit.Y=transport.Y;unit.Altitude=transport.Altitude;return;
            }
            else if(Tick%10==unit.Id%10)SetGoal(unit,transport.CellX,transport.CellY,UnitOrder.Move);
        }
        if(unit.Definition.Domain!=MovementDomain.Air)return;
        if(!unit.Definition.AirportBound){unit.Altitude=CruisingAltitude(unit.Definition);return;}
        var dock=Find(unit.AircraftDockId) as Building;
        if(dock is null || dock.Owner!=unit.Owner || ClassicAircraftDockPosition(dock,ClassicAircraftDockSlot(unit,dock)) is null)
        {
            dock=Buildings.Where(b=>b.Alive && b.Complete && b.Owner==unit.Owner && b.Definition.AirCapacity>0
                && Units.Count(other=>other.Alive && other.Definition.AirportBound && other.Owner==b.Owner && other.Id!=unit.Id && other.AircraftDockId==b.Id)<b.Definition.AirCapacity)
                .OrderBy(b=>DistanceSquared(unit,b)).ThenBy(b=>b.Id).FirstOrDefault();
            unit.AircraftDockId=dock?.Id??0;
        }
        if(unit.Ammo<=0)unit.Returning=true;
        if(dock is null)
        {
            if(unit.Returning)unit.Health=0;
            else unit.Altitude=Math.Min(CruisingAltitude(unit.Definition),unit.Altitude+Rules.Scale/2);
            return;
        }
        var position=ClassicAircraftDockPosition(dock,ClassicAircraftDockSlot(unit,dock));
        if(position is not {} pad){unit.AircraftDockId=0;return;}
        var fx=pad.X;var fy=pad.Y;var dx=fx/Rules.Scale;var dy=fy/Rules.Scale;
        var distance=(long)(unit.X-fx)*(unit.X-fx)+(long)(unit.Y-fy)*(unit.Y-fy);
        var landing=(unit.Returning || unit.Order==UnitOrder.Idle) && distance<Rules.Scale*Rules.Scale;
        if(unit.Returning || unit.Order==UnitOrder.Idle && distance>=Rules.Scale*Rules.Scale/4)
        {
            unit.TargetId=0;SetGoal(unit,dx,dy,UnitOrder.Move);
            if(distance<Rules.Scale*Rules.Scale/4){unit.X=fx;unit.Y=fy;unit.Order=UnitOrder.Idle;}
        }
        unit.Altitude=landing?Math.Max(0,unit.Altitude-Rules.Scale/2):Math.Min(CruisingAltitude(unit.Definition),unit.Altitude+Rules.Scale/2);
        if(landing && unit.Altitude==0 && unit.Ammo<unit.Definition.Ammo && Tick>=unit.AirReloadTick && ClassicBuildingOperational(dock))
        {
            unit.Ammo++;unit.AirReloadTick=Tick+80;
            if(unit.Ammo>=unit.Definition.Ammo){unit.Returning=false;unit.Order=UnitOrder.Idle;unit.TargetId=0;}
        }
    }
    void MoveAirUnit(Unit unit)
    {
        var tx=unit.GoalX*Rules.Scale+Rules.Scale/2;var ty=unit.GoalY*Rules.Scale+Rules.Scale/2;
        var dx=tx-unit.X;var dy=ty-unit.Y;var length=IntegerSqrt((long)dx*dx+(long)dy*dy);
        if(length==0){if(unit.Order==UnitOrder.Move)unit.Order=UnitOrder.Idle;return;}
        var speed=unit.Definition.Speed;
        var nx=length<=speed?tx:unit.X+(int)((long)dx*speed/length);var ny=length<=speed?ty:unit.Y+(int)((long)dy*speed/length);
        if(!Map.IsPlayablePoint(nx/(double)Rules.Scale,ny/(double)Rules.Scale))return;
        unit.X=nx;unit.Y=ny;unit.FacingX=unit.BodyFacingX=dx;unit.FacingY=unit.BodyFacingY=dy;
        if(nx==tx && ny==ty && unit.Order==UnitOrder.Move)unit.Order=UnitOrder.Idle;
    }
    void ApplyMobilityDestroyed(Unit unit)
    {
        foreach(var id in unit.Passengers)if(Find(id) is Unit passenger)passenger.Health=0;
        unit.Passengers.Clear();
    }
}
