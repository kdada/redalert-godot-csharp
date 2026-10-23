namespace RedAlert.Simulation;

public enum ClassicCrateKind { Credits, Repair, Reveal, Veteran }
public sealed record ClassicCrate(int Id,int X,int Y,ClassicCrateKind Kind,int SpawnTick,int ExpiresTick,int Value=0);
public readonly record struct ClassicCratePickup(int Tick,int CrateId,int Player,int UnitId,ClassicCrateKind Kind,int Value);
public sealed record ClassicClone(UnitType Type,Country OriginCountry,int Owner);

// Original DATA: rules.ini General RepairPercent/RepairRate/RepairStep,
// PurifierBonus=.25; HARV Storage40, CMIN Storage20; Riparius.Value25,
// Cruentus.Value50; CrateRules and Powerups (lines642..656,22496..22515).
internal static class ClassicEconomyRules
{
    public const int OreValue=25,GemValue=50,RepairStep=8,RepairTicks=5;
    public const int HarvestTicks=5,BalesPerHarvest=2,CloneBacklogLimit=64;
    public const int CrateRadius=3,CrateMoneyMaximum=2000,CrateRegenTicks=900;
    // Bound the reconstructed crate scheduler below the original maximum255.
    // Four implemented rewards retain their original relative shares20/10/10/20.
    public const int CrateMaximum=16,CrateLifetimeTicks=3600;
}

public sealed partial class Unit
{
    // Cargo remains credits; slots are separate because gems are worth twice ore.
    public int CargoBales;
}
public sealed partial class Building
{
    public bool FreeMinerPending;
    public List<ClassicClone> PendingClones=[];
}
public sealed partial class PlayerState
{
    public bool CrateMapRevealed;
}

public sealed partial class World
{
    public List<ClassicCrate> ClassicCrates {get;}=[];
    public List<ClassicCratePickup> ClassicCratePickups {get;}=[];
    public uint ClassicCrateRandomState;
    public int NextClassicCrateTick;
    public int NextClassicCrateId=1;

    public static int HarvesterStorageBales(UnitType type)=>type==UnitType.WarMiner?40:type==UnitType.ChronoMiner?20:0;
    public static int HarvesterOreCapacity(UnitType type)=>HarvesterStorageBales(type)*ClassicEconomyRules.OreValue;
    int ClassicResourceValue(int x,int y)
    {
        var cell=Map.OriginalCellAt(x,y);
        return cell is {ResourceKind:>0,ResourceValue:>0}?cell.ResourceValue:cell is {ResourceKind:2}?ClassicEconomyRules.GemValue:ClassicEconomyRules.OreValue;
    }
    static bool AtClassicHarvesterDock(Unit unit,Cell dock)=>Math.Abs(unit.CellX-dock.X)+Math.Abs(unit.CellY-dock.Y)<=1;
    Cell? FindClassicHarvesterLanding(Unit unit,Cell dock)
    {
        // Match the unloading radius and keep occupied destinations separate from
        // the stable dock. A fully blocked bay waits instead of stacking miners.
        Cell[] offsets=[new(0,0),new(-1,0),new(0,-1),new(1,0),new(0,1)];
        foreach(var offset in offsets)
        {
            var x=dock.X+offset.X;var y=dock.Y+offset.Y;
            if(!CanOccupyUnitPosition(unit,x*Rules.Scale+Rules.Scale/2,y*Rules.Scale+Rules.Scale/2)
                || Units.Any(other=>other.Id!=unit.Id && OccupiesSurface(other) && other.CellX==x && other.CellY==y))continue;
            return new Cell(x,y);
        }
        return null;
    }
    bool ClassicSupportPowered(Building building)=>ClassicBuildingOperational(building) && !Players[building.Owner].Defeated
        && Players[building.Owner].Powered && Players[building.Owner].PowerBlackoutUntil<=Tick;
    public int ClassicOreIncome(int player,int cargo)
    {
        if(player<0 || player>=Players.Length || cargo<=0)return 0;
        var purifiers=Buildings.Count(building=>building.Owner==player && building.Type==BuildingType.OrePurifier && ClassicSupportPowered(building));
        return (int)Math.Min(int.MaxValue,(long)cargo*(4+purifiers)/4);
    }
    void AddClassicCredits(int owner,int amount)=>Players[owner].Credits=(int)Math.Clamp((long)Players[owner].Credits+amount,0,int.MaxValue);
    static int ClassicRepairCost(int cost,int maximumHealth,int amount)=>Math.Max(1,(int)(((long)cost*15*amount+maximumHealth*100L-1)/(maximumHealth*100L)));

