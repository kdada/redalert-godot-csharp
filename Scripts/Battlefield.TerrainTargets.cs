using Godot;
using RedAlert.Simulation;

namespace RedAlert;

public partial class Battlefield
{
    BattleMap? terrainVisualMap;
    readonly Dictionary<int,ClassicMapCell?> destroyedBridgeVisuals=[];
    ClassicMapCell[]? destroyedBridgeFlatSources;
    readonly Godot.Collections.Array<Godot.Collections.Dictionary> destroyedBridgeVisualSources=new();
    public int VisibleDestroyedOriginalObjects=>visibleOriginalObjects.Count(item=>Controller.Battle?.Map.IsOriginalObjectDestroyed(item.ObjectIndex)==true);
    public bool OriginalObjectQueued(int objectIndex)=>visibleOriginalObjects.Any(item=>item.ObjectIndex==objectIndex);
    public bool BridgeVisualQueued(int bridgeId)=>Controller.Battle is {} world && originalVisibleTiles.Any(tile=>tile.Cell.Bridge
        && world.Map.BridgeIds[world.Map.Index(tile.Cell.X,tile.Cell.Y)]==bridgeId);

    // Map state owns destruction and support heights. These are display-only
    // copies; OriginalCellAt and the registered source definition stay untouched.
    ClassicMapCell? TerrainVisualCell(BattleMap map,ClassicMapCell original)
    {
        if(!ReferenceEquals(terrainVisualMap,map))
        {terrainVisualMap=map;destroyedBridgeVisuals.Clear();destroyedBridgeFlatSources=null;destroyedBridgeVisualSources.Clear();}
        if(!original.Bridge || map.IsBridgeCellIntact(original.X,original.Y))return original;
        var index=map.Index(original.X,original.Y);
        if(destroyedBridgeVisuals.TryGetValue(index,out var cached))return cached;
        var ground=map.Ground[index];var level=map.ElevationAt(original.X,original.Y);
        // State numbers encode source assembly/lane geometry. The retained catalog
        // does not establish which is a collapsed state. Reuse an actual original
        // underlying-terrain TMP instead of guessing an overlay state or retaining
        // a deck baked into an OVRPS tile's ground/extra pixels.
        var rawSubstrate=(original.Template.StartsWith("water",StringComparison.OrdinalIgnoreCase)
            || original.Template.Equals("clear01",StringComparison.OrdinalIgnoreCase))
            && original.RampType==0 && !Controller.Graphics.OriginalMapTileHasExtra(original);
        destroyedBridgeFlatSources??=map.OriginalDefinition!.RuntimeCells.Where(cell=>!cell.Bridge && cell.RampType==0
            && cell.TemplateHeight==0 && !cell.Template.StartsWith("ovrps",StringComparison.OrdinalIgnoreCase)
            && !Controller.Graphics.OriginalMapTileHasExtra(cell)
            && Controller.Graphics.OriginalMapTileBounds(cell,Vector2.Zero,1).Size.X>0).ToArray();
        ClassicMapCell? Nearest(IEnumerable<ClassicMapCell> cells)=>cells
            .OrderBy(cell=>Math.Abs(cell.X-original.X)+Math.Abs(cell.Y-original.Y)).ThenBy(cell=>cell.Y).ThenBy(cell=>cell.X).FirstOrDefault();
        var selection=rawSubstrate?"preserved original water/clear TMP substrate":"nearest same-ground same-level original TMP substrate";
        var under=rawSubstrate?original:Nearest(destroyedBridgeFlatSources.Where(cell=>cell.Ground==ground && cell.Level==level));
        var waterLayer=ground==Terrain.Water;
        if(under is null)
        {
            // Rock/coast support has no flat rock TMP. Draw an actual flat source
            // diamond at the canonical underlying plane; do not leave a black hole.
            under=Nearest(destroyedBridgeFlatSources.Where(cell=>cell.Level==level && (cell.Ground==Terrain.Water)==waterLayer));
            selection="original flat TMP on the same underlying level and water/land layer; source ground type differs";
        }
        if(under is null)
        {
            under=Nearest(destroyedBridgeFlatSources.Where(cell=>(cell.Ground==Terrain.Water)==waterLayer));
            selection="original flat TMP transplanted to canonical underlying level on the same water/land layer";
        }
        if(under is null)throw new InvalidOperationException("No drawable original flat TMP for collapsed bridge substrate layer");
        ClassicMapCell visual=new()
        {
            X=original.X,Y=original.Y,SourceX=original.SourceX,SourceY=original.SourceY,Playable=original.Playable,
            TileIndex=under.TileIndex,SubTile=under.SubTile,Level=level,SurfaceLevel=level,
            TileSetId=under.TileSetId,Template=under.Template,TemplateHeight=under.TemplateHeight,
            TerrainType=under.TerrainType,Ground=ground,UnderlyingGround=ground,
            LandPassable=map.Walkable(original.X,original.Y),WaterPassable=map.Navigable(original.X,original.Y,MovementDomain.Water),
            Buildable=false,Bridge=false,HighBridge=false,OverlayIndex=255
        };
        destroyedBridgeVisuals[index]=visual;
        destroyedBridgeVisualSources.Add(new()
        {
            ["x"]=original.X,["y"]=original.Y,["originalTileIndex"]=original.TileIndex,["originalSubTile"]=original.SubTile,
            ["originalTemplate"]=original.Template,["originalOverlay"]=original.OverlayName,["originalOverlayData"]=original.OverlayData,
            ["sourceX"]=under.SourceX,["sourceY"]=under.SourceY,["tileIndex"]=under.TileIndex,
            ["subTile"]=under.SubTile,["template"]=under.Template,["level"]=level,["sourceAvailable"]=true,
            ["exactBrokenStateAvailable"]=false,["preservedRawSubstrate"]=rawSubstrate,
            ["sourceGround"]=under.Ground.ToString(),["sourceLevel"]=under.Level,["runtimeGround"]=ground.ToString(),["waterLayer"]=waterLayer,
            ["selection"]=selection
        });
        return visual;
    }
    public Godot.Collections.Dictionary GetTerrainVisualAudit()=>new()
    {
        ["destroyedBridgeSourceTiles"]=new Godot.Collections.Array<Godot.Collections.Dictionary>(destroyedBridgeVisualSources),
        ["missingUnderlyingSourceTiles"]=destroyedBridgeVisualSources.Count(source=>!source["sourceAvailable"].AsBool()),
        ["queuedDestroyedObjects"]=VisibleDestroyedOriginalObjects,
        ["exactBrokenBridgeStateAvailable"]=false,
        ["limitation"]="Retained high/low bridge state metadata does not prove a collapsed-state mapping. Destroyed spans use actual original underlying-terrain TMP source geometry; exact debris/roadhead silhouettes remain unavailable."
    };

