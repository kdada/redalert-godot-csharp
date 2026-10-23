using RedAlert.Simulation;

namespace RedAlert;

public readonly record struct PreviewPoint(float X,float Y);

/// <summary>
/// Reversible, linear mapping of the playable battlefield rectangle to radar UV.
/// U follows battlefield horizontal (x-y), V follows battlefield vertical (x+y).
/// Terrain, markers, camera corners and navigation all use these exact bounds.
/// Pure managed math also runs in background menu-preview workers.
/// </summary>
public readonly struct MapPreviewProjection
{
    readonly float minU,minV,spanU,spanV;
    public MapPreviewProjection(BattleMap map)
    {
        minU=map.PlayableMinU;minV=map.PlayableMinV;
        spanU=Math.Max(1,map.PlayableMaxU-map.PlayableMinU);
        spanV=Math.Max(1,map.PlayableMaxV-map.PlayableMinV);
    }
    public PreviewPoint Project(float x,float y)
        =>new((x-y-minU)/spanU,(x+y-minV)/spanV);
    public PreviewPoint Unproject(float x,float y)
    {
        var worldU=minU+x*spanU;var worldV=minV+y*spanV;
        return new((worldU+worldV)/2,(worldV-worldU)/2);
    }
    // Nearest cell samples at the outer edge can fall outside the discrete cell
    // mask. Keep their backing deterministic without changing the map projection.
    public static Terrain BackingTerrain(BattleMap map,int x,int y,uint seed)
        =>map.Inside(x,y)?map.Ground[map.Index(x,y)]:BattleMap.Noise(x,y,seed)%9==0?Terrain.Dirt:Terrain.Grass;
}
