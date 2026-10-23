using Godot;
using RedAlert.Simulation;

namespace RedAlert;

public partial class Battlefield
{
    const int OriginalRockDepthSentinel=int.MinValue+1;
    readonly List<(ClassicMapCell Cell,Vector2 Point,Rect2 Bounds,Color Light)> visibleOriginalRocks=[];
    public int OriginalRockSpritesDrawn {get;private set;}
    public int OriginalRockDrawFailures {get;private set;}
    void QueueOriginalMapRocks(World world)
    {
        visibleOriginalRocks.Clear();OriginalRockSpritesDrawn=0;OriginalRockDrawFailures=0;
        var viewport=new Rect2(Vector2.Zero,Size).Grow(15*Zoom);
        foreach(var tile in originalVisibleTiles)
        {
            var cell=tile.Cell;if(!cell.OverlayName.StartsWith("TROCK",StringComparison.Ordinal) && !cell.OverlayName.StartsWith("SROCK",StringComparison.Ordinal) && cell.OverlayName!="RUBBLE_OVERLAY")continue;
            var point=ProjectGround(new Vector2(cell.X+.5f,cell.Y+.5f));var bounds=Controller.Graphics.OriginalMapRockOverlayBounds(cell,point,Zoom);
            if(!bounds.Intersects(viewport))continue;
            visibleOriginalRocks.Add((cell,point,bounds,tile.Light));
            if(cell.OverlayName!="RUBBLE_OVERLAY")depthItems.Add(((cell.X+cell.Y+1)*Rules.Scale,-2000-visibleOriginalRocks.Count,null,visibleOriginalRocks.Count-1,OriginalRockDepthSentinel));
        }
    }
    void DrawOriginalMapRubble()
    {
        for(var index=0;index<visibleOriginalRocks.Count;index++)if(visibleOriginalRocks[index].Cell.OverlayName=="RUBBLE_OVERLAY")DrawOriginalMapRockAt(index);
    }
    void DrawOriginalMapRockAt(int index)
    {
        var rock=visibleOriginalRocks[index];
        if(Controller.Graphics.DrawOriginalMapRockOverlay(this,rock.Cell,rock.Point,Zoom,rock.Light))OriginalRockSpritesDrawn++;
        else OriginalRockDrawFailures++;
        if(rock.Cell.OverlayName!="RUBBLE_OVERLAY")DrawOriginalMapForegroundAt(Controller.Battle!,rock.Cell.X,rock.Cell.Y,rock.Cell.SurfaceLevel,rock.Cell.X+rock.Cell.Y+1,rock.Bounds,false);
    }
}
