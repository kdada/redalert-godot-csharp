using System.Diagnostics;
using System.Text.Json;
using RedAlert.Simulation;

namespace RedAlert.Tests;

public static class ExperienceSimulationChecks
{
    static void Require(bool condition,string message)
    {
        if(!condition)throw new InvalidOperationException(message);
    }

    static GameCommand Command(World world,int player,CommandType type,BuildingType building=BuildingType.PowerPlant,int x=17,int y=9)
        =>new(){Tick=world.Tick,Player=player,Sequence=world.Tick*100+player,Type=type,Building=building,X=x,Y=y};

    public static int Run(string[] args)
    {
        var results=new List<object>();var failed=0;var worldSteps=0;var total=Stopwatch.StartNew();
        void Test(string name,Action action)
        {
            var watch=Stopwatch.StartNew();
            try{action();results.Add(new{name,passed=true,milliseconds=watch.Elapsed.TotalMilliseconds});Console.WriteLine($"PASS {name}");}
            catch(Exception error){failed++;results.Add(new{name,passed=false,error=error.Message,milliseconds=watch.Elapsed.TotalMilliseconds});Console.Error.WriteLine($"FAIL {name}: {error.Message}");}
        }
        World New(int players=2)
        {
            var world=new World(MatchSettings.Practice(players));
            // Isolate economy assertions from mining income and unnecessary path searches.
            foreach(var unit in world.Units)unit.Order=UnitOrder.Idle;
            return world;
        }
        void Step(World world,params GameCommand[] commands)
        {
            Require(worldSteps<1000,"targeted runner exceeded its 1000 world-step budget");
            worldSteps++;world.Step(commands);
        }
        void Advance(World world,int ticks){for(var i=0;i<ticks;i++)Step(world);}
        void Prepare(World world,int player)
        {
            var remaining=Rules.Building(world.Players[player].BuildingProduction!.Value).BuildTicks-world.Players[player].BuildingProductionProgress;
            Advance(world,remaining);
            Require(world.Players[player].BuildingReady,"preparation did not become ready after its required ticks");
        }
        void AssertGone(World world,int player,int[] ids)
        {
            var p=world.Players[player];
            Require(p.Defeated && p.BuildingProduction is null && p.BuildingProductionProgress==0,"defeat retained production state");
            Require(!world.Units.Any(u=>u.Owner==player) && !world.Buildings.Any(b=>b.Owner==player),"defeated assets remained in world lists");
            Require(ids.All(id=>world.Find(id) is null),"defeated asset remained addressable");
            Require(!world.Map.Buildings.Any(id=>ids.Contains(id)),"defeated footprint remained blocked");
        }

        Test("prepay, prepare, retain invalid placement, and erect in 20 ticks",()=>
        {
            var world=New();var p=world.Players[0];var initialCredits=p.Credits;var entities=world.EntityCount;var cost=Rules.Building(BuildingType.PowerPlant).Cost;
            Step(world,Command(world,0,CommandType.QueueBuilding));
            Require(world.AcceptedCommands==1 && p.Credits==initialCredits-cost,"queue did not prepay exactly once");
            Require(p.BuildingProduction==BuildingType.PowerPlant && p.BuildingProductionProgress==1 && !p.BuildingReady,"queue preparation state");
            Require(world.EntityCount==entities,"preparation created a map entity");
            Step(world,Command(world,0,CommandType.Build));
            Require(world.RejectedCommands==1 && world.EntityCount==entities && p.Credits==initialCredits-cost,"not-ready placement mutated entities or credits");
            Prepare(world,0);
            Require(world.EntityCount==entities,"readiness created a map entity");
            var progress=p.BuildingProductionProgress;var spawn=world.Map.Spawns[0];
            Step(world,Command(world,0,CommandType.Build,x:spawn.X,y:spawn.Y));
            Require(world.RejectedCommands==2 && p.BuildingReady && p.BuildingProductionProgress==progress && world.EntityCount==entities && p.Credits==initialCredits-cost,"invalid placement consumed the ready item");
            Require(world.CanPlaceBuilding(0,BuildingType.PowerPlant,17,9),"valid test footprint was rejected");
            Step(world,Command(world,0,CommandType.Build));
            var placed=world.Buildings.Single(b=>b.Owner==0 && b.CellLeft==17 && b.CellTop==9);
            Require(world.AcceptedCommands==2 && p.Credits==initialCredits-cost,"placement charged a second time");
            Require(p.BuildingProduction is null && p.BuildingProductionProgress==0,"placement retained its prepared job");
            Require(placed.BuildDurationTicks==20 && placed.TotalBuildTicks==20 && placed.RemainingBuildTicks==19 && !placed.Complete,"placement did not start its 20-tick erection");
            Advance(world,18);Require(!placed.Complete && placed.RemainingBuildTicks==1,"erection completed early");
            Step(world);Require(placed.Complete && placed.Health==placed.MaxHealth && placed.ConstructionProgress==1f,"erection duration or full-health growth was wrong");
        });

        Test("cancellation refunds full prepaid cost before and after readiness",()=>
        {
            var world=New();var p=world.Players[0];var initial=p.Credits;var entities=world.EntityCount;
            Step(world,Command(world,0,CommandType.QueueBuilding));
            Step(world,Command(world,0,CommandType.CancelBuilding));
            Require(p.Credits==initial && p.BuildingProduction is null && p.BuildingProductionProgress==0,"in-progress cancellation did not fully refund and clear");
            Step(world,Command(world,0,CommandType.QueueBuilding));Prepare(world,0);
            Step(world,Command(world,0,CommandType.CancelBuilding));
            Require(p.Credits==initial && p.BuildingProduction is null && p.BuildingProductionProgress==0 && world.EntityCount==entities,"ready cancellation did not fully refund and clear");
        });

        Test("low power prepares buildings at half rate without another job",()=>
        {
            var world=New();world.Buildings.Single(b=>b.Owner==0 && b.Type==BuildingType.PowerPlant).Health=0;
            Step(world,Command(world,0,CommandType.QueueBuilding));Advance(world,19);
            Require(!world.Players[0].Powered && world.Players[0].BuildingProductionProgress==10,"low-power preparation did not advance on exactly half of ticks");
            Require(!world.CanQueueBuilding(0,BuildingType.Barracks),"a second building job was allowed");
        });

        Test("surrender clears prepared building and every asset in the same step",()=>
        {
            var world=New();var ids=world.Buildings.Cast<Entity>().Concat(world.Units).Where(e=>e.Owner==1).Select(e=>e.Id).ToArray();
            Step(world,Command(world,1,CommandType.QueueBuilding));
            Require(world.Players[1].BuildingProduction is not null,"surrender test did not queue production");
            Step(world,Command(world,1,CommandType.Surrender));AssertGone(world,1,ids);
            Require(world.Finished && world.WinningTeam==0,"surrender did not resolve the remaining team");
        });

        Test("walls count as surviving buildings; losing the final wall clears all assets",()=>
        {
            var world=New();var spawn=world.Map.Spawns[1];var wall=world.SpawnBuilding(1,BuildingType.Wall,spawn.X+4,spawn.Y-4);
            var ids=world.Buildings.Cast<Entity>().Concat(world.Units).Where(e=>e.Owner==1).Select(e=>e.Id).ToArray();
            Step(world,Command(world,1,CommandType.QueueBuilding));
            foreach(var building in world.Buildings.Where(b=>b.Owner==1 && b.Id!=wall.Id))building.Health=0;
            Step(world);
            Require(!world.Players[1].Defeated && !world.Finished && world.Find(wall.Id) is not null,"the last surviving wall was ignored");
            Require(world.Units.Any(u=>u.Owner==1),"units were removed while their wall survived");
            wall.Health=0;Step(world);AssertGone(world,1,ids);
            Require(world.Finished && world.WinningTeam==0,"last-building loss did not resolve victory immediately");
        });

        Test("three defeated teams leave two active teams; the final team then wins",()=>
        {
            var world=New(5);
            Step(world,Command(world,0,CommandType.Surrender),Command(world,1,CommandType.Surrender),Command(world,2,CommandType.Surrender));
            Require(world.Players.Count(p=>p.Defeated)==3 && !world.Finished && world.WinningTeam==-1,"match ended with two active teams");
            Require(world.Players.Where(p=>!p.Defeated).Select(p=>p.Setup.Team).Distinct().Count()==2,"test did not leave exactly two teams");
            Step(world,Command(world,3,CommandType.Surrender));
            Require(world.Finished && world.WinningTeam==4 && !world.Players[4].Defeated,"last remaining team was not declared winner");
        });

        Test("short deterministic building command stream and serialized replay",()=>
        {
            var settings=MatchSettings.Practice(2);var a=new World(settings);var b=new World(settings);var stream=new List<GameCommand>();
            const int ticks=40;
            for(var tick=0;tick<ticks;tick++)
            {
                var commands=new List<GameCommand>();
                if(tick==0)
                {
                    for(var player=0;player<2;player++)commands.Add(new GameCommand{Tick=tick,Player=player,Sequence=0,Type=CommandType.Stop,Actors=a.Units.Where(u=>u.Owner==player).Select(u=>u.Id).ToArray()});
                    commands.Add(new GameCommand{Tick=tick,Player=0,Sequence=1,Type=CommandType.QueueBuilding,Building=BuildingType.Wall});
                }
                if(tick is 1 or 15 or 16)commands.Add(Command(a,0,CommandType.Build,BuildingType.Wall,x:tick==15?13:17,y:tick==15?13:9));
                if(tick is 17 or 19)commands.Add(Command(a,0,CommandType.QueueBuilding,BuildingType.Wall));
                if(tick is 18 or 34)commands.Add(Command(a,0,CommandType.CancelBuilding,BuildingType.Wall));
                stream.AddRange(commands);Step(a,commands.ToArray());Step(b,commands.AsEnumerable().Reverse().ToArray());
                Require(a.StateHash()==b.StateHash(),"command-order determinism failed at tick "+tick);
            }
            Require(a.RejectedCommands==2 && a.AcceptedCommands==8,"short stream did not exercise all intended building phases");
            Require(a.Buildings.Any(building=>building.Owner==0 && building.Type==BuildingType.Wall && building.Complete),"replay stream did not complete placement");
            var replay=new ReplayFile{Settings=settings,Commands=stream,FinalTick=a.Tick,FinalHash=a.StateHash().ToString("x16")};
            var json=JsonSerializer.Serialize(replay,ReplayFile.JsonOptions);
            var loaded=JsonSerializer.Deserialize<ReplayFile>(json,ReplayFile.JsonOptions)??throw new InvalidOperationException("replay JSON was empty");
            Require(loaded.Protocol==Rules.Protocol && loaded.Content==World.ContentFingerprint(),"replay content contract changed in serialization");
            Require(worldSteps+ticks<=1000,"replay exceeded targeted world-step budget");worldSteps+=ticks;
            Require(loaded.Play().StateHash()==a.StateHash(),"serialized building replay diverged");
        });

        var report=new{passed=failed==0,totalTests=results.Count,passedTests=results.Count-failed,failed,worldSteps,milliseconds=total.Elapsed.TotalMilliseconds,content=World.ContentFingerprint(),tests=results};
        var reportArg=args.FirstOrDefault(arg=>arg.StartsWith("--report=",StringComparison.Ordinal));
        if(reportArg is not null)
        {
            var path=Path.GetFullPath(reportArg[9..]);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        }
        Console.WriteLine($"{results.Count-failed} passed; {failed} failed; {worldSteps} world steps");
        return failed==0?0:1;
    }
}
