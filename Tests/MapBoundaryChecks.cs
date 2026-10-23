using System.Diagnostics;
using System.Text.Json;
using RedAlert.Simulation;

namespace RedAlert.Tests;

public static class MapBoundaryChecks
{
    static void Require(bool value,string message)
    {
        if(!value)throw new InvalidOperationException(message);
    }

    static GameCommand Command(World world,CommandType type,int x=0,int y=0,int[]? actors=null,int sequence=0)
        =>new(){Tick=world.Tick,Player=0,Sequence=sequence,Type=type,Actors=actors??[],Building=BuildingType.Wall,X=x,Y=y};

    public static int Run(string[] args)
    {
        var watch=Stopwatch.StartNew();var results=new List<object>();var failed=0;var steps=0;
        void Test(string name,Action action)
        {
            try{action();results.Add(new{name,passed=true});Console.WriteLine($"PASS {name}");}
            catch(Exception error){failed++;results.Add(new{name,passed=false,error=error.Message});Console.Error.WriteLine($"FAIL {name}: {error.Message}");}
        }
        void Step(World world,params GameCommand[] commands)
        {Require(steps<150,"boundary checks exceeded 150 world steps");steps++;world.Step(commands);}

        Test("projected rectangle leaves decorative terrain outside all authoritative fog and movement",()=>
        {
            var world=new World(MatchSettings.Practice(8));var map=world.Map;
            Require(map.Width==96 && map.Height==96 && map.Ground.Length==96*96,"map storage dimensions changed");
            var outside=0;var playable=0;
            for(var y=0;y<map.Height;y++)for(var x=0;x<map.Width;x++)
            {
                var inside=map.IsPlayable(x,y);
                Require(inside==map.IsPlayablePoint(x+.5,y+.5),"cell-center/continuous boundary mismatch");
                if(inside){playable++;continue;}
                outside++;var index=map.Index(x,y);
                Require(!map.Walkable(x,y) && map.Ore[index]==0 && map.Buildings[index]==0,"exterior received gameplay data");
                foreach(var player in world.Players)
                    Require(player.Visibility[index]==0 && !world.Visible(player.Id,x,y) && !world.Explored(player.Id,x,y),"exterior received visibility");
            }
            Require(outside>1000 && playable>3500,"decorative padding or playable interior was lost");
            Require(map.IsPlayablePoint(2,46) && map.IsPlayablePoint(46,2) && map.IsPlayablePoint(94,50) && map.IsPlayablePoint(50,94),"continuous rectangle corners changed");
            Require(!map.IsPlayablePoint(1.99,46) && !map.IsPlayablePoint(94.01,50) && !map.IsPlayablePoint(double.NaN,48),"continuous exterior accepted");
            Require(map.IsPlayable(2,46) && !map.FootprintFree(2,46,1,1),"whole building footprint crossed the boundary");
            world.Players[0].Visibility[map.Index(0,0)]=3;
            Require(!world.Visible(0,0,0) && !world.Explored(0,0,0),"stale exterior visibility bypassed the domain guard");
            Step(world);Require(world.Players[0].Visibility[0]==0,"fog refresh did not clear exterior bits");
            foreach(var player in world.Players)
            {
                var spawn=map.Spawns[player.Id];
                for(var y=spawn.Y+2;y<=spawn.Y+6;y++)for(var x=spawn.X+6;x<=spawn.X+10;x++)
                    Require(map.IsPlayable(x,y) && map.Ore[map.Index(x,y)]>0 && world.Visible(player.Id,x,y),$"start {player.Id}: missing or hidden ore");
                var miner=world.Units.Single(unit=>unit.Owner==player.Id && unit.Definition.Miner);
                Require(map.Ore[map.Index(miner.CellX,miner.CellY)]>0 && miner.Order==UnitOrder.Harvest,"miner does not start on accessible ore");
            }
            for(var i=0;i<5;i++)Step(world);
            Require(world.Units.Where(unit=>unit.Definition.Miner).All(unit=>unit.Cargo>=50),"a starting miner did not harvest within six ticks");
            Require(world.Units.All(unit=>map.IsPlayablePoint(unit.X/(double)Rules.Scale,unit.Y/(double)Rules.Scale)),"unit escaped the playable rectangle");
        });

        Test("outside orders reject atomically and retain a ready building",()=>
        {
            var world=new World(MatchSettings.Practice(2));var map=world.Map;var spawn=map.Spawns[0];var player=world.Players[0];
            var unit=world.Units.First(u=>u.Owner==0 && !u.Definition.Miner);var credits=player.Credits;var count=world.EntityCount;
            Step(world,Command(world,CommandType.Move,actors:[unit.Id]),Command(world,CommandType.Build,sequence:1),Command(world,CommandType.Support,sequence:2));
            Require(world.RejectedCommands==3 && player.Credits==credits && world.EntityCount==count && unit.Order==UnitOrder.Idle,"outside command mutated the world");
            var paths=new GridPathfinder(map);
            Require(paths.Find(0,0,spawn.X+7,spawn.Y+4).Count==0 && paths.Find(spawn.X+7,spawn.Y+4,0,0).Count==0,"path accepted an exterior endpoint");
            Step(world,Command(world,CommandType.QueueBuilding));
            while(!player.BuildingReady)Step(world);
            var prepaid=player.Credits;
            Step(world,Command(world,CommandType.Build));
            Require(player.BuildingReady && player.Credits==prepaid && world.EntityCount==count,"invalid exterior placement consumed a ready building");
            Require(world.CanPlaceBuilding(0,BuildingType.Wall,spawn.X+4,spawn.Y-4),"spawn-relative construction pad was invalid");
            Step(world,Command(world,CommandType.Build,spawn.X+4,spawn.Y-4));
            Require(player.BuildingProduction is null && player.Credits==prepaid && world.EntityCount==count+1,"valid placement did not preserve the prepaid contract");
        });

        Test("short boundary command stream replays the new domain and landscape hash",()=>
        {
            var settings=MatchSettings.Practice(2);var a=new World(settings);var b=new World(settings);var stream=new List<GameCommand>();
            const int ticks=20;
            for(var tick=0;tick<ticks;tick++)
            {
                var commands=new List<GameCommand>();var spawn=a.Map.Spawns[0];
                if(tick==0)
                {
                    commands.Add(Command(a,CommandType.Move,actors:[a.Units.First(u=>u.Owner==0).Id]));
                    commands.Add(Command(a,CommandType.QueueBuilding,sequence:1));
                }
                if(tick==1 || tick==15)commands.Add(Command(a,CommandType.Build));
                if(tick==16)commands.Add(Command(a,CommandType.Build,spawn.X+4,spawn.Y-4));
                stream.AddRange(commands);Step(a,commands.ToArray());Step(b,commands.AsEnumerable().Reverse().ToArray());
                Require(a.StateHash()==b.StateHash(),"boundary stream diverged at tick "+tick);
            }
            Require(a.AcceptedCommands==2 && a.RejectedCommands==3,"boundary stream missed its intended cases");
            var replay=new ReplayFile{Settings=settings,Commands=stream,FinalTick=a.Tick,FinalHash=a.StateHash().ToString("x16")};
            var json=JsonSerializer.Serialize(replay,ReplayFile.JsonOptions);
            var restored=JsonSerializer.Deserialize<ReplayFile>(json,ReplayFile.JsonOptions)??throw new InvalidOperationException("empty replay");
            Require(steps+ticks<=150,"replay exceeded the narrow check budget");steps+=ticks;
            Require(restored.Play().StateHash()==a.StateHash(),"new bounds/elevation/bridge replay diverged");
        });

        var report=new{passed=failed==0,totalTests=results.Count,failed,worldSteps=steps,milliseconds=watch.Elapsed.TotalMilliseconds,content=World.ContentFingerprint(),tests=results};
        var reportArg=args.FirstOrDefault(arg=>arg.StartsWith("--report=",StringComparison.Ordinal));
        if(reportArg is not null)
        {
            var path=Path.GetFullPath(reportArg[9..]);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        }
        Console.WriteLine($"Map boundary: {results.Count-failed}/{results.Count} passed; {steps} world steps.");
        return failed==0?0:1;
    }
}
