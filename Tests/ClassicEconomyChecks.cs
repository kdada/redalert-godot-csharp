using System.Diagnostics;
using System.Text.Json;
using RedAlert.Simulation;

namespace RedAlert.Tests;

// Small deterministic integration checks; the Lead owns runner registration/build.
public static class ClassicEconomyChecks
{
    const string FixtureId="classic-economy-check";
    static readonly Lazy<ClassicMapDefinition> Fixture=new(()=>
    {
        var cells=Enumerable.Range(0,96*96).Select(index=>
        {
            var x=index%96;var y=index/96;var water=y<16;var resource=x==30 && y==34?1:x==31 && y==34?2:0;
            return new ClassicMapCell{X=x,Y=y,SourceX=x,SourceY=y,Playable=true,Ground=water?Terrain.Water:Terrain.Grass,
                UnderlyingGround=water?Terrain.Water:Terrain.Grass,LandPassable=!water,WaterPassable=water,Buildable=!water,
                ResourceKind=resource,ResourceDensity=10,ResourceValue=resource==2?50:resource==1?25:0};
        }).ToArray();
        return ClassicMapDefinition.Parse(JsonSerializer.Serialize(new ClassicMapDefinition
        {
            Schema=1,Id=FixtureId,Name="Economy test fixture",Width=96,Height=96,RuntimeCells=cells,ShipyardsAllowed=true,
            PlayableBounds=new(){MinU=-95,MaxU=95,MinV=1,MaxV=191},
            Spawns=[new(){Index=0,X=20,Y=30,SourceX=20,SourceY=30},new(){Index=1,X=70,Y=60,SourceX=70,SourceY=60}]
        }));
    });
    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    static World New(bool crates=false,bool soviet=false)
    {
        ClassicMapRepository.Register(Fixture.Value);
        var settings=MatchSettings.LegacyFixture(2,271828) with
        {
            MapId=FixtureId,Crates=crates,ShortGame=false,StartingCredits=100000,
            Players=[new("Economy",soviet?Country.Russia:Country.America,0),new("Other",soviet?Country.America:Country.Russia,1)]
        };
        var world=new World(settings);foreach(var unit in world.Units){unit.Order=UnitOrder.Idle;unit.TargetId=0;}return world;
    }
    static Building Place(World world,BuildingType type,int owner=0)
    {
        var definition=Rules.Building(type,world.Players[owner].Setup.Country);
        for(var y=18;y<74;y++)for(var x=2;x<60;x++)
            if(world.CanUseBuildingFootprint(definition,x,y) && !world.BuildingAreaHasUnits(x,y,definition.Width,definition.Height))
                return world.SpawnBuilding(owner,type,x,y);
        throw new InvalidOperationException("No fixture foundation for "+type);
    }
    static void Power(World world){for(var count=0;count<5;count++)Place(world,BuildingType.PowerPlant);}
    static void Ready(Building producer,UnitType type)
    {producer.Queue.Add(type);producer.ProductionProgress=Rules.Unit(type,producer.FoundationCountry).BuildTicks;}
    static ClassicCrate AddCrate(World world,Unit collector,ClassicCrateKind kind,int value=0)
    {
        var crate=new ClassicCrate(world.NextClassicCrateId++,collector.CellX,collector.CellY,kind,world.Tick,world.Tick+100,value);
        world.ClassicCrates.Add(crate);return crate;
    }

