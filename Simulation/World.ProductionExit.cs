namespace RedAlert.Simulation;

public enum ProductionExitBlock { None, UnitCrowding, Structure, Terrain }
public readonly record struct ProductionExitResult(Cell? Cell,ProductionExitBlock Block);

public sealed partial class World
{
    // Original rules.ini ExitCoord, in 256 leptons/cell. Refineries instead
    // specify art.ini QueueingCell=4,1; shipyards specify DockingOffset0.
    // Zero-offset NOD barracks/cloning vats use the simulation's +X door face.
    // This models a legal external portal, not animated travel through the SHP.
    public (int X,int Y) ClassicProductionDoorPosition(Building producer)
    {
        var offset=producer.Definition.RulesId.ToUpperInvariant() switch
        {
            "GAWEAP" or "NAWEAP" => (X:384,Y:96),
            "GAPILE" => (X:-64,Y:64),
            "GAREFN" or "NAREFN" => (X:640,Y:0),
            "GAYARD" or "NAYARD" => (X:384,Y:-128),
            _ => (X:0,Y:0)
        };
        return (producer.X+offset.X*Rules.Scale/256,producer.Y+offset.Y*Rules.Scale/256);
    }

    public IReadOnlyList<Cell> ClassicProductionExitPortals(Building producer)
    {
        var door=ClassicProductionDoorPosition(producer);
        var left=producer.CellLeft;var top=producer.CellTop;
        var right=left+producer.Definition.Width;var bottom=top+producer.Definition.Height;
        var dx=door.X-producer.X;var dy=door.Y-producer.Y;
        // The original ground ExitCoord chooses the mouth's side and tangent
        // cell. Never scan around a blocked mouth to emerge behind a wall.
        Cell mouth;
        if(Math.Abs(dy)>=Math.Abs(dx) && dy!=0)
            mouth=new(Math.Clamp(door.X/Rules.Scale,left,right-1),dy>0?bottom:top-1);
        else mouth=new(dx<0?left-1:right,Math.Clamp(door.Y/Rules.Scale,top,bottom-1));
        if(!producer.Definition.WaterBound)return [mouth];
        // Open shipyard bays have no rules ExitCoord. Only immediate wet
        // perimeter cells qualify, ordered from the source art docking point.
        var bays=new List<Cell>();
        for(var y=top;y<bottom;y++){bays.Add(new(left-1,y));bays.Add(new(right,y));}
        for(var x=left;x<right;x++){bays.Add(new(x,top-1));bays.Add(new(x,bottom));}
        return bays.OrderBy(cell=>
        {
            var x=(long)cell.X*Rules.Scale+Rules.Scale/2-door.X;
            var y=(long)cell.Y*Rules.Scale+Rules.Scale/2-door.Y;
            return x*x+y*y;
        }).ThenBy(cell=>cell.Y).ThenBy(cell=>cell.X).ToArray();
    }

    public ProductionExitResult ClassicProductionExit(Building producer,UnitDef definition)
    {
        var units=false;var structures=false;
        foreach(var cell in ClassicProductionExitPortals(producer))
        {
            if(!Map.IsPlayable(cell.X,cell.Y))continue;
            var index=Map.Index(cell.X,cell.Y);
            if(Map.Buildings[index]!=0 || Map.OriginalObjectBlocksMovement(cell.X,cell.Y,MovementDomain.Land)
                && Map.OriginalObjects.Any(item=>item.Definition.Kind==ClassicMapObjectKind.Structure && cell.X>=item.X && cell.X<item.X+item.Width && cell.Y>=item.Y && cell.Y<item.Y+item.Height))
            {structures=true;continue;}
            var domain=producer.Definition.WaterBound?MovementDomain.Water:definition.Domain;
            var amphibious=!producer.Definition.WaterBound && definition.Amphibious;
            if(!Map.Navigable(cell.X,cell.Y,domain,amphibious))continue;
            var inner=new Cell(Math.Clamp(cell.X,producer.CellLeft,producer.CellLeft+producer.Definition.Width-1),
                Math.Clamp(cell.Y,producer.CellTop,producer.CellTop+producer.Definition.Height-1));
            var water=domain==MovementDomain.Water || amphibious
                && (Map.Ground[Map.Index(inner.X,inner.Y)]==Terrain.Water && !Map.HasBridge(inner)
                    || Map.Ground[index]==Terrain.Water && !Map.HasBridge(cell));
            if(domain!=MovementDomain.Air && (water
                ?Map.WaterSurfaceLevel(inner.X,inner.Y)!=Map.WaterSurfaceLevel(cell.X,cell.Y) || Map.IsRamp(inner) || Map.IsRamp(cell)
                :!Map.CanTraverseTerrain(inner,cell)))continue;
            if(simulatingUnits?occupied[index]>0:Units.Any(unit=>OccupiesSurface(unit) && unit.CellX==cell.X && unit.CellY==cell.Y))
            {units=true;continue;}
            return new(cell,ProductionExitBlock.None);
        }
        // A unit occupying a valid bay is temporary congestion. It must never
        // trigger the Soviet barracks wall-refund rule or cloning side effects.
        return new(null,units?ProductionExitBlock.UnitCrowding:structures?ProductionExitBlock.Structure:ProductionExitBlock.Terrain);
    }

    // Official cumulative 1.005 change: walls/buildings blocking a Soviet
    // barracks refund its unit without producing an original or a clone.
    // https://cncnz.com/features/technical-support-help-guides/red-alert-2-v1001-v1006-patch-history/
    public bool ClassicRefundBlockedBarracks(Building producer,UnitDef definition,ProductionExitBlock block)=>
        block==ProductionExitBlock.Structure && definition.Infantry
        && producer.Definition.RulesId.Equals("NAHAND",StringComparison.OrdinalIgnoreCase);
}
