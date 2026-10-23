using System.Text.Json;
using RedAlert;
using RedAlert.Simulation;

static class PresentationAudit
{
    public static void Run(Action<bool,string> check)
    {
        var directory=new DirectoryInfo(Directory.GetCurrentDirectory());
        while(directory is not null && !File.Exists(Path.Combine(directory.FullName,"Assets/Generated/Terrain/layout.json")))directory=directory.Parent;
        if(directory is null)throw new InvalidOperationException("Run tests inside the project checkout to verify original terrain data.");
        using var data=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.FullName,"Assets/Generated/Terrain/layout.json")));
        var lat=data.RootElement.GetProperty("latByMask").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
        var shores=data.RootElement.GetProperty("shoreByMask").EnumerateArray().Select(x=>new ClassicTerrainPlan.Shore(x.GetProperty("template").GetString()!,x.GetProperty("frames").EnumerateArray().Select(n=>n.GetInt32()).ToArray())).ToArray();
        foreach(var seed in new uint[]{0,7,12345,0xDEADBEEF})
        {
            var world=new World(MatchSettings.Practice(8,seed));var map=world.Map;
            var before=world.StateHash();var ground=map.Ground.ToArray();var ore=map.Ore.ToArray();var occupancy=map.Buildings.ToArray();var navigation=map.NavigationVersion;
            var first=new ClassicTerrainPlan(map,seed,lat,shores);var second=new ClassicTerrainPlan(map,seed,lat,shores);
            check(world.StateHash()==before && map.Ground.SequenceEqual(ground) && map.Ore.SequenceEqual(ore) && map.Buildings.SequenceEqual(occupancy) && map.NavigationVersion==navigation,"presentation terrain mutated simulation/navigation");
            check(first.TransitionCells>0 && first.ShoreCells>0 && first.SandCells>0,"transitions, shoreline or sand collar missing");
            AuditCoast(first,map,data.RootElement,check);
            if(seed==0)
            {
                var broken=shores.ToArray();broken[12]=new ClassicTerrainPlan.Shore("shore42",[30,31,39,40]);
                var rejected=false;
                try{AuditCoast(new ClassicTerrainPlan(map,seed,lat,broken),map,data.RootElement,(valid,message)=>{if(!valid)throw new InvalidDataException(message);});}
                catch(InvalidDataException error){rejected=error.Message.StartsWith("disconnected coast at",StringComparison.Ordinal);}
                check(rejected,"original broken shoreline passed the edge-continuity regression");
            }
            for(var y=0;y<map.Height;y++)for(var x=0;x<map.Width;x++)
            {
                var tile=first[x,y];check(tile==second[x,y],"repeated presentation generation differed");
                check(!tile.Template.StartsWith("rough",StringComparison.Ordinal),"farm crop tiles were mistaken for natural rough ground");
                if(x>=3 && y>=3 && x<map.Width-3 && y<map.Height-3)
                    check(!tile.Template.StartsWith("shore",StringComparison.Ordinal),"shore artwork moved into inland navigation cells");
            }
        }
    }
    static void AuditCoast(ClassicTerrainPlan plan,BattleMap map,JsonElement layout,Action<bool,string> check)
    {
        var profiles=layout.GetProperty("shoreEdgeProfiles").EnumerateArray().ToDictionary(
            entry=>new ClassicTerrainPlan.Tile(entry.GetProperty("template").GetString()!,entry.GetProperty("frame").GetInt32()),
            entry=>entry.GetProperty("waterEdges").EnumerateArray().Select(edge=>edge.EnumerateArray().Select(n=>n.GetSingle()).ToArray()).ToArray());
        float[] Edge(ClassicTerrainPlan.Tile tile,int edge)
        {
            if(profiles.TryGetValue(tile,out var profile))return profile[edge];
            check(!tile.Template.StartsWith("shore",StringComparison.Ordinal) && !tile.Template.StartsWith("water",StringComparison.Ordinal),"missing original shoreline edge evidence");
            return new float[12];
        }
        var count=0;var worst=0f;var total=0f;
        for(var y=0;y<map.Height;y++)for(var x=0;x<map.Width;x++)
        {
            var a=plan[x,y];
            foreach(var (dx,dy,direction) in new[]{(1,0,2),(0,1,3)})
            {
                if(!map.Inside(x+dx,y+dy))continue;
                var b=plan[x+dx,y+dy];
                if(!a.Template.StartsWith("shore",StringComparison.Ordinal) && !b.Template.StartsWith("shore",StringComparison.Ordinal))continue;
                var first=Edge(a,direction);var second=Edge(b,(direction+2)%4);
                var error=first.Zip(second,(left,right)=>MathF.Abs(left-right)).Average();
                check(error<=.20f,$"disconnected coast at ({x},{y}) direction {direction}: {a} -> {b}, water-profile error {error:F3}");
                worst=Math.Max(worst,error);total+=error;count++;
            }
        }
        check(count>1000,"coast audit did not include module seams, corners and base terrain");
        foreach(var (x,y) in new[]{(3,20),(20,3),(map.Width-4,20),(20,map.Height-4)})
        {
            var tile=plan[x,y];
            check(tile.Template.StartsWith("glat",StringComparison.Ordinal) || tile.Template.StartsWith("green",StringComparison.Ordinal),"shore sand joins directly to unrelated inland material");
        }
        Console.WriteLine($"COAST VERIFIED edges={count} meanWaterError={total/count:F6} worstWaterError={worst:F6}");
    }
}
