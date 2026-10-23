using Godot;
using RedAlert.Simulation;

namespace RedAlert;

public partial class Battlefield
{
    const int OriginalObjectDepthSentinel=int.MinValue;
    readonly List<(ClassicMapObjectPlacement Object,Vector2 Point,Rect2 Bounds,Color Light,float Height,float Depth,int ObjectIndex)> visibleOriginalObjects=[];
    public int OriginalObjectSpritesDrawn {get;private set;}
    public int OriginalObjectDrawFailures {get;private set;}
    public int VisibleOriginalObjects=>visibleOriginalObjects.Count;
    void QueueOriginalMapObjects(World world)
    {
        visibleOriginalObjects.Clear();OriginalObjectSpritesDrawn=0;OriginalObjectDrawFailures=0;
        var viewport=new Rect2(Vector2.Zero,Size).Grow(15*Zoom);
        for(var objectIndex=0;objectIndex<world.Map.OriginalObjects.Count;objectIndex++)
        {
            var item=world.Map.OriginalObjects[objectIndex];
            if(world.Map.IsOriginalObjectDestroyed(objectIndex) || !world.Map.Inside(item.X,item.Y))continue;
            var structure=item.Definition.Kind==ClassicMapObjectKind.Structure;
            var anchor=new Vector2(item.X+(structure?item.Width*.5f:.5f),item.Y+(structure?item.Height*.5f:.5f));
            var cellX=Math.Clamp((int)anchor.X,0,world.Map.Width-1);var cellY=Math.Clamp((int)anchor.Y,0,world.Map.Height-1);
            var playable=world.Map.IsPlayable(cellX,cellY);if(playable && !Controller.ViewExplored(cellX,cellY))continue;
            var point=ProjectGround(anchor);var bounds=Controller.Graphics.OriginalMapObjectBounds(item.Source,point,Zoom);
            if(!bounds.Intersects(viewport))continue;
            var light=playable?Controller.ViewVisible(cellX,cellY)?Colors.White:new Color(.32f,.32f,.32f):new Color(.6f,.6f,.6f);
            var height=(float)world.Map.SurfaceHeightAt(anchor.X,anchor.Y);var depth=item.X+item.Y+(structure?item.Width+item.Height-1:1);
            visibleOriginalObjects.Add((item,point,bounds,light,height,depth,objectIndex));
            if(item.Definition.Kind!=ClassicMapObjectKind.Smudge)depthItems.Add((depth*Rules.Scale,-2-visibleOriginalObjects.Count,null,visibleOriginalObjects.Count-1,OriginalObjectDepthSentinel));
        }
    }
    public bool EntityDrawsAfterOriginalStructure(int entityId,string name)
    {
        var entity=depthItems.FindIndex(item=>item.Entity?.Id==entityId);
        var structure=depthItems.FindIndex(item=>item.Y==OriginalObjectDepthSentinel && visibleOriginalObjects[item.X].Object.Name==name);
        return entity>=0 && structure>=0 && entity>structure;
    }
    void DrawOriginalMapSmudges()
    {
        for(var i=0;i<visibleOriginalObjects.Count;i++)if(visibleOriginalObjects[i].Object.Definition.Kind==ClassicMapObjectKind.Smudge)DrawOriginalMapObjectAt(i);
    }
    void DrawOriginalMapObjectAt(int index)
    {
        var item=visibleOriginalObjects[index];
        if(Controller.Battle!.Map.IsOriginalObjectDestroyed(item.ObjectIndex))return;
        if(Controller.Graphics.DrawOriginalMapObject(this,item.Object.Source,item.Point,Zoom,item.Light,Controller.Battle!.Tick))OriginalObjectSpritesDrawn++;
        else OriginalObjectDrawFailures++;
        if(item.Object.Definition.Kind!=ClassicMapObjectKind.Smudge)
            DrawOriginalMapForegroundAt(Controller.Battle!,item.Object.X,item.Object.Y,item.Height,item.Depth,item.Bounds,false);
    }
}
