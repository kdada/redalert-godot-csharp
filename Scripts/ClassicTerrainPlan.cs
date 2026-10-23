using RedAlert.Simulation;

namespace RedAlert;

// Presentation-only map. It never writes simulation ground, ore, pathing or visibility.
public sealed class ClassicTerrainPlan
{
    public readonly record struct Tile(string Template,int Frame);
    readonly Tile[] tiles;
    readonly int width;
    public int TransitionCells {get;private set;}
    public int ShoreCells {get;private set;}
    public int ThickCells {get;private set;}
    public int RoughCells {get;private set;}
    public int SandCells {get;private set;}
    public Tile this[int x,int y]=>tiles[y*width+x];
    public sealed record Shore(string Template,int[] Frames);

    public ClassicTerrainPlan(BattleMap map,uint seed,int[] latByMask,Shore[] shoreByMask)
    {
        if(latByMask.Length!=16 || shoreByMask.Length!=16 || latByMask.Distinct().Count()!=16)
            throw new InvalidDataException("原版地表连接表必须覆盖16种相邻关系");
        width=map.Width;tiles=new Tile[width*map.Height];
        var surfaces=new byte[tiles.Length];
        for(var y=0;y<map.Height;y++)for(var x=0;x<width;x++)
        {
            var index=map.Index(x,y);
            if(map.Ground[index]==Terrain.Water)continue;
            // Broad, continuous patches replace rectangular farm templates and single-cell noise.
            var thick=Field(x,y,7,seed^0x1C3945U);
            var worn=Field(x,y,11,seed^0x982A73U);
            var oreArea=map.Spawns.Any(s=>Math.Abs(x-(s.X+9))<=3 && Math.Abs(y-(s.Y+7))<=3)
                || x is >=42 and <=53 && y is >=42 and <=53;
            var coastal=false;
            for(var dy=-2;dy<=2 && !coastal;dy++)for(var dx=-2;dx<=2;dx++)
                if(map.Inside(x+dx,y+dy) && map.Ground[map.Index(x+dx,y+dy)]==Terrain.Water){coastal=true;break;}
            // Original shore's dry half is sand (green/glat), not clear grass.
            // Keep a sand collar under it, then let GLAT blend back into inland clear.
            surfaces[index]=(byte)(coastal?3:oreArea || worn>.72f?2:thick>.52f?1:0);
        }
        byte Surface(int x,int y)=>map.Inside(x,y)?surfaces[map.Index(x,y)]:(byte)0;
        for(var y=0;y<map.Height;y++)for(var x=0;x<width;x++)
        {
            var index=map.Index(x,y);var variation=(int)(BattleMap.Noise(x,y,seed)&7);
            if(map.Ground[index]==Terrain.Water)
            {
                tiles[index]=new Tile("water01",(y&1)*2+(x&1));continue;
            }
            var surface=surfaces[index];
            if(surface==0){tiles[index]=new Tile(Variant("clear",variation),0);continue;}
            var mask=(Surface(x-1,y)==surface?1:0)|(Surface(x,y-1)==surface?2:0)
                |(Surface(x+1,y)==surface?4:0)|(Surface(x,y+1)==surface?8:0);
            var solid=surface==1?"ruff":surface==2?"sandy":"green";
            var transition=surface==1?"clat":surface==2?"dlat":"glat";
            if(mask==15)tiles[index]=new Tile(Variant(solid,variation),0);
            else
            {
                tiles[index]=new Tile($"{transition}{latByMask[mask]:00}",0);TransitionCells++;
            }
            if(surface==1)ThickCells++;else if(surface==2)RoughCells++;else SandCells++;
        }
        // A complete source template spans two cells. Adjacent patches must use
        // the same wet samples on their shared corner grid nodes. Sampling each
        // patch's four interior cells leaves hard water-to-sand cuts on UV coasts.
        for(var top=-1;top<map.Height;top+=2)for(var left=-1;left<width;left+=2)
        {
            var mask=0;
            for(var dy=0;dy<2;dy++)for(var dx=0;dx<2;dx++)
                if(!map.Inside(left+2*dx,top+2*dy) || map.Ground[map.Index(left+2*dx,top+2*dy)]==Terrain.Water)mask|=1<<(dy*2+dx);
            if(mask is 0 or 15)continue;
            var shore=shoreByMask[mask];
            if(shore.Frames.Length!=4)throw new InvalidDataException($"岸线关系{mask}没有匹配的原版完整模板，不能用不相关弯岸替代");
            for(var dy=0;dy<2;dy++)for(var dx=0;dx<2;dx++)
            {
                var x=left+dx;var y=top+dy;if(!map.Inside(x,y))continue;
                tiles[map.Index(x,y)]=new Tile(shore.Template,shore.Frames[dy*2+dx]);ShoreCells++;
            }
        }
    }
    static string Variant(string prefix,int variation)=>prefix+"01"+(variation==0?"":((char)('a'+variation-1)).ToString());
    static float Field(int x,int y,int size,uint seed)
    {
        var gx=(int)MathF.Floor(x/(float)size);var gy=(int)MathF.Floor(y/(float)size);
        var tx=x/(float)size-gx;var ty=y/(float)size-gy;
        tx=tx*tx*(3-2*tx);ty=ty*ty*(3-2*ty);
        float Noise(int px,int py)=>(BattleMap.Noise(px,py,seed)&65535)/65535f;
        var a=Noise(gx,gy)*(1-tx)+Noise(gx+1,gy)*tx;
        var b=Noise(gx,gy+1)*(1-tx)+Noise(gx+1,gy+1)*tx;
        return a*(1-ty)+b*ty;
    }
}
