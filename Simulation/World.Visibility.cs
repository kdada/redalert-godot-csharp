namespace RedAlert.Simulation;

// Vanilla DATA: rules.ini General FogOfWar=no (172), ShroudGrow=no (551),
// MultiplayerDialogSettings ShadowGrow=no / FogOfWar=no (2510 / 2520).
// GAGAP GapRadiusInCells=10, Powered=true; GASPYSAT SpySat=yes, Powered=true.
internal static class ClassicVisibilityRules
{
    public const byte Explored=1,DirectSight=2,Satellite=4,SatelliteHistory=8;
    public const int GapRadius=10;
}

public sealed partial class World
{
    public bool ClassicDirectSight(int player,int x,int y)=>player>=0 && player<Players.Length && Map.IsPlayable(x,y)
        && (Players[player].Visibility[Map.Index(x,y)]&ClassicVisibilityRules.DirectSight)!=0;
    public bool ClassicSpySatelliteActive(int player)=>player>=0 && player<Players.Length && Buildings.Any(building=>
        building.Type==BuildingType.SpySatellite && Allied(player,building.Owner) && ClassicSupportPowered(building));
    bool ClassicSpySatellitePresent(int player)=>Buildings.Any(building=>building.Type==BuildingType.SpySatellite
        && Allied(player,building.Owner) && !Players[building.Owner].Defeated && building.Alive && building.Complete);
    void PrepareClassicVisibility()
    {
        foreach(var player in Players)
        {
            var present=ClassicSpySatellitePresent(player.Id);
            // Gameplay documentation: selling/destroying the last satellite resets
            // its beneficiary's shroud. History lives in already-hashed cell bytes.
            var reset=!present && player.Visibility.Any(value=>(value&ClassicVisibilityRules.SatelliteHistory)!=0);
            for(var i=0;i<player.Visibility.Length;i++)
                player.Visibility[i]=!reset && Map.IsPlayable(i%Map.Width,i/Map.Width)
                    ?(byte)(player.Visibility[i]&(ClassicVisibilityRules.Explored|(present?ClassicVisibilityRules.SatelliteHistory:0))):(byte)0;
        }
    }
    void ApplyClassicSatelliteVisibility()
    {
        foreach(var player in Players)
        {
            if(player.Defeated || !ClassicSpySatelliteActive(player.Id))continue;
            for(var i=0;i<player.Visibility.Length;i++)if(Map.IsPlayable(i%Map.Width,i/Map.Width))
                player.Visibility[i]|=ClassicVisibilityRules.Satellite|ClassicVisibilityRules.SatelliteHistory;
        }
    }
    // Reveal crates uncover once. They do not become a powered satellite or
    // continuously undo an enemy Gap Generator's later re-shrouding.
    public void RevealClassicMap(int player)
    {
        if(player<0 || player>=Players.Length || Players[player].Defeated)return;
        for(var i=0;i<Players[player].Visibility.Length;i++)if(Map.IsPlayable(i%Map.Width,i/Map.Width))
            Players[player].Visibility[i]|=ClassicVisibilityRules.Explored;
        ApplyClassicGapShroud();
    }
    void ApplyClassicGapShroud()
    {
        foreach(var generator in Buildings)
        {
            if(generator.Type!=BuildingType.GapGenerator || !ClassicSupportPowered(generator))continue;
            foreach(var player in Players)
            {
                if(player.Defeated || Allied(player.Id,generator.Owner))continue;
                var radius=ClassicVisibilityRules.GapRadius;var radiusSquared=radius*radius;
                for(var y=Math.Max(0,generator.CellY-radius);y<=Math.Min(Map.Height-1,generator.CellY+radius);y++)
                {
                    var dy=y-generator.CellY;var extent=IntegerSqrt(radiusSquared-dy*dy);
                    for(var x=Math.Max(0,generator.CellX-extent);x<=Math.Min(Map.Width-1,generator.CellX+extent);x++)
                    {
                        if(!Map.IsPlayable(x,y))continue;var index=Map.Index(x,y);var value=player.Visibility[index];
                        // Direct nearby sight can uncover within the gap. Once the
                        // scout moves away, Gap erases that exploration again. The
                        // powered satellite cannot defeat Gap; its provenance stays.
                        if((value&ClassicVisibilityRules.DirectSight)==0)
                            player.Visibility[index]=(byte)(value&ClassicVisibilityRules.SatelliteHistory);
                    }
                }
            }
        }
    }
}
