using Godot;
using RedAlert.Simulation;
using System.Text.Json;
using GFile=Godot.FileAccess;

namespace RedAlert;

public sealed partial class ArtLibrary
{
    public sealed record OriginalBridgePiece(string Template,int Frame);
    public sealed record OriginalLowBridgeLayout(int CrossLaneCount,int DrawCrossLane,OriginalBridgePiece Start,OriginalBridgePiece[] Middle,OriginalBridgePiece End);
    sealed class OriginalTerrainLayer
    {
        public required PixelAtlas Pixels;
        public required Rect2 Region;
        public required Vector2 Offset;
        public int Frame,CellX,CellY;
        public bool Extra;
    }
    sealed class OriginalTerrainTemplate
    {
        public int Width,Height;
        public readonly List<OriginalTerrainLayer> Layers=[],Foreground=[];
    }
    readonly Dictionary<string,OriginalTerrainTemplate> originalTerrainTemplates=[];
    readonly Dictionary<string,PixelAtlas> originalTerrainImages=[];
    readonly Dictionary<BridgeAxis,OriginalLowBridgeLayout> originalLowBridges=[];
    bool originalTerrainChecked;
    const string OriginalTerrainFolder="res://Assets/Generated/Terrain/OriginalStructures/";
    static Vector2 OriginalOffset(JsonElement value)=>new(value.GetProperty("x").GetSingle(),value.GetProperty("y").GetSingle());
    static OriginalBridgePiece OriginalBridgeSelection(JsonElement value)=>new(value.GetProperty("template").GetString()!,value.GetProperty("frame").GetInt32());
    PixelAtlas OriginalTerrainPixels(string path)
    {
        if(originalTerrainImages.TryGetValue(path,out var cached))return cached;
        cached=ReadPixels(path);originalTerrainImages.Add(path,cached);
        // Existing ArtLibrary texture cache owns upload and disposal.
        sprites.Add("$original-terrain/"+path,new SpriteAtlas{Width=cached.Width,Height=cached.Height,Frames=[],Pixels=cached});
        return cached;
    }
    bool LoadOriginalTerrain()
    {
        if(originalTerrainChecked)return originalTerrainTemplates.Count>0;
        originalTerrainChecked=true;
        if(!GFile.FileExists(OriginalTerrainFolder+"catalog.json"))return false;
        using var document=JsonDocument.Parse(GFile.GetFileAsString(OriginalTerrainFolder+"catalog.json"));
        var root=document.RootElement;var atlas=OriginalTerrainPixels(OriginalTerrainFolder+root.GetProperty("atlas").GetString());
        foreach(var item in root.GetProperty("templates").EnumerateArray())
        {
            var name=item.GetProperty("id").GetString()!;var isSprite=item.GetProperty("format").GetString()=="shp";
            var template=new OriginalTerrainTemplate{Width=isSprite?1:item.GetProperty("blocksX").GetInt32(),Height=isSprite?1:item.GetProperty("blocksY").GetInt32()};
            var anchor=isSprite?OriginalOffset(item.GetProperty("pixelFootAnchorFromTileOrigin")):Vector2.Zero;
            var maximum=isSprite?0:item.GetProperty("maxHeight").GetInt32();
            foreach(var frame in item.GetProperty("frames").EnumerateArray())
            {
                var index=frame.GetProperty("index").GetInt32();
                var cellX=isSprite?0:frame.GetProperty("cellX").GetInt32();var cellY=isSprite?0:frame.GetProperty("cellY").GetInt32();
                // Raw TMP maxHeight adjustment is 15 native pixels per source
                // level. It is never converted using simulation landscape rise.
                var adjustment=isSprite?-anchor:new Vector2(-30,(maximum-frame.GetProperty("height").GetInt32())*15);
                foreach(var layer in frame.GetProperty("layers").EnumerateArray())
                {
                    var region=Rect(layer.GetProperty("region"));if(region.Size.X<=0 || region.Size.Y<=0)continue;
                    template.Layers.Add(new OriginalTerrainLayer{Pixels=atlas,Region=region,Offset=OriginalOffset(layer.GetProperty("offset"))+adjustment,Frame=index,CellX=cellX,CellY=cellY,Extra=layer.GetProperty("kind").GetString()=="extra"});
                }
                if(frame.TryGetProperty("foregroundLayer",out var foreground) && foreground.ValueKind==JsonValueKind.Object)
                    template.Foreground.Add(new OriginalTerrainLayer{Pixels=atlas,Region=Rect(foreground.GetProperty("region")),Offset=OriginalOffset(foreground.GetProperty("offset"))+adjustment,Frame=index,CellX=cellX,CellY=cellY});
            }
            template.Layers.Sort(static(a,b)=>a.CellX+a.CellY!=b.CellX+b.CellY?(a.CellX+a.CellY).CompareTo(b.CellX+b.CellY):a.Extra.CompareTo(b.Extra));
            originalTerrainTemplates.Add(name,template);
        }
        if(root.TryGetProperty("selection",out var selection) && selection.TryGetProperty("lowBridge",out var lowBridge))
        foreach(var choice in lowBridge.EnumerateObject())
        {
            if(!Enum.TryParse<BridgeAxis>(choice.Name,out var axis))continue;
            var value=choice.Value;
            originalLowBridges.Add(axis,new OriginalLowBridgeLayout(value.GetProperty("crossLaneCount").GetInt32(),value.GetProperty("drawCrossLane").GetInt32(),OriginalBridgeSelection(value.GetProperty("start")),value.GetProperty("middle").EnumerateArray().Select(OriginalBridgeSelection).ToArray(),OriginalBridgeSelection(value.GetProperty("end"))));
        }
        return originalTerrainTemplates.Count>0;
    }
    public OriginalLowBridgeLayout? OriginalLowBridge(BridgeAxis axis)=>LoadOriginalTerrain()?originalLowBridges.GetValueOrDefault(axis):null;
    public Rect2 OriginalTerrainBounds(string name,int frame,Vector2 tileOrigin,float zoom)
    {
        var bounds=new Rect2(tileOrigin,Vector2.Zero);var first=true;
        if(!LoadOriginalTerrain() || !originalTerrainTemplates.TryGetValue(name,out var template))return bounds;
        foreach(var layer in template.Layers)
        {
            if(frame>=0 && layer.Frame!=frame)continue;
            var rectangle=new Rect2(tileOrigin+layer.Offset*zoom,layer.Region.Size*zoom);
            bounds=first?rectangle:bounds.Merge(rectangle);first=false;
        }
        return bounds;
    }
    public bool DrawOriginalTerrain(CanvasItem target,string name,int frame,Vector2 tileOrigin,float zoom,Color light)
    {
        if(!LoadOriginalTerrain() || !originalTerrainTemplates.TryGetValue(name,out var template))return false;
        var drawn=false;
        foreach(var layer in template.Layers)
        {
            if(frame>=0 && layer.Frame!=frame)continue;
            target.DrawTextureRectRegion(Texture(layer.Pixels,-1),new Rect2(tileOrigin+layer.Offset*zoom,layer.Region.Size*zoom),layer.Region,light);drawn=true;
        }
        return drawn;
    }
    // Only the retained near-edge timber mask is repainted over an actor. A
    // horizontal UV crop follows that edge's world-depth plane; no road surface
    // is repainted and no new rail pixels are synthesized.
    public bool DrawOriginalTerrainForeground(CanvasItem target,string name,int frame,Vector2 tileOrigin,float zoom,Color light,float cutX,bool keepRight)
    {
        if(!LoadOriginalTerrain() || !originalTerrainTemplates.TryGetValue(name,out var template))return false;
        var drawn=false;
        foreach(var layer in template.Foreground)
        {
            if(layer.Frame!=frame)continue;
            var full=new Rect2(tileOrigin+layer.Offset*zoom,layer.Region.Size*zoom);
            var left=keepRight?Math.Max(full.Position.X,cutX):full.Position.X;
            var right=keepRight?full.End.X:Math.Min(full.End.X,cutX);if(right<=left)continue;
            var source=new Rect2(layer.Region.Position+new Vector2((left-full.Position.X)/zoom,0),new Vector2((right-left)/zoom,layer.Region.Size.Y));
            target.DrawTextureRectRegion(Texture(layer.Pixels,-1),new Rect2(new Vector2(left,full.Position.Y),new Vector2(right-left,full.Size.Y)),source,light);drawn=true;
        }
        return drawn;
    }
    void DisposeOriginalTerrain()
    {originalTerrainTemplates.Clear();originalTerrainImages.Clear();originalLowBridges.Clear();originalTerrainChecked=false;}
}
