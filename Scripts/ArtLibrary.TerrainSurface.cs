using Godot;

namespace RedAlert;

public sealed partial class ArtLibrary
{
    readonly Vector2[] terrainSurfaceUvs=new Vector2[4];
    readonly Color[] terrainSurfaceColors=new Color[1];

    // Corners are projected tile-grid NW, NE, SE, SW (diamond top, right,
    // bottom, left) at their ground heights. The caller's array is not changed.
    public bool DrawTerrainSurface(CanvasItem target,string template,int frame,Vector2[] corners,Color light)
    {
        if(corners.Length!=4 || !LoadGround() || !terrainTemplates.TryGetValue((template,frame),out var region))return false;
        return DrawSurfaceRegion(target,region,corners,light);
    }

    public bool DrawGroundSurface(CanvasItem target,string kind,int variation,Vector2[] corners,Color light)
    {
        if(corners.Length!=4 || !LoadGround())return false;
        if(!groundRegions.TryGetValue(kind,out var regions) || regions.Length==0)
            if(!groundRegions.TryGetValue("clear",out regions) || regions.Length==0)return false;
        return DrawSurfaceRegion(target,regions[(variation&int.MaxValue)%regions.Length],corners,light);
    }

    bool DrawSurfaceRegion(CanvasItem target,Rect2 region,Vector2[] corners,Color light)
    {
        var atlas=groundAtlas!;
        var width=(float)atlas.GetWidth();
        var height=(float)atlas.GetHeight();
        var left=region.Position.X/width;
        var top=region.Position.Y/height;
        var right=region.End.X/width;
        var bottom=region.End.Y/height;
        var middleX=(left+right)*.5f;
        var middleY=(top+bottom)*.5f;
        terrainSurfaceUvs[0]=new Vector2(middleX,top);
        terrainSurfaceUvs[1]=new Vector2(right,middleY);
        terrainSurfaceUvs[2]=new Vector2(middleX,bottom);
        terrainSurfaceUvs[3]=new Vector2(left,middleY);
        // TMP boundary pixels include transparent coverage. The exact same shared
        // quad gets an opaque original-color base; art alpha and UVs stay intact.
        target.DrawColoredPolygon(corners,OpaqueGroundUnderfill(groundUnderfills[region],light));
        terrainSurfaceColors[0]=light;
        target.DrawPolygon(corners,terrainSurfaceColors,terrainSurfaceUvs,atlas);
        return true;
    }
}
