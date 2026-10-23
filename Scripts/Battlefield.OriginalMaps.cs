using Godot;
using RedAlert.Simulation;

namespace RedAlert;

public partial class Battlefield
{
    readonly List<(ClassicMapCell Cell,Vector2 Point,Color Light,Rect2 Bounds)> originalVisibleTiles=[];
    Vector2 OriginalDeckPoint(ClassicMapCell cell)=>Project(new Vector2(cell.X+.5f,cell.Y+.5f))-new Vector2(0,cell.SurfaceLevel*15*Zoom);
    void QueueOriginalMapTile(World world,int x,int y,Color light,bool groundOnly=false)
    {
        var source=world.Map.OriginalCellAt(x,y);if(source is null)return;
        var cell=TerrainVisualCell(world.Map,source);if(cell is null)return;
        if(groundOnly){cell=ShroudFringeOriginalGroundCell(world,cell);if(cell is null)return;}
        // TMP extra pixels already include source-relative cliff geometry. Only
        // original ground Level is projected here; deck SurfaceLevel is for actors.
        var point=Project(new Vector2(x+.5f,y+.5f))-new Vector2(0,cell.Level*15*Zoom);
        var bounds=Controller.Graphics.OriginalMapTileBounds(cell,point,Zoom);
        if(cell.Bridge)bounds=bounds.Merge(Controller.Graphics.OriginalMapBridgeOverlayBounds(cell,OriginalDeckPoint(cell),Zoom));
        if(bounds.Intersects(new Rect2(Vector2.Zero,Size)))
        {
            if(groundOnly)originalShroudGroundTiles.Add((cell,point,light,bounds));
            else originalVisibleTiles.Add((cell,point,light,bounds));
        }
    }
    void DrawOriginalMapGround(World world)
    {
        originalVisibleTiles.Sort(static(a,b)=>a.Cell.X+a.Cell.Y!=b.Cell.X+b.Cell.Y?(a.Cell.X+a.Cell.Y).CompareTo(b.Cell.X+b.Cell.Y):a.Cell.X.CompareTo(b.Cell.X));
        foreach(var tile in OriginalGroundWithShroudFringe())
        {
            var fringe=world.Map.IsPlayable(tile.Cell.X,tile.Cell.Y) && !Controller.ViewExplored(tile.Cell.X,tile.Cell.Y);
            if(Controller.Graphics.DrawOriginalMapTile(this,tile.Cell,tile.Point,Zoom,tile.Light,includeExtra:!fringe))LandscapeGroundTilesDrawn++;
            if(tile.Cell.RampType>0)LandscapeRampTilesDrawn++;
            if(!fringe && Controller.Graphics.OriginalMapTileHasExtra(tile.Cell))LandscapeCliffFacesDrawn++;
        }
        // Finish all native TMP diamonds/extras before resource overlays, so a
        // later neighboring ground tile cannot repaint a visible deposit.
        foreach(var tile in originalVisibleTiles)
            if(world.Map.IsPlayable(tile.Cell.X,tile.Cell.Y))
                DrawOreTile(world,tile.Cell.X,tile.Cell.Y,ProjectGround(new Vector2(tile.Cell.X+.5f,tile.Cell.Y+.5f)),BattleMap.Noise(tile.Cell.X,tile.Cell.Y,world.Settings.Seed),Controller.ViewVisible(tile.Cell.X,tile.Cell.Y),tile.Light);
        foreach(var tile in originalVisibleTiles)
        {
            if((!tile.Cell.Bridge || !world.Map.IsBridgeCellIntact(tile.Cell.X,tile.Cell.Y)))continue;
            if(Controller.Graphics.DrawOriginalMapBridgeOverlay(this,tile.Cell,OriginalDeckPoint(tile.Cell),Zoom,tile.Light))
            {OriginalBridgePiecesDrawn++;BridgeTilesDrawn++;}
        }
        BridgeSpansDrawn=world.Map.BridgeSpans.Count(span=>originalVisibleTiles.Any(t=>t.Cell.Bridge && t.Cell.X>=span.Origin.X && t.Cell.X<span.Origin.X+span.SizeX && t.Cell.Y>=span.Origin.Y && t.Cell.Y<span.Origin.Y+span.SizeY));
    }
    void DrawOriginalMapForeground(World world,Entity entity,Vector2 point)
    {
        var height=entity is Unit unit?UnitSurfaceLevel(unit)+unit.Altitude/(float)Rules.Scale:(float)world.Map.SurfaceHeightAt(entity.X/(double)Rules.Scale,entity.Y/(double)Rules.Scale);
        var depth=(entity.X+entity.Y)/(float)Rules.Scale;
        var bounds=new Rect2(point-new Vector2(entity is Building?140:55,entity is Building?180:80)*Zoom,new Vector2(entity is Building?280:110,entity is Building?230:110)*Zoom);
        DrawOriginalMapForegroundAt(world,entity.CellX,entity.CellY,height,depth,bounds,entity is Unit);
    }
    void DrawOriginalMapForegroundAt(World world,int cellX,int cellY,float height,float depth,Rect2 bounds,bool skipTraversable)
    {
        foreach(var tile in originalVisibleTiles)
        {
            if(landscapeForegroundBudget<=0)break;
            if(tile.Cell.Bridge)continue;
            if(tile.Cell.X==cellX && tile.Cell.Y==cellY || tile.Cell.X+tile.Cell.Y+2<=depth || !tile.Bounds.Intersects(bounds))continue;
            var corners=world.Map.SurfaceCornersAt(tile.Cell.X,tile.Cell.Y);
            var top=Math.Max(Math.Max(corners.NorthWest,corners.NorthEast),Math.Max(corners.SouthWest,corners.SouthEast))/(float)Rules.Scale;
            if(top<=height+.001f)continue;
            if(skipTraversable && world.Map.CanTraverseTerrain(new Cell(cellX,cellY),new Cell(tile.Cell.X,tile.Cell.Y)))continue;
            if(Controller.Graphics.DrawOriginalMapTileForeground(this,tile.Cell,tile.Point,Zoom,tile.Light,true))
            {LandscapeForegroundDrawCommands++;landscapeForegroundBudget--;}
        }
        foreach(var tile in originalVisibleTiles)
        {
            if((!tile.Cell.Bridge || !world.Map.IsBridgeCellIntact(tile.Cell.X,tile.Cell.Y)) || height>tile.Cell.SurfaceLevel+.001f || !tile.Bounds.Intersects(bounds) || landscapeForegroundBudget<=0)continue;
            var deck=OriginalDeckPoint(tile.Cell);
            if(height<tile.Cell.SurfaceLevel-.5f && depth<=tile.Cell.X+tile.Cell.Y+3 && tile.Cell.HighBridge)
            {
                if(Controller.Graphics.DrawOriginalMapBridgeOverlay(this,tile.Cell,deck,Zoom,tile.Light))
                {landscapeForegroundBudget--;LandscapeForegroundDrawCommands++;OriginalBridgeForegroundDraws++;}
                continue;
            }
            foreach(var span in world.Map.BridgeSpans)
            {
                if(tile.Cell.X<span.Origin.X || tile.Cell.X>=span.Origin.X+span.SizeX || tile.Cell.Y<span.Origin.Y || tile.Cell.Y>=span.Origin.Y+span.SizeY)continue;
                var along=span.Axis==BridgeAxis.X?Vector2.Right:Vector2.Down;var cross=span.Axis==BridgeAxis.X?Vector2.Down:Vector2.Right;
                var step=span.Axis==BridgeAxis.X?tile.Cell.X-span.Origin.X:tile.Cell.Y-span.Origin.Y;
                var edge=new Vector2(span.Origin.X,span.Origin.Y)+along*step+cross*span.Width;var end=edge+along;
                var from=edge.X+edge.Y;var to=end.X+end.Y;if(depth>=to)break;
                var right=end.X>edge.X;var cut=depth<=from?(right?float.NegativeInfinity:float.PositiveInfinity):Mathf.Lerp(Project(edge).X,Project(end).X,(depth-from)/(to-from));
                if(Controller.Graphics.DrawOriginalMapBridgeForeground(this,tile.Cell,deck,Zoom,tile.Light,cut,right))
                {landscapeForegroundBudget--;LandscapeForegroundDrawCommands++;OriginalBridgeForegroundDraws++;}
                break;
            }
        }
    }
    float UnitSurfaceLevel(Unit unit)
    {
        var map=Controller.Battle!.Map;
        var sea=unit.Definition.Domain==MovementDomain.Water && (!unit.Definition.Amphibious || map.Ground[map.Index(unit.CellX,unit.CellY)]==Terrain.Water);
        return sea?map.WaterSurfaceLevel(unit.CellX,unit.CellY):(float)map.SurfaceHeightAt(unit.X/(double)Rules.Scale,unit.Y/(double)Rules.Scale);
    }
    Vector2 UnitActorPoint(Unit unit)
    {
        var cell=new Vector2(unit.X/(float)Rules.Scale,unit.Y/(float)Rules.Scale);
        return Project(cell)-new Vector2(0,(UnitSurfaceLevel(unit)+unit.Altitude/(float)Rules.Scale)*LandscapeLevelRise*Zoom);
    }
}
