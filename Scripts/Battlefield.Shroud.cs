using Godot;
using RedAlert.Simulation;
using System.Text.Json;

namespace RedAlert;

public partial class Battlefield
{
    ImageTexture? originalShroudTexture;
    readonly Dictionary<int,Rect2> originalShroudRegions=[];
    readonly Dictionary<int,int> originalShroudFrames=[];
    readonly List<(ClassicMapCell Cell,Vector2 Point,Color Light,Rect2 Bounds)> originalShroudGroundTiles=[],originalShroudGroundPass=[];
    readonly Dictionary<int,ClassicMapCell?> shroudBridgeSubstrates=[];
    World? shroudGroundWorld;
    int originalShroudEdges,originalShroudFull,originalShroudWarped,originalShroudDegenerate,knownTerrainDarkened,shroudGroundFringeCells;
    string originalShroudSha="";
    const string ShroudFolder="res://Assets/Generated/Shroud/";
    static readonly (int X,int Y,int Bit)[] ShroudCorners=[(-1,-1,1),(1,-1,2),(1,1,4),(-1,1,8)];
    public override void _ExitTree(){originalShroudTexture?.Dispose();originalShroudTexture=null;originalShroudRegions.Clear();originalShroudFrames.Clear();}
    void BeginShroudFrame()
    {
        originalShroudEdges=0;originalShroudFull=0;originalShroudWarped=0;originalShroudDegenerate=0;knownTerrainDarkened=0;shroudGroundFringeCells=0;
        originalShroudGroundTiles.Clear();originalShroudGroundPass.Clear();
        if(!ReferenceEquals(shroudGroundWorld,Controller.Battle)){shroudGroundWorld=Controller.Battle;shroudBridgeSubstrates.Clear();}
    }
    // This private queue participates in the original TMP ground pass only.
    // Keeping it out of originalVisibleTiles prevents unknown fringe from
    // entering resource, rock, bridge, foreground or terrain-target consumers.
    IReadOnlyList<(ClassicMapCell Cell,Vector2 Point,Color Light,Rect2 Bounds)> OriginalGroundWithShroudFringe()
    {
        if(originalShroudGroundTiles.Count==0)return originalVisibleTiles;
        originalShroudGroundPass.Clear();originalShroudGroundPass.AddRange(originalVisibleTiles);originalShroudGroundPass.AddRange(originalShroudGroundTiles);
        originalShroudGroundPass.Sort(static(a,b)=>a.Cell.X+a.Cell.Y!=b.Cell.X+b.Cell.Y?(a.Cell.X+a.Cell.Y).CompareTo(b.Cell.X+b.Cell.Y):a.Cell.X.CompareTo(b.Cell.X));
        return originalShroudGroundPass;
    }
    ClassicMapCell? ShroudFringeOriginalGroundCell(World world,ClassicMapCell cell)
    {
        if(!cell.Bridge)return cell;
        var map=world.Map;var index=map.Index(cell.X,cell.Y);if(shroudBridgeSubstrates.TryGetValue(index,out var cached))return cached;
        // Some overpass TMPs bake the bridge into their extra pixels. An unknown
        // fringe must use actual substrate TMP data rather than reveal that deck.
        var ground=map.Ground[index];var level=map.ElevationAt(cell.X,cell.Y);
        var under=map.OriginalDefinition!.RuntimeCells.FirstOrDefault(candidate=>!candidate.Bridge && candidate.RampType==0
            && candidate.Ground==ground && candidate.Level==level && !candidate.Template.StartsWith("ovrps",StringComparison.OrdinalIgnoreCase)
            && !Controller.Graphics.OriginalMapTileHasExtra(candidate));
        ClassicMapCell? result=under is null?null:new ClassicMapCell
        {
            X=cell.X,Y=cell.Y,SourceX=cell.SourceX,SourceY=cell.SourceY,Playable=cell.Playable,
            TileIndex=under.TileIndex,SubTile=under.SubTile,Level=level,SurfaceLevel=level,TileSetId=under.TileSetId,
            Template=under.Template,TemplateHeight=under.TemplateHeight,TerrainType=under.TerrainType,Ground=ground,UnderlyingGround=ground,
            LandPassable=map.Walkable(cell.X,cell.Y),WaterPassable=map.Navigable(cell.X,cell.Y,MovementDomain.Water),Buildable=false,Bridge=false,HighBridge=false,OverlayIndex=255
        };
        shroudBridgeSubstrates[index]=result;return result;
    }
    bool ShroudGroundFringe(int x,int y)
    {
        var mask=OriginalShroudMaskAt(x,y);return mask!=0 && mask!=255;
    }
    void LoadOriginalShroud()
    {
        if(originalShroudTexture is not null)return;
        using var document=JsonDocument.Parse(Godot.FileAccess.GetFileAsString(ShroudFolder+"manifest.json"));var root=document.RootElement;
        if(root.GetProperty("canvasWidth").GetInt32()!=60 || root.GetProperty("canvasHeight").GetInt32()!=30)throw new InvalidDataException("原版迷雾画布尺寸不匹配");
        var sequence=root.GetProperty("sequences").GetProperty("shroud");originalShroudSha=sequence.GetProperty("sourceSha256").GetString()??"";
        if(sequence.GetProperty("sourceFrameCount").GetInt32()!=96 || sequence.GetProperty("storedRasterFrames").GetInt32()!=48)throw new InvalidDataException("原版迷雾帧数不匹配");
        foreach(var frame in sequence.GetProperty("frames").EnumerateArray())
        {
            var index=frame.GetProperty("index").GetInt32();if(!frame.GetProperty("hasData").GetBoolean() || index==47)continue;
            var mask=frame.GetProperty("mask").GetInt32();var region=frame.GetProperty("region");
            originalShroudRegions.Add(mask,new Rect2(region.GetProperty("x").GetSingle(),region.GetProperty("y").GetSingle(),60,30));originalShroudFrames.Add(mask,index);
        }
        using var image=Image.LoadFromFile(ProjectSettings.GlobalizePath(ShroudFolder+root.GetProperty("atlas").GetString()));originalShroudTexture=ImageTexture.CreateFromImage(image);
    }
    static int CanonicalShroudMask(int mask)
    {
        // These directions describe TRANSPARENCY toward revealed neighbors,
        // not the opaque side: edges16/32/64/128 are upper-right/lower-right/
        // lower-left/upper-left. Each includes its two transparent endpoint corners.
        if((mask&16)!=0)mask&=~3;if((mask&32)!=0)mask&=~6;if((mask&64)!=0)mask&=~12;if((mask&128)!=0)mask&=~9;
        return mask;
    }
    public int OriginalShroudMaskAt(int x,int y)
    {
        var world=Controller.Battle;if(world is null || !world.Map.IsPlayable(x,y) || Controller.IsSpectating)return 0;
        if(Controller.ViewExplored(x,y))return 0;
        var mask=0;
        for(var i=0;i<FogEdges.Length;i++)
        {
            var edge=FogEdges[i];var nx=x+edge.X;var ny=y+edge.Y;
            if(world.Map.IsPlayable(nx,ny) && Controller.ViewExplored(nx,ny))mask|=16<<i;
        }
        foreach(var corner in ShroudCorners)
        {
            var nx=x+corner.X;var ny=y+corner.Y;
            if(world.Map.IsPlayable(nx,ny) && Controller.ViewExplored(nx,ny))mask|=corner.Bit;
        }
        return mask==0?255:CanonicalShroudMask(mask);
    }
    void DrawOriginalShroudCell(int x,int y,int mask,Vector2 center)
    {
        if(!originalShroudRegions.TryGetValue(mask,out var region))throw new InvalidDataException("原版迷雾邻域帧缺失："+mask);
        var corners=GroundFootprint(x,y,1,1);var top=corners[0];var right=corners[1];var bottom=corners[2];var left=corners[3];
        var half=new Vector2(30,15)*Zoom;
        if(top.DistanceSquaredTo(center-new Vector2(0,half.Y))<.001f && right.DistanceSquaredTo(center+new Vector2(half.X,0))<.001f
            && bottom.DistanceSquaredTo(center+new Vector2(0,half.Y))<.001f && left.DistanceSquaredTo(center-new Vector2(half.X,0))<.001f)
            DrawTextureRectRegion(originalShroudTexture!,new Rect2(center-half,half*2),region);
        else
        {
            // Map each original quarter onto the real terrain diamond. Ramps and
            // height breaks retain their actual projected corners; no screen-grid
            // stair polygon or resized replacement edge is manufactured.
            var size=originalShroudTexture!.GetSize();var uv=region.Position/size;var uvHalf=region.Size/size*.5f;
            bool Patch(Vector2[] points,Vector2 offset)
            {
                var area=0f;for(var i=0;i<points.Length;i++)area+=points[i].Cross(points[(i+1)%points.Length]);
                // Original ramp13 projects edge-on at this camera angle. A line
                // has no shroud area and cannot be triangulated as a polygon.
                if(MathF.Abs(area)<.01f){originalShroudDegenerate++;return false;}
                DrawPolygon(points,new Color[]{Colors.White},
                    new Vector2[]{uv+offset,uv+offset+new Vector2(uvHalf.X,0),uv+offset+uvHalf,uv+offset+new Vector2(0,uvHalf.Y)},originalShroudTexture);
                return true;
            }
            var drawn=Patch([top+left-center,top,center,left],Vector2.Zero);
            drawn|=Patch([top,top+right-center,right,center],new(uvHalf.X,0));
            drawn|=Patch([center,right,bottom+right-center,bottom],uvHalf);
            drawn|=Patch([left,center,bottom,bottom+left-center],new(0,uvHalf.Y));
            if(!drawn)return;originalShroudWarped++;
        }
        if(mask==255)originalShroudFull++;else originalShroudEdges++;
    }
    void DrawOriginalShroud(World world)
    {
        if(Controller.IsSpectating)return;LoadOriginalShroud();
        for(var y=0;y<world.Map.Height;y++)for(var x=0;x<world.Map.Width;x++)
        {
            if(!world.Map.IsPlayable(x,y))continue;
            var center=ProjectGround(new Vector2(x+.5f,y+.5f));
            if(center.X < -100 || center.Y < -150 || center.X>Size.X+100 || center.Y>Size.Y+150)continue;
            var mask=OriginalShroudMaskAt(x,y);if(mask!=0)DrawOriginalShroudCell(x,y,mask,center);
        }
    }
    public Godot.Collections.Dictionary GetClassicShroudRenderAudit()=>new(){["sourceFrames"]=96,["storedRasterFrames"]=48,
        ["sha256"]=originalShroudSha,["edgeMasks"]=originalShroudEdges,["fullMasks"]=originalShroudFull,["terrainWarpedMasks"]=originalShroudWarped,
        ["degenerateQuartersSkipped"]=originalShroudDegenerate,["knownTerrainDarkened"]=knownTerrainDarkened,["fogLayerDrawn"]=false,["textureLoaded"]=originalShroudTexture is not null,
        ["maskPlacement"]="unknown-cell-known-neighbors",["groundFringeCells"]=shroudGroundFringeCells,["originalGroundFringeTiles"]=originalShroudGroundTiles.Count,
        ["unexploredResourceCellsDrawn"]=Controller.Battle is {} oreWorld?drawnResourceCells.Keys.Count(index=>!Controller.ViewExplored(index%oreWorld.Map.Width,index/oreWorld.Map.Width)):0,
        ["unexploredActorTerrainTilesQueued"]=Controller.Battle is {} terrainWorld?originalVisibleTiles.Count(tile=>terrainWorld.Map.IsPlayable(tile.Cell.X,tile.Cell.Y) && !Controller.ViewExplored(tile.Cell.X,tile.Cell.Y)):0,
        ["unexploredVerticalFacesQueued"]=Controller.Battle is {} faceWorld?landscapeTiles.Where(tile=>faceWorld.Map.IsPlayable(tile.X,tile.Y) && !Controller.ViewExplored(tile.X,tile.Y)).Sum(tile=>tile.FaceCount):0,
        ["fringeOriginalExtrasEnabled"]=false,
        ["alphaTransferVerifiedNative"]=false,["alphaPolicy"]="Original indices preserved; 254 transparent, 0..127 normalized to 0..255 black opacity. Numeric native palette unverified."};
}
