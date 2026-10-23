using RedAlert.Simulation;

namespace RedAlert;

/// <summary>Immutable, engine-independent preview payload. RGBA pixels use one pixel per map cell.</summary>
public sealed class MenuMapPreviewData
{
    readonly byte[] pixels;
    public uint Seed {get;}
    public int Width {get;}
    public int Height {get;}
    public IReadOnlyList<Cell> Spawns {get;}
    public IReadOnlyList<PreviewPoint> SpawnMarkers {get;}
    internal MenuMapPreviewData(uint seed,int width,int height,byte[] pixels,Cell[] spawns,PreviewPoint[] markers)
    {Seed=seed;Width=width;Height=height;this.pixels=pixels;Spawns=Array.AsReadOnly(spawns);SpawnMarkers=Array.AsReadOnly(markers);}
    // Image.CreateFromData expects an owned array. Never expose the cached mutable buffer.
    public byte[] CopyRgbaPixels()=>(byte[])pixels.Clone();
}

/// <summary>
/// Shared six-seed cache survives front-page recreation. All map generation, terrain color
/// selection, and pixel packing happen on background threads without any Godot dependency.
/// Player count affects only marker drawing; it does not invalidate terrain pixels.
/// </summary>
public static class MenuMapPreviewCache
{
    public const int Capacity=6;
    static readonly BackgroundSeedCache<MenuMapPreviewData> Cache=new(Generate,Capacity);
    public static int CachedSeedCount=>Cache.CachedSeedCount;
    public static Task<MenuMapPreviewData> GetAsync(uint seed)=>Cache.GetAsync(seed);

    static MenuMapPreviewData Generate(uint seed)
    {
        // Keep terrain generation in the simulation model; the menu does not duplicate its noise/rules.
        var map=new BattleMap(MatchSettings.Practice(2,seed));
        var rgba=new byte[checked(map.Width*map.Height*4)];var projection=new MapPreviewProjection(map);
        for(var py=0;py<map.Height;py++)for(var px=0;px<map.Width;px++)
        {
            var cell=projection.Unproject((px+.5f)/map.Width,(py+.5f)/map.Height);
            var x=(int)MathF.Floor(cell.X);var y=(int)MathF.Floor(cell.Y);var playable=map.IsPlayable(x,y);
            var ground=MapPreviewProjection.BackingTerrain(map,x,y,seed);
            var (r,g,b)=playable && map.Ore[map.Index(x,y)]>0?(0xda,0xb5,0x42):map.HasBridge(x,y)?(0xb9,0xa4,0x77):ground switch
            {
                Terrain.Water=>(0x24,0x4f,0x75),Terrain.Rock=>(0x66,0x60,0x4d),
                Terrain.Dirt=>(0xb2,0xa7,0x6c),_=>(0x7d,0x8c,0x4b)
            };
            var heightShade=Math.Clamp(80+map.ElevationAt(x,y)*6,80,100);r=r*heightShade/100;g=g*heightShade/100;b=b*heightShade/100;
            if(!playable){r=r*55/100;g=g*55/100;b=b*55/100;}
            var pixel=(py*map.Width+px)*4;rgba[pixel]=(byte)r;rgba[pixel+1]=(byte)g;rgba[pixel+2]=(byte)b;rgba[pixel+3]=255;
        }
        var markers=map.Spawns.Select(s=>projection.Project(s.X+1,s.Y+1)).ToArray();
        return new MenuMapPreviewData(seed,map.Width,map.Height,rgba,(Cell[])map.Spawns.Clone(),markers);
    }
}