    bool TerrainCellVisible(World world,int x,int y)=>world.Map.IsPlayable(x,y) && Controller.ViewExplored(x,y) && Controller.ViewVisible(x,y);
    TerrainTarget? TerrainArtworkAt(Vector2 point,out int x,out int y)
    {
        var world=Controller.Battle!;
        // Pick the same source art bounds and depth order that Battlefield drew.
        // Never pick remembered trees through current fog or a stale destroyed row.
        foreach(var item in visibleOriginalObjects.OrderByDescending(item=>item.Depth).ThenByDescending(item=>item.ObjectIndex))
        {
            if(!item.Bounds.HasPoint(point) || world.Map.IsOriginalObjectDestroyed(item.ObjectIndex))continue;
            var tree=world.Map.TerrainTargets.FirstOrDefault(target=>target.Kind==TerrainTargetKind.Tree && target.ObjectIndex==item.ObjectIndex && target.Alive);
            if(tree is null || !TerrainCellVisible(world,tree.CellX,tree.CellY))continue;
            x=tree.CellX;y=tree.CellY;return tree;
        }
        // A raised deck can project over a different ground cell. Pick its actual
        // four-corner deck plane before the normal terrain unprojection.
        foreach(var tile in originalVisibleTiles.AsEnumerable().Reverse())
        {
            var cell=tile.Cell;
            if(!cell.Bridge || !world.Map.IsBridgeCellIntact(cell.X,cell.Y) || !TerrainCellVisible(world,cell.X,cell.Y))continue;
            var up=new Vector2(0,cell.SurfaceLevel*15*Zoom);
            Vector2[] polygon=[Project(new(cell.X,cell.Y))-up,Project(new(cell.X+1,cell.Y))-up,
                Project(new(cell.X+1,cell.Y+1))-up,Project(new(cell.X,cell.Y+1))-up];
            if(!Geometry2D.IsPointInPolygon(point,polygon))continue;
            var bridge=world.Map.TerrainTargetAt(cell.X,cell.Y);
            if(bridge is not {Kind:TerrainTargetKind.Bridge,Alive:true})continue;
            x=cell.X;y=cell.Y;return bridge;
        }
        var ground=UnprojectGround(point);x=(int)MathF.Floor(ground.X);y=(int)MathF.Floor(ground.Y);
        return TerrainCellVisible(world,x,y)?world.Map.TerrainTargetAt(x,y):null;
    }

    // localPoint is in Battlefield coordinates. forceFire permits an empty cell;
    // AttackMode does the same. This query never issues commands or reveals fog.
    // Cursor hosts must keep placement/support/maintenance priority above it.
    public bool TryGetTerrainHoverTarget(Vector2 localPoint,bool forceFire,out int targetId,out int cellX,out int cellY)
    {
        targetId=0;cellX=0;cellY=0;
        var world=Controller.Battle;
        if(world is null || Controller.IsSpectating || Controller.MatchMenuOpen || Placement is not null || SupportMode || MaintenanceMode is not null
            || !new Rect2(Vector2.Zero,Size).HasPoint(localPoint))return false;
        var target=TerrainArtworkAt(localPoint,out cellX,out cellY);
        if(!TerrainCellVisible(world,cellX,cellY))return false;
        var x=cellX;var y=cellY;
        var actors=Selection.Select(world.Find).OfType<Unit>().Where(unit=>unit.Alive && unit.Owner==Controller.LocalPlayer);
        if(target is {Kind:TerrainTargetKind.Bridge} && !forceFire && !AttackMode)return false;
        if(target is {Alive:true} && actors.Any(unit=>world.CanAttackTerrain(unit,target.Id)))
        {targetId=target.Id;return true;}
        return (forceFire || AttackMode) && target is null && actors.Any(unit=>world.CanAttackGround(unit,x,y));
    }
    bool IssueTerrainAttackAt(Vector2 point,bool forceFire)
    {
        if(!TryGetTerrainHoverTarget(point,forceFire,out var targetId,out var x,out var y))return false;
        var world=Controller.Battle!;
        var actors=Selection.Select(world.Find).OfType<Unit>()
            .Where(unit=>unit.Alive && unit.Owner==Controller.LocalPlayer
                && (targetId<0?world.CanAttackTerrain(unit,targetId):world.CanAttackGround(unit,x,y)))
            .Select(unit=>unit.Id).ToArray();
        if(actors.Length==0)return false;
        Controller.Issue(new GameCommand{Type=CommandType.AttackGround,Actors=actors,Target=targetId,X=x,Y=y});
        return true;
    }
}