    public int ClassicAircraftReservations(Building producer,bool includeQueued=true)
    {
        var count=Units.Count(unit=>unit.Alive && unit.Owner==producer.Owner && unit.Definition.AirportBound && unit.AircraftDockId==producer.Id);
        if(includeQueued)count+=producer.Queue.Count(type=>Enum.IsDefined(type) && Rules.Production(type)==ProductionCategory.Aircraft);
        return count;
    }
    public bool HasClassicAircraftSlot(Building producer,bool includeQueued=true)=>producer.Definition.Produces!=ProductionCategory.Aircraft
        || ClassicAircraftReservations(producer,includeQueued)<producer.Definition.AirCapacity;

    public void NotifyClassicBuildingCompleted(Building building)
    {
        if(building.Alive && building.Complete && building.Type==BuildingType.Refinery)
        {building.FreeMinerPending=true;TrySpawnClassicFreeMiner(building);}
    }
    void TrySpawnClassicFreeMiner(Building refinery)
    {
        if(!refinery.FreeMinerPending || !ClassicBuildingOperational(refinery) || EntityCount>=Rules.MaxEntities || Players[refinery.Owner].Defeated)return;
        var type=Rules.Miner(refinery.FoundationCountry);var definition=Rules.Unit(type,refinery.FoundationCountry);
        var exit=FindProductionExit(refinery,definition);if(exit is null)return;
        var miner=SpawnUnit(refinery.Owner,type,exit.Value.X,exit.Value.Y,refinery.FoundationCountry);
        miner.Ammo=definition.Ammo;miner.Order=UnitOrder.Harvest;refinery.FreeMinerPending=false;
    }
    bool ClassicCloneBacklogAvailable(int owner)=>Buildings.Where(building=>building.Owner==owner && building.Type==BuildingType.CloningVats && ClassicSupportPowered(building))
        .All(building=>building.PendingClones.Count<ClassicEconomyRules.CloneBacklogLimit);
    void QueueClassicClones(Unit original,Country originCountry)
    {
        foreach(var vat in Buildings.Where(building=>building.Owner==original.Owner && building.Type==BuildingType.CloningVats && ClassicSupportPowered(building)).OrderBy(building=>building.Id))
            if(vat.PendingClones.Count<ClassicEconomyRules.CloneBacklogLimit)vat.PendingClones.Add(new(original.Type,originCountry,original.Owner));
    }
    void UpdateClassicPendingUnits()
    {
        foreach(var building in Buildings.OrderBy(building=>building.Id))
        {
            if(!building.Alive || Players[building.Owner].Defeated)continue;
            if(building.FreeMinerPending)TrySpawnClassicFreeMiner(building);
            if(building.Type!=BuildingType.CloningVats || !ClassicSupportPowered(building))continue;
            building.PendingClones.RemoveAll(job=>job.Owner!=building.Owner);
            if(building.PendingClones.Count==0 || EntityCount>=Rules.MaxEntities)continue;
            var clone=building.PendingClones[0];var definition=Rules.Unit(clone.Type,clone.OriginCountry);
            var exit=FindProductionExit(building,definition);if(exit is null)continue;
            var unit=SpawnUnit(building.Owner,clone.Type,exit.Value.X,exit.Value.Y,clone.OriginCountry);
            InitializeClassicProduct(unit,building,definition);building.PendingClones.RemoveAt(0);
        }
    }

