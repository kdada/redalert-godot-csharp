using System.Diagnostics;
using System.Text.Json;
using RedAlert.Simulation;

namespace RedAlert.Tests;

public static class BuildingFootprintChecks
{
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static World New(int count=2)
    {
        var world=new World(MatchSettings.Practice(count));
        foreach(var unit in world.Units)unit.Order=UnitOrder.Idle;
        return world;
    }
    static GameCommand Command(World world,CommandType type,int x,int y,int[]? actors=null)=>
        new(){Tick=world.Tick,Player=0,Sequence=world.Tick,Type=type,Building=BuildingType.Wall,X=x,Y=y,Actors=actors??[]};

    public static int Run(string[] args)
    {
        var results=new List<object>();var failed=0;var steps=0;var watch=Stopwatch.StartNew();
        void Test(string name,Action action)
        {
            try{action();results.Add(new{name,passed=true});Console.WriteLine("PASS "+name);}
            catch(Exception error){failed++;results.Add(new{name,passed=false,error=error.Message});Console.Error.WriteLine($"FAIL {name}: {error.Message}");}
        }
        void Step(World world,params GameCommand[] commands)
        {Require(steps<200,"building checks exceeded 200 world steps");steps++;world.Step(commands);}

        Test("original art foundations and all eight starting bases agree with occupancy",()=>
        {
            // Source: retained art.ini Foundation entries, including faction differences.
            (int W,int H)[] allied=[(4,4),(2,2),(4,3),(3,2),(5,3),(3,2),(3,2),(1,1),(1,1),(2,2),(1,1)];
            (int W,int H)[] soviet=[(4,4),(3,2),(4,3),(2,2),(5,3),(2,2),(3,3),(1,1),(1,1),(2,2),(1,1)];
            foreach(var country in Enum.GetValues<Country>())foreach(var type in Enum.GetValues<BuildingType>())
            {
                var expected=(Rules.Side(country)==Faction.Soviet?soviet:allied)[(int)type];var actual=Rules.Building(type,country);
                Require(actual.Width==expected.W && actual.Height==expected.H,$"{country}/{type}: original foundation mismatch");
            }
            var world=New(8);
            foreach(var building in world.Buildings)
            {
                var d=building.Definition;var cells=world.Map.Buildings.Count(id=>id==building.Id);
                Require(cells==d.Width*d.Height,$"{building.Id}: incomplete or overlapping occupancy");
                Require(building.FoundationCountry==world.Players[building.Owner].Setup.Country,"spawn lost its foundation country");
                Require(building.X==building.CellLeft*Rules.Scale+d.Width*Rules.Scale/2 && building.Y==building.CellTop*Rules.Scale+d.Height*Rules.Scale/2,"sprite/physical foundation center mismatch");
                for(var y=building.CellTop;y<building.CellTop+d.Height;y++)for(var x=building.CellLeft;x<building.CellLeft+d.Width;x++)
                    Require(!world.Map.Walkable(x,y) && world.Map.Ore[world.Map.Index(x,y)]==0,"foundation is traversable or covers ore");
            }
            Require(world.Units.All(unit=>world.CanOccupyUnitPosition(unit,unit.X,unit.Y)),"a starting unit body overlaps a building");
            foreach(var player in world.Players)
            {
                var spawn=world.Map.Spawns[player.Id];var refinery=world.Buildings.Single(b=>b.Owner==player.Id && b.Type==BuildingType.Refinery);
                Require(refinery.CellLeft==spawn.X+3 && refinery.CellTop==spawn.Y+7,"refinery layout did not preserve the clear reserve");
                for(var y=spawn.Y+2;y<=spawn.Y+6;y++)for(var x=spawn.X+6;x<=spawn.X+10;x++)
                    Require(world.Map.Ore[world.Map.Index(x,y)]>0 && world.Visible(player.Id,x,y),"original visible mineral patch was obstructed");
            }
        });

        Test("vehicle body sweep rejects corner penetration and routes around the full factory",()=>
        {
            var world=New();var spawn=world.Map.Spawns[0];var factory=world.SpawnBuilding(0,BuildingType.WarFactory,spawn.X+4,spawn.Y-4);
            var tank=world.Units.First(u=>u.Owner==0 && !u.Definition.Infantry && !u.Definition.Miner);var radius=World.UnitCollisionRadius(tank);
            var left=factory.CellLeft*Rules.Scale;var right=(factory.CellLeft+factory.Definition.Width)*Rules.Scale;
            var middle=(factory.CellTop+1)*Rules.Scale+Rules.Scale/2;
            Require(factory.Definition.Width==5 && world.Map.Buildings[world.Map.Index(factory.CellLeft+4,factory.CellTop+1)]==factory.Id,"factory's formerly missing columns remain walkable");
            Require(!world.CanOccupyUnitPosition(tank,left-radius/2,middle),"vehicle body entered foundation while its center remained outside");
            Require(world.CanOccupyUnitPosition(tank,left-Rules.Scale/2,middle),"legal adjacent vehicle position was blocked by tall artwork");
            Require(world.Map.BuildingBlocksMovement(left-2*Rules.Scale,middle,right+2*Rules.Scale,middle,radius),"swept movement tunneled through a building");
            var top=factory.CellTop*Rules.Scale;
            Require(world.Map.BuildingBlocksMovement(left-radius-100,top-radius+100,left-radius+100,top-radius-100,radius),"corner sweep escaped between two clear endpoints");
            var goalX=spawn.X+10;var goalY=spawn.Y-3;
            var blockedTrace=new List<object>();
            object Snapshot()=>new{world.Tick,tank.X,tank.Y,tank.CellX,tank.CellY,tank.GoalX,tank.GoalY,tank.Order,tank.PathIndex,pathCount=tank.Path.Count,tank.PathVersion,tank.NextPathTick,tank.BlockedTicks,world.Map.NavigationVersion,
                path=tank.Path.Skip(Math.Max(0,tank.PathIndex-1)).Take(5).Select(cell=>new{x=cell%world.Map.Width,y=cell/world.Map.Width}).ToArray()};
            Step(world,Command(world,CommandType.Move,goalX,goalY,[tank.Id]));
            for(var tick=0;tick<140 && (tank.CellX!=goalX || tank.CellY!=goalY || tank.Order!=UnitOrder.Idle);tick++)
            {
                Require(world.CanOccupyUnitPosition(tank,tank.X,tank.Y),"actual vehicle body penetrated an occupied foundation");Step(world);
                if(blockedTrace.Count<16 && tank.BlockedTicks>0 && (tank.BlockedTicks==1 || tank.BlockedTicks%16==0))blockedTrace.Add(Snapshot());
            }
            var diagnostic=JsonSerializer.Serialize(new{final=Snapshot(),blockedTrace,others=world.Units.Where(unit=>unit.Id!=tank.Id && unit.Owner==tank.Owner).Select(unit=>new{unit.Id,unit.X,unit.Y,unit.CellX,unit.CellY,unit.Order})});
            Require(world.CanOccupyUnitPosition(tank,tank.X,tank.Y) && tank.CellX==goalX && tank.CellY==goalY,"vehicle failed to finish a valid route around the factory: "+diagnostic);
            // Replay the captured blocked pose exactly: the other starting tank is
            // still in the eastern neighbor. A new diagonal must first retreat to
            // the current cell center, rather than enter that occupied neighbor.
            tank.X=(spawn.X+2)*Rules.Scale-17;tank.Y=(spawn.Y-2)*Rules.Scale+Rules.Scale/2;
            tank.Order=UnitOrder.Move;tank.GoalX=goalX;tank.GoalY=goalY;tank.Path.Clear();tank.PathIndex=0;tank.PathVersion=-1;tank.NextPathTick=0;tank.BlockedTicks=16;
            var blockedX=tank.X;var blockedY=tank.Y;Step(world);
            Require(tank.X<blockedX && tank.Y==blockedY && tank.BlockedTicks==0 && tank.Path.Count>0
                && tank.Path[0]==world.Map.Index(spawn.X+1,spawn.Y-2),"off-center replan did not safely return to its start-cell center: "+JsonSerializer.Serialize(Snapshot()));
        });

        Test("body-aware placement retains ready production and capture preserves the foundation",()=>
        {
            var world=New();var spawn=world.Map.Spawns[0];var tank=world.Units.First(u=>u.Owner==0 && !u.Definition.Infantry && !u.Definition.Miner);
            var siteX=spawn.X+10;var siteY=spawn.Y;var savedX=tank.X;var savedY=tank.Y;var count=world.EntityCount;var credits=world.Players[0].Credits;
            Step(world,Command(world,CommandType.QueueBuilding,siteX,siteY));
            while(!world.Players[0].BuildingReady)Step(world);
            tank.X=siteX*Rules.Scale-World.UnitCollisionRadius(tank)/2;tank.Y=siteY*Rules.Scale+Rules.Scale/2;
            Require(tank.CellX!=siteX && world.BuildingAreaHasUnits(siteX,siteY,1,1),"test body does not overlap outside its center cell");
            var rejected=false;try{world.SpawnBuilding(0,BuildingType.Wall,siteX,siteY);}catch(ArgumentException){rejected=true;}
            Require(rejected && world.EntityCount==count,"direct spawn embedded an existing vehicle");
            Step(world,Command(world,CommandType.Build,siteX,siteY));
            Require(world.Players[0].BuildingReady && world.Players[0].Credits==credits-Rules.Building(BuildingType.Wall).Cost,"invalid placement lost its ready item or credits");
            tank.X=savedX;tank.Y=savedY;
            Step(world,Command(world,CommandType.Build,siteX,siteY));
            var wall=world.Buildings.Single(b=>b.CellLeft==siteX && b.CellTop==siteY);
            for(var tick=0;tick<19;tick++)Step(world);
            Require(wall.Complete && wall.Health==wall.MaxHealth && world.Players[0].BuildingProduction is null && world.Players[0].Credits==credits-Rules.Building(BuildingType.Wall).Cost,"20-tick erection or prepaid queue behavior changed");
            var power=world.Buildings.Single(b=>b.Owner==1 && b.Type==BuildingType.PowerPlant);power.Owner=0;
            Require(power.FoundationCountry==Country.Russia && power.Definition.Width==3 && world.Map.Buildings.Count(id=>id==power.Id)==6,"capturing resized the Soviet foundation");
            var hash=world.StateHash();power.FoundationCountry=Country.America;
            Require(world.StateHash()!=hash,"foundation country was omitted from authoritative hash");
        });

        var report=new{passed=failed==0,totalTests=results.Count,failed,worldSteps=steps,milliseconds=watch.Elapsed.TotalMilliseconds,content=World.ContentFingerprint(),tests=results};
        var reportArg=args.FirstOrDefault(arg=>arg.StartsWith("--report=",StringComparison.Ordinal));
        if(reportArg is not null)
        {
            var path=Path.GetFullPath(reportArg[9..]);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        }
        Console.WriteLine($"Building footprints: {results.Count-failed}/{results.Count} passed; {steps} world steps.");return failed==0?0:1;
    }
}
