using System.Collections.Concurrent;

namespace RedAlert.Simulation;

// The simulation consumes immutable original DATA. The frontend supplies its
// resource reader (Godot PCK or loose files); tests can register decoded fixtures.
public static class ClassicMapRepository
{
    static readonly ConcurrentDictionary<string,ClassicMapDefinition> maps=new(StringComparer.Ordinal);
    public static Func<string,ClassicMapDefinition?>? Loader {get;set;}
    public static void Register(ClassicMapDefinition map)=>maps[map.Id]=map;
    public static ClassicMapDefinition Get(string id)
    {
        if(maps.TryGetValue(id,out var map))return map;
        map=Loader?.Invoke(id)??throw new InvalidDataException($"未找到原版地图：{id}");
        maps.TryAdd(id,map);return map;
    }
}

public sealed partial class BattleMap
{
    public ClassicMapDefinition? OriginalDefinition {get;private set;}
    public ClassicMapCell? OriginalCellAt(int x,int y)=>OriginalDefinition?.CellAt(x,y);
    void ApplyOriginalMap(ClassicMapDefinition definition)
    {
        OriginalDefinition=definition;
        PlayableMinU=definition.PlayableBounds.MinU;PlayableMaxU=definition.PlayableBounds.MaxU;
        PlayableMinV=definition.PlayableBounds.MinV;PlayableMaxV=definition.PlayableBounds.MaxV;
        Spawns=definition.Spawns.OrderBy(spawn=>spawn.Index).Select(spawn=>new Cell(spawn.X,spawn.Y)).ToArray();
        Array.Fill(Ground,Terrain.Rock);Elevation=new byte[Ground.Length];Bridges=new bool[Ground.Length];
        Ramps=new RampDirection[Ground.Length];BridgeIds=new int[Ground.Length];
        foreach(var cell in definition.RuntimeCells)
        {
            var i=Index(cell.X,cell.Y);Ground[i]=cell.UnderlyingGround;Elevation[i]=(byte)Math.Clamp(cell.SurfaceLevel,0,255);Bridges[i]=cell.Bridge;
            if(cell.ResourceKind>0)Ore[i]=(cell.ResourceDensity+1)*Math.Max(1,cell.ResourceValue)*4;
        }
        var spans=new List<BridgeSpan>();
        foreach(var bridge in definition.Bridges)
        {
            var axis=string.Equals(bridge.Axis,"Y",StringComparison.OrdinalIgnoreCase)?BridgeAxis.Y:BridgeAxis.X;
            var sx=bridge.MaxX-bridge.MinX+1;var sy=bridge.MaxY-bridge.MinY+1;
            if(sx<=0 || sy<=0)continue;
            var length=axis==BridgeAxis.X?sx:sy;var width=axis==BridgeAxis.X?sy:sx;
            var middle=width/2;var bankA=axis==BridgeAxis.X?new Cell(bridge.MinX-1,bridge.MinY+middle):new Cell(bridge.MinX+middle,bridge.MinY-1);
            var bankB=axis==BridgeAxis.X?new Cell(bridge.MaxX+1,bridge.MinY+middle):new Cell(bridge.MinX+middle,bridge.MaxY+1);
            var id=spans.Count+1;spans.Add(new BridgeSpan(id,axis,new Cell(bridge.MinX,bridge.MinY),length,width,bridge.DeckLevel*Rules.Scale,bankA,bankB));
            foreach(var cell in bridge.Cells)if(Inside(cell.X,cell.Y))BridgeIds[Index(cell.X,cell.Y)]=id;
        }
        BridgeSpans=spans.ToArray();
        ApplyOriginalObjects(definition);
        InitializeTerrainTargets();
    }
}