    static bool ClassicCanGainVeterancy(Unit unit)=>unit.Type is not (UnitType.Engineer or UnitType.Dog or UnitType.Spy or UnitType.CrazyIvan
        or UnitType.ChronoIvan or UnitType.Terrorist or UnitType.DemolitionTruck or UnitType.TerrorDrone or UnitType.ChronoMiner
        or UnitType.Mcv or UnitType.AmphibiousTransport);
    static long ClassicDepotDistanceSquared(Unit unit,Building depot)
    {
        var left=depot.CellLeft*Rules.Scale;var top=depot.CellTop*Rules.Scale;
        var right=(depot.CellLeft+depot.Definition.Width)*Rules.Scale;var bottom=(depot.CellTop+depot.Definition.Height)*Rules.Scale;
        var dx=(long)unit.X-Math.Clamp(unit.X,left,right);var dy=(long)unit.Y-Math.Clamp(unit.Y,top,bottom);return dx*dx+dy*dy;
    }
    void UpdateClassicServiceDepots()
    {
        if(Tick%ClassicEconomyRules.RepairTicks!=0)return;
        var serviced=new HashSet<int>();
        foreach(var depot in Buildings.Where(building=>building.Type==BuildingType.ServiceDepot && ClassicSupportPowered(building)).OrderBy(building=>building.Id))
        {
            // The current map collider treats this foundation as solid. Its clear
            // perimeter is the service bay until drive-on foundations are integrated.
            var vehicle=Units.Where(unit=>unit.Alive && unit.Owner==depot.Owner && unit.TransportId==0 && !unit.Definition.Infantry
                && unit.Definition.Domain==MovementDomain.Land && (unit.Health<unit.MaxHealth || unit.ParasiteId!=0) && !serviced.Contains(unit.Id)
                && unit.ErasedById==0 && ClassicDepotDistanceSquared(unit,depot)<=(long)Rules.Scale*Rules.Scale)
                .OrderBy(unit=>ClassicDepotDistanceSquared(unit,depot)).ThenBy(unit=>unit.Id).FirstOrDefault();
            if(vehicle is null)continue;
            CureClassicParasite(vehicle);serviced.Add(vehicle.Id);
            var amount=Math.Min(ClassicEconomyRules.RepairStep,vehicle.MaxHealth-vehicle.Health);if(amount<=0)continue;
            var cost=ClassicRepairCost(vehicle.Definition.Cost,vehicle.MaxHealth,amount);var owner=Players[vehicle.Owner];
            if(owner.Credits<cost)continue;
            owner.Credits-=cost;vehicle.Health+=amount;
        }
    }

