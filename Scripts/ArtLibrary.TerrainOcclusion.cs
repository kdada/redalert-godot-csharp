using Godot;

namespace RedAlert;

public sealed partial class ArtLibrary
{
    struct TerrainClipVertex
    {
        public Vector2 Point,Uv;
        public float Depth,Height;
        public TerrainClipVertex Lerp(TerrainClipVertex b,float t)=>new(){Point=Point.Lerp(b.Point,t),Uv=Uv.Lerp(b.Uv,t),Depth=Mathf.Lerp(Depth,b.Depth,t),Height=Mathf.Lerp(Height,b.Height,t)};
    }
    readonly TerrainClipVertex[] terrainClipA=new TerrainClipVertex[8],terrainClipB=new TerrainClipVertex[8];
    readonly Vector2[][] terrainClipPoints=Enumerable.Range(0,9).Select(n=>new Vector2[n]).ToArray();
    readonly Vector2[][] terrainClipUvs=Enumerable.Range(0,9).Select(n=>new Vector2[n]).ToArray();
    readonly Color[] terrainClipColor=new Color[1];
    static int ClipTerrain(TerrainClipVertex[] source,int count,TerrainClipVertex[] output,float threshold,bool height)
    {
        if(count==0)return 0;
        var written=0;var a=source[count-1];var av=(height?a.Height:a.Depth)-threshold;
        for(var i=0;i<count;i++)
        {
            var b=source[i];var bv=(height?b.Height:b.Depth)-threshold;
            if((av>=0)!=(bv>=0))output[written++]=a.Lerp(b,av/(av-bv));
            if(bv>=0)output[written++]=b;
            a=b;av=bv;
        }
        return written;
    }
    // A higher foreground top may hide a lower actor, but the actor's own flat
    // tile must never be repainted over it. The caller excludes its continuous
    // support surface; these two geometric clips replace tile-centroid sorting.
    public bool DrawTerrainOccluder(CanvasItem target,string template,int frame,Vector2[] corners,float[] depths,float[] heights,float actorDepth,float actorHeight,Color light)
    {
        if(!LoadGround() || groundAtlas is null || !terrainTemplates.TryGetValue((template,frame),out var region))return false;
        var size=groundAtlas.GetSize();var half=region.Size*.5f;
        terrainClipA[0]=new(){Point=corners[0],Uv=(region.Position+new Vector2(half.X,0))/size,Depth=depths[0],Height=heights[0]};
        terrainClipA[1]=new(){Point=corners[1],Uv=(region.Position+new Vector2(region.Size.X,half.Y))/size,Depth=depths[1],Height=heights[1]};
        terrainClipA[2]=new(){Point=corners[2],Uv=(region.Position+new Vector2(half.X,region.Size.Y))/size,Depth=depths[2],Height=heights[2]};
        terrainClipA[3]=new(){Point=corners[3],Uv=(region.Position+new Vector2(0,half.Y))/size,Depth=depths[3],Height=heights[3]};
        var count=ClipTerrain(terrainClipA,4,terrainClipB,actorDepth+.0001f,false);
        count=ClipTerrain(terrainClipB,count,terrainClipA,actorHeight+.001f,true);
        if(count<3)return false;
        var points=terrainClipPoints[count];var uvs=terrainClipUvs[count];
        for(var i=0;i<count;i++){points[i]=terrainClipA[i].Point;uvs[i]=terrainClipA[i].Uv;}
        target.DrawColoredPolygon(points,OpaqueGroundUnderfill(terrainUnderfills[(template,frame)],light));
        terrainClipColor[0]=light;target.DrawPolygon(points,terrainClipColor,uvs,groundAtlas);return true;
    }
}