    public static int Run(string[] args)
    {
        var results=new List<object>();var failed=0;var steps=0;var watch=Stopwatch.StartNew();
        void Test(string name,Action check)
        {
            try{check();results.Add(new{name,passed=true});Console.WriteLine("PASS "+name);}
            catch(Exception error){failed++;results.Add(new{name,passed=false,error=error.Message});Console.Error.WriteLine("FAIL "+name+": "+error.Message);}
        }
        void Step(World world)
        {Require(steps<512,"classic economy exceeded its 512-step budget");steps++;world.Step();}

        Test("aircraft slots include reservations without blocking their own queued jobs",()=>
        {
            var world=New();Power(world);var airport=Place(world,BuildingType.Radar);
            for(var count=0;count<4;count++)airport.Queue.Add(UnitType.Harrier);
            Require(world.ClassicAircraftReservations(airport)==4 && !world.HasClassicAircraftSlot(airport),"queued aircraft did not reserve four airport slots");
            Require(!world.CanTrainUnit(0,UnitType.Harrier,airport),"admission accepted a fifth reserved aircraft");
            for(var count=0;count<4;count++){airport.ProductionProgress=Rules.Unit(UnitType.Harrier).BuildTicks;Step(world);}
            var planes=world.Units.Where(unit=>unit.Alive && unit.Type==UnitType.Harrier).ToArray();
            Require(planes.Length==4 && airport.Queue.Count==0,"four reserved aircraft deadlocked against their own queue");
            Require(planes.All(unit=>unit.AircraftDockId==airport.Id && unit.Ammo==unit.Definition.Ammo && unit.OriginCountry==airport.FoundationCountry),"aircraft lost home/ammo/origin");
            Ready(airport,UnitType.Harrier);Step(world);
            Require(airport.Queue.Count==1 && world.Units.Count(unit=>unit.Alive && unit.Type==UnitType.Harrier)==4,"a full airport spawned an extra aircraft");
            planes[0].Health=0;Step(world);
            Require(airport.Queue.Count==0 && world.Units.Count(unit=>unit.Alive && unit.Type==UnitType.Harrier)==4,"a released aircraft slot did not resume the ready queue");
        });

        Test("barracks/factory/naval exits and captured producer origins remain distinct",()=>
        {
            var world=New();Power(world);Place(world,BuildingType.Radar);var factory=Place(world,BuildingType.WarFactory);
            var barracks=world.Buildings.First(building=>building.Owner==0 && building.Type==BuildingType.Barracks);
            Ready(barracks,UnitType.Rocketeer);Step(world);
            var rocketeer=world.Units.Single(unit=>unit.Type==UnitType.Rocketeer);
            Require(rocketeer.Definition.Domain==MovementDomain.Air && rocketeer.Definition.Category==ProductionCategory.Infantry,"flying infantry was sent through the aircraft category");
            Ready(factory,UnitType.Nighthawk);Step(world);
            Require(world.Units.Any(unit=>unit.Type==UnitType.Nighthawk && unit.Definition.Domain==MovementDomain.Air),"factory could not produce its helicopter");
            factory.FoundationCountry=Country.Russia;Ready(factory,UnitType.Rhino);Step(world);
            Require(world.Units.Any(unit=>unit.Owner==0 && unit.Type==UnitType.Rhino && unit.OriginCountry==Country.Russia),"captured factory used the current owner's Allied origin");
            var yard=world.SpawnBuilding(0,BuildingType.NavalYard,20,12);Ready(yard,UnitType.Destroyer);Step(world);
            var ship=world.Units.Single(unit=>unit.Type==UnitType.Destroyer);
            Require(ship.Definition.Domain==MovementDomain.Water && world.Map.Ground[world.Map.Index(ship.CellX,ship.CellY)]==Terrain.Water,"ship spawned on a land-only exit");
        });

        Test("paused jobs retain progress and queued BuildLimit units can finish",()=>
        {
            var world=New(soviet:true);Power(world);var factory=Place(world,BuildingType.WarFactory);Place(world,BuildingType.Radar);Place(world,BuildingType.BattleLab);
            Ready(factory,UnitType.Rhino);world.Players[0].PowerBlackoutUntil=world.Tick+10;Step(world);
            Require(world.Players[0].GeneratedPower==0 && factory.Queue.Count==1,"blackout did not stop generation/production");
            world.Players[0].PowerBlackoutUntil=0;factory.ErasedById=world.Units.First(unit=>unit.Owner==1).Id;Step(world);
            Require(factory.Queue.Count==1,"erased producer completed a unit");
            factory.ErasedById=0;Step(world);Require(factory.Queue.Count==0,"restored producer did not finish its ready job");
            var radar=world.Buildings.First(building=>building.Owner==0 && building.Type==BuildingType.Radar);Ready(factory,UnitType.V3Launcher);radar.Health=0;Step(world);
            Require(factory.Queue.Count==1,"job advanced after losing its radar prerequisite");Place(world,BuildingType.Radar);Step(world);
            Require(factory.Queue.Count==0,"restoring radar did not resume its ready job");
            var barracks=world.Buildings.First(building=>building.Owner==0 && building.Type==BuildingType.Barracks);
            world.Players[0].StolenSovietTech=true;Ready(barracks,UnitType.YuriPrime);
            Require(!world.CanProduceUnit(0,UnitType.YuriPrime),"fixture did not expose the queue-admission BuildLimit case");
            Step(world);Require(barracks.Queue.Count==0 && world.Units.Count(unit=>unit.Type==UnitType.YuriPrime)==1,"queue processing reapplied its own BuildLimit admission gate");
        });

        Test("refinery completion and vats preserve original faction prototypes",()=>
        {
            var world=New(soviet:true);Power(world);var vat=Place(world,BuildingType.CloningVats);
            var barracks=world.Buildings.First(building=>building.Owner==0 && building.Type==BuildingType.Barracks);
            var before=world.Units.Count(unit=>unit.Owner==0 && unit.Type==UnitType.Conscript);
            Ready(barracks,UnitType.Conscript);Step(world);world.UpdateClassicEconomy();
            Require(world.Units.Count(unit=>unit.Owner==0 && unit.Type==UnitType.Conscript)==before+2 && vat.PendingClones.Count==0,"vats did not deliver exactly one free clone");
            var refinery=Place(world,BuildingType.Refinery);refinery.Owner=1;var miners=world.Units.Count;
            world.NotifyClassicBuildingCompleted(refinery);
            var free=world.Units.Skip(miners).Single();
            Require(free.Owner==1 && free.Type==UnitType.WarMiner && free.OriginCountry==Country.Russia && free.Order==UnitOrder.Harvest,"captured Soviet refinery gave its Allied owner a Chrono Miner");
        });

        Test("ore/gems occupy physical holds and powered purifiers increase deposits",()=>
        {
            foreach(var type in new[]{UnitType.ChronoMiner,UnitType.WarMiner})foreach(var gems in new[]{false,true})
            {
                var world=New();Power(world);Place(world,BuildingType.OrePurifier);
                var x=gems?31:30;const int y=34;var initial=world.Map.Ore[world.Map.Index(x,y)];
                var miner=world.SpawnUnit(0,type,x,y,type==UnitType.WarMiner?Country.Russia:Country.America);miner.Order=UnitOrder.Harvest;
                var loops=World.HarvesterStorageBales(type)/2*5;
                for(var tick=0;tick<loops;tick++)Step(world);
                var expected=World.HarvesterOreCapacity(type)*(gems?2:1);
                Require(miner.Cargo==expected && miner.CargoBales==World.HarvesterStorageBales(type),$"{type}/{gems}: wrong cash or physical capacity");
                Require(world.Map.Ore[world.Map.Index(x,y)]==initial-expected,"mining created or double-valued resource credits");
                var refinery=world.Buildings.First(building=>building.Owner==0 && building.Type==BuildingType.Refinery);
                var exit=world.FindProductionExit(refinery,miner.Definition)??throw new InvalidOperationException("fixture has no refinery exit");
                miner.X=exit.X*Rules.Scale+Rules.Scale/2;miner.Y=exit.Y*Rules.Scale+Rules.Scale/2;miner.Returning=true;miner.Path.Clear();miner.PathIndex=0;
                var credits=world.Players[0].Credits;Step(world);
                Require(world.Players[0].Credits==credits+expected*5/4 && miner.Cargo==0 && miner.CargoBales==0,
                    $"{type}/gems={gems}: deposit expected credits {credits+expected*5/4}, actual {world.Players[0].Credits}; cargo {miner.Cargo}, bales {miner.CargoBales}, position {miner.CellX},{miner.CellY}, dock {exit.X},{exit.Y}, power {world.Players[0].GeneratedPower}/{world.Players[0].UsedPower}");
                world.Players[0].PowerBlackoutUntil=world.Tick+10;
                Require(world.ClassicOreIncome(0,1000)==1000,"unpowered purifier still added revenue");
            }
        });

        Test("service bay heals one ground vehicle and charges only its owner",()=>
        {
            var world=New();Power(world);var depot=Place(world,BuildingType.ServiceDepot);
            var vehicle=world.SpawnUnit(0,UnitType.Grizzly,depot.CellLeft+depot.Definition.Width,depot.CellTop+1);vehicle.Health-=40;
            var health=vehicle.Health;var money=world.Players[0].Credits;var other=world.Players[1].Credits;
            world.UpdateClassicEconomy();
            Require(vehicle.Health==health+8 && world.Players[0].Credits<money && world.Players[1].Credits==other,"service repair did not heal/charge correctly");
            world.Players[0].PowerBlackoutUntil=world.Tick+10;health=vehicle.Health;money=world.Players[0].Credits;world.UpdateClassicEconomy();
            Require(vehicle.Health==health && world.Players[0].Credits==money,"blacked-out service bay continued repairing");
            world.Players[0].PowerBlackoutUntil=0;vehicle.Health=vehicle.MaxHealth;
            var parasite=world.SpawnUnit(1,UnitType.TerrorDrone,vehicle.CellX+3,vehicle.CellY);parasite.ParasiteHostId=vehicle.Id;vehicle.ParasiteId=parasite.Id;
            money=world.Players[0].Credits;world.UpdateClassicEconomy();
            Require(vehicle.ParasiteId==0 && !parasite.Alive && world.Players[0].Credits==money,"full-health vehicle did not receive free parasite removal");
        });

        Test("four crate rewards apply once and reject aircraft/transport passengers",()=>
        {
            var world=New(crates:true);var collector=world.Units.First(unit=>unit.Owner==0 && unit.Type==UnitType.Grizzly);
            var money=world.Players[0].Credits;var cash=AddCrate(world,collector,ClassicCrateKind.Credits,2000);
            Require(world.TryPickupClassicCrate(cash.Id,collector) && world.Players[0].Credits==money+2000 && !world.TryPickupClassicCrate(cash.Id,collector),"money crate was lost or consumed twice");
            collector.Health-=20;var own=world.Buildings.First(building=>building.Owner==0);own.Health-=20;
            var enemy=world.Buildings.First(building=>building.Owner==1);enemy.Health-=20;var enemyHealth=enemy.Health;
            Require(world.TryPickupClassicCrate(AddCrate(world,collector,ClassicCrateKind.Repair).Id,collector),"repair crate was not collected");
            Require(collector.Health==collector.MaxHealth && own.Health==own.MaxHealth && enemy.Health==enemyHealth,"repair crate affected the wrong assets");
            world.TryPickupClassicCrate(AddCrate(world,collector,ClassicCrateKind.Reveal).Id,collector);
            Require(world.Players[0].CrateMapRevealed && world.Visible(0,70,60) && !world.Players[1].CrateMapRevealed,"reveal crate did not persist for its owner");
            var engineer=world.SpawnUnit(0,UnitType.Engineer,collector.CellX+2,collector.CellY+1);
            world.TryPickupClassicCrate(AddCrate(world,collector,ClassicCrateKind.Veteran).Id,collector);
            Require(collector.Kills>=3 && engineer.Kills==0,"veteran crate failed to promote or promoted untrainable engineers");
            var plane=world.SpawnUnit(0,UnitType.Harrier,collector.CellX+5,collector.CellY+1);
            Require(!world.TryPickupClassicCrate(AddCrate(world,plane,ClassicCrateKind.Credits,1000).Id,plane),"aircraft collected a ground crate");
            collector.TransportId=999;
            Require(!world.TryPickupClassicCrate(AddCrate(world,collector,ClassicCrateKind.Credits,1000).Id,collector),"passenger collected an exterior crate");
        });

        Test("crate generation is deterministic bounded and controlled by settings",()=>
        {
            var first=New(crates:true);var second=New(crates:true);first.UpdateClassicEconomy();second.UpdateClassicEconomy();
            Require(first.ClassicCrates.Count>0 && first.ClassicCrates.SequenceEqual(second.ClassicCrates) && first.ClassicCrateRandomState==second.ClassicCrateRandomState,"same seed generated different crates");
            for(var count=0;count<40;count++){first.NextClassicCrateTick=0;first.UpdateClassicEconomy();}
            Require(first.ClassicCrates.Count<=first.Players.Length*2 && first.ClassicCrates.Count<=16,"crate scheduler exceeded its bound");
            var disabled=New();var actor=disabled.Units[0];var crate=AddCrate(disabled,actor,ClassicCrateKind.Credits,2000);
            Require(!disabled.TryPickupClassicCrate(crate.Id,actor),"disabled crate setting still awarded money");disabled.UpdateClassicEconomy();
            Require(disabled.ClassicCrates.Count==0 && disabled.ClassicCrateRandomState==0,"disabled crate setting spawned random state");
        });

        var report=new{passed=failed==0,totalTests=results.Count,failed,worldSteps=steps,milliseconds=watch.Elapsed.TotalMilliseconds,tests=results};
        var reportArg=args.FirstOrDefault(arg=>arg.StartsWith("--report=",StringComparison.Ordinal));
        if(reportArg is not null)
        {
            var path=Path.GetFullPath(reportArg[9..]);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        }
        Console.WriteLine($"Classic economy: {results.Count-failed}/{results.Count} passed; {steps} world steps.");return failed==0?0:1;
    }
}
