using System.Text.Json;
using RedAlert.Simulation;

namespace RedAlert.Tests;

// Lead owns Program registration/build/run. Exactly one World.Step per official map;
// no AI, auto-clear, relocated deployment attempt, long economy run or mirrored mask test.
public static class ClassicMapObjectChecks
{
    static readonly (string Id, int MaxPlayers)[] cases =
        [("mp01t4",4),("mp02t2",2),("mp08t2",2),("mp23t4",4),("mp25t6",6)];
    static void Require(bool condition, string failure) { if (!condition) throw new InvalidOperationException(failure); }

    public static int Run(string[] args)
    {
        var root = SourceRoot(args); var results = new List<object>(); var failed = 0; var steps = 0; var expanded = 0;
        foreach (var test in cases)
        {
            object? before = null;
            try
            {
                var original = ClassicMapDefinition.Parse(File.ReadAllText(Path.Combine(root,"Assets","Generated","Maps",test.Id+".json")));
                Require(original.Spawns.Length == test.MaxPlayers, $"{test.Id}: expected {test.MaxPlayers} source starts");
                ClassicMapRepository.Register(original);
                var settings = MatchSettings.Practice(test.MaxPlayers,20261022) with
                    {MapId=test.Id,StartingUnits=0,ClassicStart=true,Crates=false};
                var world = new World(settings);
                var mcvs = world.Units.Where(unit=>unit.Alive && unit.Type==UnitType.Mcv).OrderBy(unit=>unit.Owner).ToArray();
                before = mcvs.Select(unit=>Describe(world,unit)).ToArray();
                Require(mcvs.Length == test.MaxPlayers && world.Buildings.Count==0,
                    $"{test.Id}: classic zero-unit start must contain {test.MaxPlayers} MCVs and no bases");
                var commands = mcvs.Select(unit=>new GameCommand
                    {Tick=0,Player=unit.Owner,Sequence=unit.Owner,Type=CommandType.Deploy,Actors=[unit.Id]}).ToArray();
                Require(steps<5,"official-deploy check exceeded its five-step budget");
                steps++; world.Step(commands);
                var yards = world.Buildings.Where(building=>building.Alive && building.Type==BuildingType.ConstructionYard).ToArray();
                var count = yards.Select(building=>building.Owner).Distinct().Count(); expanded += count;
                Require(world.AcceptedCommands==test.MaxPlayers && world.RejectedCommands==0 && count==test.MaxPlayers
                    && mcvs.All(unit=>!unit.Alive),
                    $"{test.Id}: accepted={world.AcceptedCommands}, rejected={world.RejectedCommands}, yards={count}/{test.MaxPlayers}; see before diagnostics");
                Require(world.Map.UnmappedOriginalObjects==0 && world.Map.OriginalObjects.Count==original.Objects.Length,
                    $"{test.Id}: incomplete original-object decoding");
                results.Add(new{id=test.Id,passed=true,accepted=world.AcceptedCommands,rejected=world.RejectedCommands,
                    expanded=count,objects=world.Map.OriginalObjects.Count,before});
                Console.WriteLine($"PASS {test.Id}: {count}/{test.MaxPlayers} official MCVs deploy at Tick0");
            }
            catch (Exception error)
            {
                failed++; results.Add(new{id=test.Id,passed=false,error=error.Message,before});
                Console.Error.WriteLine($"FAIL {test.Id}: {error.Message}");
            }
        }
        if (steps!=5 || expanded!=18) failed++;
        var json=JsonSerializer.Serialize(new{checks="official-map-zero-army-deploy",passed=failed==0,
            worldSteps=steps,stepCap=5,expanded,expectedExpanded=18,results},new JsonSerializerOptions{WriteIndented=true});
        Console.WriteLine(json);var report=args.FirstOrDefault(value=>value.StartsWith("--report=",StringComparison.Ordinal));
        if(report is not null)File.WriteAllText(report[9..],json);
        return failed==0?0:1;
    }

    static object Describe(World world, Unit unit)
    {
        var map=world.Map; var definition=Rules.Building(BuildingType.ConstructionYard,unit.OriginCountry);
        var left=unit.CellX-(definition.Width-1)/2; var top=unit.CellY-(definition.Height-1)/2;
        var blocked=new List<object>(); var level=map.Inside(left,top)?map.Elevation[map.Index(left,top)]:-1;
        for(var y=top;y<top+definition.Height;y++)for(var x=left;x<left+definition.Width;x++)
        {
            if(!map.Inside(x,y)){blocked.Add(new{x,y,reason="outside source array"});continue;}
            var index=map.Index(x,y);var original=map.OriginalCellAt(x,y);var reasons=new List<string>();
            if(!map.IsPlayable(x,y))reasons.Add("outside playable source cells");
            if(original?.LandPassable!=true)reasons.Add("source land not passable");
            if(original?.Buildable!=true)reasons.Add("source tile not buildable");
            if(map.Ore[index]!=0)reasons.Add("original mineral");
            if(map.Bridges[index])reasons.Add("original bridge");
            if(map.IsRamp(x,y))reasons.Add("original ramp");
            if(map.Elevation[index]!=level)reasons.Add("source elevation differs");
            if(map.Buildings[index]!=0)reasons.Add("dynamic building");
            if(map.OriginalObjectBlocksPlacement(x,y))reasons.Add("original object foundation");
            if(reasons.Count==0)continue;
            var objects=map.OriginalObjects.Where(placement=>placement.Definition.BlocksPlacement && x>=placement.X && y>=placement.Y
                && x<placement.X+placement.Width && y<placement.Y+placement.Height)
                .Select(placement=>new{placement.Name,placement.X,placement.Y,placement.Width,placement.Height,placement.Source.Key}).ToArray();
            blocked.Add(new{x,y,sourceX=original?.SourceX,sourceY=original?.SourceY,tileIndex=original?.TileIndex,
                terrainType=original?.TerrainType,rampType=original?.RampType,ore=map.Ore[index],reasons,objects});
        }
        return new{player=unit.Owner,country=unit.OriginCountry.ToString(),officialSpawn=map.Spawns[unit.Owner],
            actualMcv=new{x=unit.CellX,y=unit.CellY},foundation=new{left,top,definition.Width,definition.Height},
            footprintLegal=map.FootprintFree(definition,left,top),blocked};
    }

    static string SourceRoot(string[] args)
    {
        var at=Array.IndexOf(args,"--source-root");if(at>=0 && at+1<args.Length)return Path.GetFullPath(args[at+1]);
        foreach(var start in new[]{Directory.GetCurrentDirectory(),AppContext.BaseDirectory})
            for(DirectoryInfo? directory=new(start);directory is not null;directory=directory.Parent)
                if(File.Exists(Path.Combine(directory.FullName,"Assets","Generated","Maps","catalog.json")))return directory.FullName;
        throw new DirectoryNotFoundException("Use --source-root to select the original map data repository");
    }
}