    // The Lead calls this once per simulation tick. It mutates no wall-clock state.
    public void UpdateClassicEconomy()
    {
        ClassicCratePickups.Clear();if(Finished)return;
        UpdateClassicPendingUnits();UpdateClassicServiceDepots();UpdateClassicCrates();
        foreach(var player in Players)
        {
            if(player.Setup.Country!=Country.America || !Buildings.Any(b=>b.Alive && b.Complete && b.Owner==player.Id && b.Definition.AirCapacity>0)){player.SupportReadyTick=-1;continue;}
            if(player.SupportReadyTick<0)player.SupportReadyTick=Tick+1200;
            else if(!player.Powered || player.PowerBlackoutUntil>Tick || !Buildings.Any(b=>b.Owner==player.Id && b.Definition.AirCapacity>0 && ClassicBuildingOperational(b)))player.SupportReadyTick++;
        }
    }
    public void ApplyClassicEconomyVisibility()
    {
        ApplyClassicSatelliteVisibility();
        ApplyClassicGapShroud();
    }
    int NextClassicCrateRandom(int limit)
    {
        if(ClassicCrateRandomState==0)ClassicCrateRandomState=Settings.Seed^0x9e3779b9u;
        if(ClassicCrateRandomState==0)ClassicCrateRandomState=0xa341316cu;
        var value=ClassicCrateRandomState;value^=value<<13;value^=value>>17;value^=value<<5;ClassicCrateRandomState=value;
        return (int)(value%(uint)limit);
    }
    void UpdateClassicCrates()
    {
        if(!Settings.Crates){ClassicCrates.Clear();return;}
        ClassicCrates.RemoveAll(crate=>crate.ExpiresTick<=Tick);
        if(Tick>=NextClassicCrateTick)
        {
            var initial=ClassicCrateRandomState==0;
            var target=initial?Math.Clamp(Players.Length,1,ClassicEconomyRules.CrateMaximum):1;
            var maximum=Math.Min(ClassicEconomyRules.CrateMaximum,Math.Max(1,Players.Length*2));
            for(var count=0;count<target && ClassicCrates.Count<maximum;count++)TrySpawnClassicCrate();
            NextClassicCrateTick=Tick+ClassicEconomyRules.CrateRegenTicks*4/5+NextClassicCrateRandom(ClassicEconomyRules.CrateRegenTicks*2/5+1);
        }
        foreach(var crate in ClassicCrates.ToArray())
        {
            var collector=Units.Where(unit=>ClassicCanCollectCrate(unit,crate)).OrderBy(unit=>unit.Id).FirstOrDefault();
            if(collector is not null)TryPickupClassicCrate(crate.Id,collector);
        }
    }
    bool TrySpawnClassicCrate()
    {
        for(var attempt=0;attempt<128;attempt++)
        {
            var index=NextClassicCrateRandom(Map.Ore.Length);var x=index%Map.Width;var y=index/Map.Width;
            if(!Map.IsPlayable(x,y) || Map.Buildings[index]!=0 || Map.Ore[index]>0 || Map.Ground[index]==Terrain.Rock || Map.Bridges[index]
                || ClassicCrates.Any(crate=>crate.X==x && crate.Y==y)
                || Units.Any(unit=>unit.Alive && unit.TransportId==0 && unit.Definition.Domain!=MovementDomain.Air && unit.CellX==x && unit.CellY==y))continue;
            if(Map.Ground[index]==Terrain.Water)
            {if(Map.OriginalCellAt(x,y) is {WaterPassable:false})continue;}
            else if(!Map.Walkable(x,y))continue;
            var share=NextClassicCrateRandom(60);
            var kind=share<20?ClassicCrateKind.Credits:share<30?ClassicCrateKind.Repair:share<40?ClassicCrateKind.Reveal:ClassicCrateKind.Veteran;
            // All four source powerups allow water. The selected money range is a
            // bounded reconstruction; original Money data specifies maximum2000.
            var amount=kind==ClassicCrateKind.Credits?500+NextClassicCrateRandom(ClassicEconomyRules.CrateMoneyMaximum-500+1):kind==ClassicCrateKind.Veteran?1:0;
            ClassicCrates.Add(new(NextClassicCrateId++,x,y,kind,Tick,Tick+ClassicEconomyRules.CrateLifetimeTicks,amount));return true;
        }
        return false;
    }
    bool ClassicCanCollectCrate(Unit unit,ClassicCrate crate)=>unit.Alive && unit.Owner>=0 && unit.Owner<Players.Length && !Players[unit.Owner].Defeated
        && unit.TransportId==0 && unit.ParasiteHostId==0 && ClassicUnitCanAct(unit) && unit.Definition.Domain!=MovementDomain.Air && unit.CellX==crate.X && unit.CellY==crate.Y;
    public bool TryPickupClassicCrate(int crateId,Unit collector)
    {
        if(!Settings.Crates)return false;
        var crate=ClassicCrates.FirstOrDefault(candidate=>candidate.Id==crateId);
        if(crate is null || crate.ExpiresTick<=Tick || !ClassicCanCollectCrate(collector,crate))return false;
        ClassicCrates.Remove(crate);var player=Players[collector.Owner];
        switch(crate.Kind)
        {
            case ClassicCrateKind.Credits:AddClassicCredits(collector.Owner,Math.Clamp(crate.Value,0,ClassicEconomyRules.CrateMoneyMaximum));break;
            case ClassicCrateKind.Repair:
                foreach(var unit in Units)if(unit.Alive && unit.Owner==collector.Owner)unit.Health=unit.MaxHealth;
                foreach(var building in Buildings)if(building.Alive && building.Owner==collector.Owner)building.Health=building.MaxHealth;
                break;
            case ClassicCrateKind.Reveal:player.CrateMapRevealed=true;RevealClassicMap(player.Id);break;
            case ClassicCrateKind.Veteran:
                foreach(var unit in Units)
                    if(unit.Alive && unit.Owner==collector.Owner && unit.TransportId==0 && ClassicCanGainVeterancy(unit)
                        && DistanceSquared(unit,collector)<=(long)ClassicEconomyRules.CrateRadius*ClassicEconomyRules.CrateRadius*Rules.Scale*Rules.Scale)
                        unit.Kills=Math.Max(unit.Kills,Math.Min(6,(unit.Kills/3+1)*3));
                break;
        }
        ClassicCratePickups.Add(new(Tick,crate.Id,collector.Owner,collector.Id,crate.Kind,crate.Value));return true;
    }
}
