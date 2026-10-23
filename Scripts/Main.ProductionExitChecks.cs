using Godot;
using RedAlert.Simulation;

namespace RedAlert;

public partial class Main
{
    // Isolated bounded fixtures, invoked explicitly by the Lead. Does not touch
    // Battle, run a native scenario, render, or alter the ordinary match driver.
    public Godot.Collections.Dictionary RunProductionExitChecks()
    {
        var checks=new Godot.Collections.Array<string>();var errors=new Godot.Collections.Array<string>();var steps=0;
        void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        void Test(string name,Action action){try{action();checks.Add(name);}catch(Exception error){errors.Add(name+": "+error.Message);}}
        World Fixture()
        {
            var world=new World(MatchSettings.LegacyFixture(2));
            foreach(var unit in world.Units){unit.DisabledUntil=int.MaxValue;unit.Order=UnitOrder.Idle;unit.TargetId=0;}
            for(var y=30;y<=65;y++)for(var x=30;x<=65;x++)
            {
                var i=world.Map.Index(x,y);world.Map.Ground[i]=Terrain.Grass;world.Map.Elevation[i]=0;
                world.Map.Bridges[i]=false;world.Map.Ramps[i]=RampDirection.None;world.Map.Ore[i]=0;
            }
            return world;
        }
        void Step(World world,int count=1)
        {
            for(var i=0;i<count;i++){if(++steps>40)throw new InvalidOperationException("Production exit check exceeded 40-step cap");world.Step();}
        }
        UnitDef Queue(World world,Building factory,UnitType type)
        {
            var definition=Rules.Unit(type,factory.FoundationCountry);
            world.Players[factory.Owner].Credits-=definition.Cost;factory.Queue.Add(type);
            factory.ProductionProgress=definition.BuildTicks;factory.ProductionPaid=definition.Cost;
            return definition;
        }
        (World World,Building Barracks,Building Vat,Cell Mouth) Soviet()
        {
            var world=Fixture();var barracks=world.SpawnBuilding(1,BuildingType.Barracks,40,40);
            var vat=world.SpawnBuilding(1,BuildingType.CloningVats,50,40);world.SpawnBuilding(1,BuildingType.NuclearReactor,55,40);
            return (world,barracks,vat,world.ClassicProductionExitPortals(barracks).Single());
        }
        Test("blocked vehicle waits; opening the actual door releases one product",()=>
        {
            var world=Fixture();var factory=world.SpawnBuilding(0,BuildingType.WarFactory,40,40);
            var definition=Queue(world,factory,UnitType.Grizzly);var mouth=world.ClassicProductionExitPortals(factory).Single();
            var wall=world.SpawnBuilding(0,BuildingType.Wall,mouth.X,mouth.Y);var credits=world.Players[0].Credits;
            var old=world.Units.Max(unit=>unit.Id);
            Require(world.ClassicProductionExit(factory,definition).Block==ProductionExitBlock.Structure,"Wall was not classified as a structural block");
            Step(world);
            Require(factory.Queue.Count==1 && factory.ProductionProgress==definition.BuildTicks && world.Players[0].Credits==credits,"Blocked vehicle was canceled/refunded/reset");
            Require(!world.Units.Any(unit=>unit.Id>old),"Product teleported across a blocked factory door");
            wall.Health=0;Step(world,2);
            var product=world.Units.Single(unit=>unit.Id>old && unit.Type==UnitType.Grizzly);
            Require(factory.Queue.Count==0 && product.CellX==mouth.X && product.CellY==mouth.Y,"Unblocked vehicle did not emerge at the legal portal");
        });
        Test("Soviet barracks structural block refunds once without original or clone",()=>
        {
            var (world,barracks,vat,mouth)=Soviet();var before=world.Players[1].Credits;
            Queue(world,barracks,UnitType.Conscript);world.SpawnBuilding(1,BuildingType.Wall,mouth.X,mouth.Y);var old=world.Units.Max(unit=>unit.Id);
            Step(world);
            Require(barracks.Queue.Count==0 && barracks.ProductionProgress==0 && world.Players[1].Credits==before,"Official blocked NAHAND refund was not exact");
            Require(!world.Units.Any(unit=>unit.Id>old) && vat.PendingClones.Count==0,"Blocked Soviet barracks created an original or clone");
            Step(world);Require(world.Players[1].Credits==before,"Completed blocked job refunded more than once");
        });
        Test("unit congestion waits without refund or cloning; clearing it produces once",()=>
        {
            var (world,barracks,vat,mouth)=Soviet();var blocker=world.SpawnUnit(1,UnitType.Conscript,mouth.X,mouth.Y);blocker.DisabledUntil=int.MaxValue;
            var definition=Queue(world,barracks,UnitType.Conscript);var credits=world.Players[1].Credits;var old=world.Units.Max(unit=>unit.Id);
            Require(world.ClassicProductionExit(barracks,definition).Block==ProductionExitBlock.UnitCrowding,"Unit crowding was not distinguished from a wall");
            Step(world,2);
            Require(barracks.Queue.Count==1 && world.Players[1].Credits==credits && vat.PendingClones.Count==0,"Crowded door refunded or cloned infantry");
            blocker.Health=0;Step(world);
            Require(barracks.Queue.Count==0 && world.Units.Count(unit=>unit.Id>old)==1 && vat.PendingClones.Count==1,"Cleared barracks did not produce exactly one original and one pending clone");
        });
        Test("cliff cannot be crossed; a neighboring wall does not block the actual mouth",()=>
        {
            var world=Fixture();var factory=world.SpawnBuilding(0,BuildingType.WarFactory,40,40);
            var definition=Queue(world,factory,UnitType.Grizzly);var mouth=world.ClassicProductionExitPortals(factory).Single();var i=world.Map.Index(mouth.X,mouth.Y);
            world.Map.Elevation[i]=1;var old=world.Units.Max(unit=>unit.Id);
            Require(world.ClassicProductionExit(factory,definition).Block==ProductionExitBlock.Terrain,"Cliff exit was accepted");
            Step(world);Require(factory.Queue.Count==1 && !world.Units.Any(unit=>unit.Id>old),"Product crossed the factory cliff");
            world.Map.Elevation[i]=0;world.SpawnBuilding(0,BuildingType.Wall,mouth.X,mouth.Y+1);
            Require(world.ClassicProductionExit(factory,definition).Cell==mouth,"Neighbor wall incorrectly closed the clear portal");
            Step(world);Require(world.Units.Any(unit=>unit.Id>old && unit.CellX==mouth.X && unit.CellY==mouth.Y),"Clear neighboring portal did not release production");
        });
        Test("captured factory preserves foundation-country product and source door precision",()=>
        {
            var world=Fixture();var factory=world.SpawnBuilding(0,BuildingType.WarFactory,40,40,foundationCountry:Country.Russia);
            Queue(world,factory,UnitType.Rhino);var old=world.Units.Max(unit=>unit.Id);var door=world.ClassicProductionDoorPosition(factory);
            Require(door.X-factory.X==384*Rules.Scale/256 && door.Y-factory.Y==96*Rules.Scale/256,"Original ExitCoord lost its lepton precision");
            Step(world);var product=world.Units.Single(unit=>unit.Id>old && unit.Type==UnitType.Rhino);
            Require(product.Owner==0 && product.OriginCountry==Country.Russia && factory.FoundationCountry==Country.Russia,"Captured factory product lost its source country");
        });
        return new(){{"passed",errors.Count==0 && checks.Count==5},{"checks",checks},{"errors",errors},{"worldSteps",steps},{"worldStepCap",40},
            {"fixtureOnly",true},{"ordinaryBattleModified",false},{"doorInteriorAnimationImplemented",false}};
    }
}
