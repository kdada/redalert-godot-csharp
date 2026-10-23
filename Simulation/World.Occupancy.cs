namespace RedAlert.Simulation;

public sealed partial class World
{
    // Ground bodies are deliberately smaller than one cell. Sprite roofs, guns,
    // shadows and tall walls do not enlarge authoritative collision footprints.
    public static int UnitCollisionRadius(Unit unit)=>unit.Definition.Infantry?Rules.Scale/8:Rules.Scale/3;

    public bool CanOccupyUnitPosition(Unit unit,int fixedX,int fixedY)
    {
        var u=(long)fixedX-fixedY;var v=(long)fixedX+fixedY;
        if(fixedX<0 || fixedY<0 || fixedX>=Map.Width*Rules.Scale || fixedY>=Map.Height*Rules.Scale
            || u<(long)Map.PlayableMinU*Rules.Scale || u>(long)Map.PlayableMaxU*Rules.Scale
            || v<(long)Map.PlayableMinV*Rules.Scale || v>(long)Map.PlayableMaxV*Rules.Scale)return false;
        return Map.Navigable(fixedX/Rules.Scale,fixedY/Rules.Scale,unit.Definition.Domain,unit.Definition.Amphibious)
            && (unit.Definition.Domain==MovementDomain.Air || !Map.BuildingBlocksMovement(fixedX,fixedY,fixedX,fixedY,UnitCollisionRadius(unit)));
    }

    public bool BuildingAreaHasUnits(int x,int y,int width,int height)
    {
        var left=(long)x*Rules.Scale;var top=(long)y*Rules.Scale;
        var right=((long)x+width)*Rules.Scale;var bottom=((long)y+height)*Rules.Scale;
        foreach(var unit in Units)
        {
            if(!OccupiesSurface(unit))continue;
            var radius=UnitCollisionRadius(unit);
            if((long)unit.X+radius>=left && (long)unit.X-radius<=right
                && (long)unit.Y+radius>=top && (long)unit.Y-radius<=bottom)return true;
        }
        return false;
    }
}
