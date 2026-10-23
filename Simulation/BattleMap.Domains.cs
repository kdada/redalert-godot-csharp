namespace RedAlert.Simulation;

public sealed partial class BattleMap
{
    public bool AllowsRuleObject(string rulesId)
    {
        var section=OriginalDefinition?.MapRules.FirstOrDefault(pair=>string.Equals(pair.Key,rulesId,StringComparison.OrdinalIgnoreCase)).Value;
        if(section is null)return true;
        var tech=section.FirstOrDefault(pair=>string.Equals(pair.Key,"TechLevel",StringComparison.OrdinalIgnoreCase)).Value;
        return !int.TryParse(tech,out var level) || level>=0;
    }
    public bool Navigable(int x,int y,MovementDomain domain,bool amphibious=false)
    {
        if(!IsPlayable(x,y))return false;
        if(domain==MovementDomain.Air)return true;
        var i=Index(x,y);if(Buildings[i]!=0)return false;
        if(amphibious)
        {
            if(Walkable(x,y))return true;
            return !OriginalObjectBlocksMovement(x,y,MovementDomain.Water)
                && RuntimeWaterPassable(x,y) && !Bridges[i];
        }
        if(domain==MovementDomain.Water)
            return !OriginalObjectBlocksMovement(x,y,MovementDomain.Water)
                && RuntimeWaterPassable(x,y) && (!Bridges[i] || OriginalCellAt(x,y)?.HighBridge==true);
        if(amphibious && Ground[i]==Terrain.Water)return !Bridges[i];
        return Walkable(x,y);
    }
    public bool CanTraverseMovement(Cell from,Cell to,MovementDomain domain,bool amphibious=false)
    {
        if(!Navigable(from.X,from.Y,domain,amphibious) || !Navigable(to.X,to.Y,domain,amphibious))return false;
        if(domain==MovementDomain.Air)return true;
        var dx=to.X-from.X;var dy=to.Y-from.Y;
        if(Math.Abs(dx)>1 || Math.Abs(dy)>1)return false;
        if(domain==MovementDomain.Land && !amphibious)return CanTraverseTerrain(from,to);
        if(dx!=0 && dy!=0)
        {
            var a=new Cell(to.X,from.Y);var b=new Cell(from.X,to.Y);
            return Navigable(a.X,a.Y,domain,amphibious) && Navigable(b.X,b.Y,domain,amphibious)
                && CanTraverseMovement(from,a,domain,amphibious) && CanTraverseMovement(from,b,domain,amphibious)
                && CanTraverseMovement(a,to,domain,amphibious) && CanTraverseMovement(b,to,domain,amphibious);
        }
        if(domain==MovementDomain.Water && !amphibious)return WaterSurfaceLevel(from.X,from.Y)==WaterSurfaceLevel(to.X,to.Y);
        if(Ground[Index(from.X,from.Y)]!=Terrain.Water && Ground[Index(to.X,to.Y)]!=Terrain.Water)return CanTraverseTerrain(from,to);
        return ElevationAt(from)==ElevationAt(to) && !IsRamp(from) && !IsRamp(to);
    }
    bool RuntimeWaterPassable(int x,int y) => (OriginalCellAt(x,y) is { } original?original.WaterPassable:Ground[Index(x,y)]==Terrain.Water)
        || (Ground[Index(x,y)]==Terrain.Water && IsCollapsedBridgeCell(x,y));
    public int WaterSurfaceLevel(int x,int y)=>IsCollapsedBridgeCell(x,y)?CollapsedBridgeSurfaceLevel(x,y):OriginalCellAt(x,y)?.Level??ElevationAt(x,y);
    public bool FootprintFree(BuildingDef definition,int x,int y)
    {
        if(!definition.WaterBound)return FootprintFree(x,y,definition.Width,definition.Height);
        if(OriginalDefinition is {ShipyardsAllowed:false})return false;
        var w=definition.Width;var h=definition.Height;
        if(!IsPlayablePoint(x,y) || !IsPlayablePoint(x+w,y+h) || !IsPlayablePoint(x+w,y) || !IsPlayablePoint(x,y+h))return false;
        for(var dy=0;dy<h;dy++)for(var dx=0;dx<w;dx++)
        {
            var cx=x+dx;var cy=y+dy;if(!IsPlayable(cx,cy))return false;var i=Index(cx,cy);
            if(Ground[i]!=Terrain.Water || Bridges[i] || Buildings[i]!=0 || Ore[i]!=0 || OriginalObjectBlocksPlacement(cx,cy))return false;
        }
        // A shipyard sits in water alongside a usable bank, not arbitrarily at sea.
        for(var cy=y-2;cy<y+h+2;cy++)for(var cx=x-2;cx<x+w+2;cx++)
            if(IsPlayable(cx,cy) && Ground[Index(cx,cy)] is Terrain.Grass or Terrain.Dirt && !Bridges[Index(cx,cy)])return true;
        return false;
    }
}
