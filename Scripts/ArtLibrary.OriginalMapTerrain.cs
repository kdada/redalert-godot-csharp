using Godot;
using RedAlert.Simulation;
using System.Text.Json;
using GFile=Godot.FileAccess;

namespace RedAlert;

public sealed partial class ArtLibrary
{
    const string OriginalMapTerrainFolder="res://Assets/Generated/ClassicExpansion/Terrain/";
    readonly record struct OriginalMapLayer(Rect2 Region,Vector2 Offset,bool Extra);
    sealed class OriginalMapFrame
    {
        public required OriginalMapLayer[] Layers;
        public Rect2 Bounds,ExtraBounds;
        public bool HasExtra;
    }
    readonly Dictionary<long,OriginalMapFrame> originalMapFrames=[];
    PixelAtlas? originalMapPixels;
    bool originalMapTerrainChecked;
    static long OriginalMapFrameKey(int tileIndex,int subTile)=>((long)(tileIndex is -1 or 65535?0:tileIndex)<<32)|(uint)subTile;
    static Vector2 OriginalMapOffset(JsonElement value)=>new(value.GetProperty("x").GetSingle(),value.GetProperty("y").GetSingle());

    bool LoadOriginalMapTerrain()
    {
        if(originalMapTerrainChecked)return originalMapFrames.Count>0;
        originalMapTerrainChecked=true;
        if(!GFile.FileExists(OriginalMapTerrainFolder+"catalog.json"))return false;
        using var document=JsonDocument.Parse(GFile.GetFileAsString(OriginalMapTerrainFolder+"catalog.json"));
        var root=document.RootElement;
        if(root.GetProperty("schema").GetInt32()!=1 || root.GetProperty("levelRise").GetInt32()!=15)
            throw new InvalidDataException("原版地图地形目录版本或原生高度无效");
        var path=OriginalMapTerrainFolder+root.GetProperty("atlas").GetString();
        originalMapPixels=ReadPixels(path);
        // The existing sprite cache owns the atlas texture's lifetime and reuse.
        sprites.Add("$original-map/"+path,new SpriteAtlas{Width=originalMapPixels.Width,Height=originalMapPixels.Height,Frames=[],Pixels=originalMapPixels});
        foreach(var tile in root.GetProperty("tiles").EnumerateArray())
        {
            var tileIndex=tile.GetProperty("tileIndex").GetInt32();
            foreach(var sourceFrame in tile.GetProperty("frames").EnumerateArray())
            {
                var layers=new List<OriginalMapLayer>();var bounds=new Rect2();var extraBounds=new Rect2();var first=true;var firstExtra=true;
                foreach(var sourceLayer in sourceFrame.GetProperty("layers").EnumerateArray())
                {
                    var region=Rect(sourceLayer.GetProperty("region"));
                    if(region.Size.X<=0 || region.Size.Y<=0)continue;
                    if(region.Position.X<0 || region.Position.Y<0 || region.End.X>originalMapPixels.Width || region.End.Y>originalMapPixels.Height)
                        throw new InvalidDataException("原版地图地形图集区域越界");
                    var offset=OriginalMapOffset(sourceLayer.GetProperty("offsetFromCellCenter"));
                    var extra=sourceLayer.GetProperty("kind").GetString()=="extra";
                    layers.Add(new OriginalMapLayer(region,offset,extra));
                    var rectangle=new Rect2(offset,region.Size);bounds=first?rectangle:bounds.Merge(rectangle);first=false;
                    if(extra){extraBounds=firstExtra?rectangle:extraBounds.Merge(rectangle);firstExtra=false;}
                }
                var key=OriginalMapFrameKey(tileIndex,sourceFrame.GetProperty("index").GetInt32());
                originalMapFrames.Add(key,new OriginalMapFrame{Layers=layers.ToArray(),Bounds=bounds,ExtraBounds=extraBounds,HasExtra=!firstExtra});
            }
        }
        return originalMapFrames.Count>0;
    }

    OriginalMapFrame? OriginalMapFrameAt(ClassicMapCell cell)=>LoadOriginalMapTerrain()?originalMapFrames.GetValueOrDefault(OriginalMapFrameKey(cell.TileIndex,cell.SubTile)):null;
    public bool HasOriginalMapTerrain=>LoadOriginalMapTerrain();
    public int OriginalMapTerrainFrameCount=>LoadOriginalMapTerrain()?originalMapFrames.Count:0;
    public bool OriginalMapTileHasExtra(ClassicMapCell cell)=>OriginalMapFrameAt(cell)?.HasExtra==true;

    /// <summary>Bounds include original overhanging cliff pixels, not only a 60x30 diamond.</summary>
    public Rect2 OriginalMapTileBounds(ClassicMapCell cell,Vector2 projectedCellCenter,float zoom,bool extraOnly=false)
    {
        var frame=OriginalMapFrameAt(cell);if(frame is null || extraOnly && !frame.HasExtra)return new Rect2(projectedCellCenter,Vector2.Zero);
        var bounds=extraOnly?frame.ExtraBounds:frame.Bounds;
        return new Rect2(projectedCellCenter+bounds.Position*zoom,bounds.Size*zoom);
    }

    /// <summary>
    /// Draw one original TileIndex/SubTile, with its native diamond and extra pixels.
    /// projectedCellCenter must already include cell.Level*15 pixels, exactly once.
    /// SurfaceLevel belongs to units/bridge decks and must not re-raise base TMP art.
    /// </summary>
    public bool DrawOriginalMapTile(CanvasItem canvas,ClassicMapCell cell,Vector2 projectedCellCenter,float zoom,Color light,bool includeExtra=true)
    {
        var frame=OriginalMapFrameAt(cell);if(frame is null || originalMapPixels is null)return false;
        var texture=Texture(originalMapPixels,-1);var drawn=false;
        foreach(var layer in frame.Layers)
        {
            if(layer.Extra && !includeExtra)continue;
            canvas.DrawTextureRectRegion(texture,new Rect2(projectedCellCenter+layer.Offset*zoom,layer.Region.Size*zoom),layer.Region,light);drawn=true;
        }
        return drawn;
    }

    /// <summary>
    /// The host selects cells genuinely in front of an actor and excludes its own
    /// surface. Default redraws native extra cliff/overhang pixels only. includeTop
    /// may be enabled for a higher foreground surface, never an actor's own road.
    /// No wall or top is synthesized, warped, or translated by templateHeight again.
    /// </summary>
    public bool DrawOriginalMapTileForeground(CanvasItem canvas,ClassicMapCell cell,Vector2 projectedCellCenter,float zoom,Color light,bool includeTop=false)
    {
        var frame=OriginalMapFrameAt(cell);if(frame is null || originalMapPixels is null)return false;
        var texture=Texture(originalMapPixels,-1);var drawn=false;
        foreach(var layer in frame.Layers)
        {
            if(!layer.Extra && !includeTop)continue;
            canvas.DrawTextureRectRegion(texture,new Rect2(projectedCellCenter+layer.Offset*zoom,layer.Region.Size*zoom),layer.Region,light);drawn=true;
        }
        return drawn;
    }

    // Atlas texture disposal is already covered by sprites. The main Dispose()
    // may call this helper to release metadata immediately when reusing an object.
    void DisposeOriginalMapTerrainMetadata()
    {originalMapFrames.Clear();originalMapPixels=null;originalMapTerrainChecked=false;DisposeOriginalMapOverlayMetadata();}

    const string OriginalMapOverlayFolder=OriginalMapTerrainFolder+"Bridges/";
    readonly record struct OriginalMapOverlayLayer(Rect2 Region,Vector2 Offset);
    sealed class OriginalMapOverlayFrame
    {
        public OriginalMapOverlayLayer Layer;
        public OriginalMapOverlayLayer? Shadow,Foreground;
        public int Pair;
        public bool HasData,IsShadow;
    }
    sealed class OriginalMapOverlaySprite
    {public readonly Dictionary<int,OriginalMapOverlayFrame> Frames=[];}
    sealed record OriginalMapHighOverlay(OriginalMapOverlaySprite Sprite,int Offset,int States);
    sealed record OriginalMapLowFrame(bool HasData,Vector2 DeckAnchor);
    sealed record OriginalMapLowOverlay(string Template,Vector2 TileTopAnchor,Dictionary<int,OriginalMapLowFrame> Frames);
    readonly Dictionary<string,OriginalMapOverlaySprite> originalMapOverlaySprites=new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,OriginalMapHighOverlay> originalMapHighOverlays=new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,OriginalMapLowOverlay> originalMapLowOverlays=new(StringComparer.OrdinalIgnoreCase);
    PixelAtlas? originalMapOverlayPixels;
    bool originalMapOverlaysChecked;

    bool LoadOriginalMapOverlays()
    {
        if(originalMapOverlaysChecked)return originalMapOverlayPixels is not null;
        originalMapOverlaysChecked=true;
        if(!GFile.FileExists(OriginalMapOverlayFolder+"catalog.json"))return false;
        using var document=JsonDocument.Parse(GFile.GetFileAsString(OriginalMapOverlayFolder+"catalog.json"));
        var root=document.RootElement;if(root.GetProperty("schema").GetInt32()!=2)throw new InvalidDataException("原版桥箱覆盖层目录版本无效");
        var path=OriginalMapOverlayFolder+root.GetProperty("atlas").GetString();
        var overlayPixels=ReadPixels(path);originalMapOverlayPixels=overlayPixels;
        sprites.Add("$original-map-overlays/"+path,new SpriteAtlas{Width=overlayPixels.Width,Height=overlayPixels.Height,Frames=[],Pixels=overlayPixels});
        OriginalMapOverlayLayer Layer(JsonElement value)
        {
            var region=Rect(value.GetProperty("region"));
            if(region.Position.X<0 || region.Position.Y<0 || region.End.X>overlayPixels.Width || region.End.Y>overlayPixels.Height)
                throw new InvalidDataException("原版桥箱覆盖层区域越界");
            return new OriginalMapOverlayLayer(region,OriginalMapOffset(value.GetProperty("offsetFromDeckCenter")));
        }
        foreach(var template in root.GetProperty("templates").EnumerateArray())
        {
            var sprite=new OriginalMapOverlaySprite();
            foreach(var source in template.GetProperty("frames").EnumerateArray())
            {
                var frame=new OriginalMapOverlayFrame{Layer=Layer(source),Pair=source.GetProperty("pairedFrame").GetInt32(),HasData=source.GetProperty("hasData").GetBoolean(),IsShadow=source.GetProperty("role").GetString()=="shadow-mask"};
                if(source.TryGetProperty("foregroundLayer",out var foreground) && foreground.GetProperty("hasData").GetBoolean())frame.Foreground=Layer(foreground);
                sprite.Frames.Add(source.GetProperty("index").GetInt32(),frame);
            }
            foreach(var frame in sprite.Frames.Values)
                if(!frame.IsShadow && sprite.Frames.TryGetValue(frame.Pair,out var shadow) && shadow.IsShadow && shadow.HasData)frame.Shadow=shadow.Layer;
            originalMapOverlaySprites.Add(template.GetProperty("id").GetString()!,sprite);
        }
        foreach(var choice in root.GetProperty("highOverlaySelection").EnumerateObject())
            originalMapHighOverlays.Add(choice.Name,new OriginalMapHighOverlay(originalMapOverlaySprites[choice.Value.GetProperty("template").GetString()!],choice.Value.GetProperty("frameOffset").GetInt32(),choice.Value.GetProperty("stateCount").GetInt32()));
        foreach(var source in root.GetProperty("lowOverlays").EnumerateArray())
        {
            var frames=new Dictionary<int,OriginalMapLowFrame>();
            foreach(var frame in source.GetProperty("frames").EnumerateArray())
                frames.Add(frame.GetProperty("index").GetInt32(),new OriginalMapLowFrame(frame.GetProperty("hasData").GetBoolean(),OriginalMapOffset(frame.GetProperty("deckCenterAnchor"))));
            originalMapLowOverlays.Add(source.GetProperty("overlayName").GetString()!,new OriginalMapLowOverlay(source.GetProperty("template").GetString()!,OriginalMapOffset(source.GetProperty("nativeTileTopAnchor")),frames));
        }
        return true;
    }
    OriginalMapOverlayFrame? OriginalMapHighBridgeFrame(ClassicMapCell cell)
    {
        if(!LoadOriginalMapOverlays() || !originalMapHighOverlays.TryGetValue(cell.OverlayName,out var choice) || cell.OverlayData<0 || cell.OverlayData>=choice.States)return null;
        return choice.Sprite.Frames.GetValueOrDefault(choice.Offset+cell.OverlayData);
    }
    static Rect2 OriginalMapOverlayRectangle(OriginalMapOverlayLayer layer,Vector2 point,float zoom)=>new(point+layer.Offset*zoom,layer.Region.Size*zoom);
    void DrawOriginalMapOverlayLayer(CanvasItem canvas,OriginalMapOverlayLayer layer,Vector2 point,float zoom,Color light)
    {canvas.DrawTextureRectRegion(Texture(originalMapOverlayPixels!,-1),OriginalMapOverlayRectangle(layer,point,zoom),layer.Region,light);}
    void DrawOriginalMapOverlayFrame(CanvasItem canvas,OriginalMapOverlayFrame frame,Vector2 point,float zoom,Color light)
    {
        // The source's cyan index1 frames are coverage masks, not color pictures.
        if(frame.Shadow is{} shadow)DrawOriginalMapOverlayLayer(canvas,shadow,point,zoom,new Color(0,0,0,.32f*light.A));
        if(frame.HasData && !frame.IsShadow)DrawOriginalMapOverlayLayer(canvas,frame.Layer,point,zoom,light);
    }

    /// <summary>
    /// Source OverlayName/OverlayData only. projectedDeckCenter already includes
    /// SurfaceLevel*15, once. A genuine native empty low-bridge state returns true
    /// without painting pixels: the caller must not replace it with a fake span.
    /// </summary>
    public bool DrawOriginalMapBridgeOverlay(CanvasItem canvas,ClassicMapCell cell,Vector2 projectedDeckCenter,float zoom,Color light)
    {
        var high=OriginalMapHighBridgeFrame(cell);
        if(high is not null){DrawOriginalMapOverlayFrame(canvas,high,projectedDeckCenter,zoom,light);return true;}
        if(!LoadOriginalMapOverlays() || !originalMapLowOverlays.TryGetValue(cell.OverlayName,out var low) || !low.Frames.TryGetValue(cell.OverlayData,out var frame))return false;
        if(!frame.HasData)return true;
        return DrawOriginalTerrain(canvas,low.Template,cell.OverlayData,projectedDeckCenter+(low.TileTopAnchor-frame.DeckAnchor)*zoom,zoom,light);
    }
    public Rect2 OriginalMapBridgeOverlayBounds(ClassicMapCell cell,Vector2 projectedDeckCenter,float zoom)
    {
        var high=OriginalMapHighBridgeFrame(cell);
        if(high is not null)
        {
            var bounds=high.HasData?OriginalMapOverlayRectangle(high.Layer,projectedDeckCenter,zoom):new Rect2(projectedDeckCenter,Vector2.Zero);
            return high.Shadow is{} shadow?bounds.Merge(OriginalMapOverlayRectangle(shadow,projectedDeckCenter,zoom)):bounds;
        }
        if(!LoadOriginalMapOverlays() || !originalMapLowOverlays.TryGetValue(cell.OverlayName,out var low) || !low.Frames.TryGetValue(cell.OverlayData,out var frame) || !frame.HasData)return new Rect2(projectedDeckCenter,Vector2.Zero);
        return OriginalTerrainBounds(low.Template,cell.OverlayData,projectedDeckCenter+(low.TileTopAnchor-frame.DeckAnchor)*zoom,zoom);
    }
    /// <summary>
    /// Repaint only original near-side exterior pixels, horizontally clipped by
    /// the host's actual near-edge depth test. No road top or replacement rail.
    /// High above-deck cap semantics are intentionally not guessed from SHP colors.
    /// </summary>
    public bool DrawOriginalMapBridgeForeground(CanvasItem canvas,ClassicMapCell cell,Vector2 projectedDeckCenter,float zoom,Color light,float cutX,bool keepRight)
    {
        var high=OriginalMapHighBridgeFrame(cell);
        if(high is not null)
        {
            if(high.Foreground is not{} layer)return false;
            var full=OriginalMapOverlayRectangle(layer,projectedDeckCenter,zoom);
            var left=keepRight?Math.Max(full.Position.X,cutX):full.Position.X;
            var right=keepRight?full.End.X:Math.Min(full.End.X,cutX);if(right<=left)return false;
            var source=new Rect2(layer.Region.Position+new Vector2((left-full.Position.X)/zoom,0),new Vector2((right-left)/zoom,layer.Region.Size.Y));
            canvas.DrawTextureRectRegion(Texture(originalMapOverlayPixels!,-1),new Rect2(new Vector2(left,full.Position.Y),new Vector2(right-left,full.Size.Y)),source,light);return true;
        }
        if(!LoadOriginalMapOverlays() || !originalMapLowOverlays.TryGetValue(cell.OverlayName,out var low) || !low.Frames.TryGetValue(cell.OverlayData,out var frame) || !frame.HasData)return false;
        return DrawOriginalTerrainForeground(canvas,low.Template,cell.OverlayData,projectedDeckCenter+(low.TileTopAnchor-frame.DeckAnchor)*zoom,zoom,light,cutX,keepRight);
    }
    /// <summary>Original CRATE/WCRATE box only. Each has one color frame and one shadow; tick adds no invented animation and Kind is not exposed.</summary>
    public bool DrawOriginalCrate(CanvasItem canvas,bool water,Vector2 projectedCellCenter,float zoom,Color light,int tick)
    {
        if(!LoadOriginalMapOverlays() || !originalMapOverlaySprites.TryGetValue(water?"wcrate":"crate",out var sprite) || !sprite.Frames.TryGetValue(0,out var frame) || frame.IsShadow)return false;
        DrawOriginalMapOverlayFrame(canvas,frame,projectedCellCenter,zoom,light);return true;
    }
    const string OriginalMapRockFolder="res://Assets/Generated/ClassicExpansion/MapObjects/Rocks/";
    readonly Dictionary<string,string> originalMapRockNames=new(StringComparer.OrdinalIgnoreCase),originalMapRockFolders=new(StringComparer.OrdinalIgnoreCase);
    bool originalMapRocksChecked;
    bool LoadOriginalMapRocks()
    {
        if(originalMapRocksChecked)return originalMapRockNames.Count>0;
        if(!GFile.FileExists(OriginalMapRockFolder+"catalog.json"))return false;
        using var document=JsonDocument.Parse(GFile.GetFileAsString(OriginalMapRockFolder+"catalog.json"));var root=document.RootElement;
        if(ExpansionInt(root,"schema")!=1 || !ExpansionBool(root,"complete"))throw new InvalidDataException("原版岩石覆盖层目录不完整");
        foreach(var entry in root.GetProperty("sprites").EnumerateObject())
        {
            var manifest=ExpansionResource(ExpansionString(entry.Value,"manifest"));
            originalMapRockFolders.Add(entry.Name,manifest[..(manifest.LastIndexOf('/')+1)]);
        }
        foreach(var entry in root.GetProperty("objects").EnumerateObject())
        {
            var sprite=ExpansionString(entry.Value,"spriteName");
            if(!originalMapRockFolders.ContainsKey(sprite))throw new InvalidDataException("原版岩石覆盖层引用缺失："+entry.Name);
            originalMapRockNames.Add(entry.Name,sprite);
        }
        originalMapRocksChecked=true;return originalMapRockNames.Count>0;
    }
    string? OriginalMapRockSpriteFolder(string name)=>LoadOriginalMapRocks()?originalMapRockFolders.GetValueOrDefault(name):null;
    public bool HasOriginalMapRocks=>LoadOriginalMapRocks();
    public int OriginalMapRockTypeCount=>LoadOriginalMapRocks()?originalMapRockFolders.Count:0;
    SpriteAtlas? OriginalMapRockAt(ClassicMapCell cell)=>LoadOriginalMapRocks() && originalMapRockNames.TryGetValue(cell.OverlayName,out var sprite)?Sprite(sprite):null;
    static int OriginalMapRockFrame(SpriteAtlas sprite,ClassicMapCell cell)=>Math.Clamp(cell.OverlayData,0,Math.Max(0,BuildingColorFrames(sprite)-1));
    static Vector2 OriginalMapRockAnchor(SpriteAtlas sprite)=>new(sprite.Width/2f,sprite.Height/2f);
    static Rect2 OriginalMapRockRectangle(SpriteAtlas sprite,int frame,Vector2 point,float zoom)
    {var source=sprite.Frames[frame];return new Rect2(point+(source.Source.Position-OriginalMapRockAnchor(sprite))*zoom,source.Image.Size*zoom);}
    void DrawOriginalMapRockFrame(CanvasItem canvas,SpriteAtlas sprite,int frame,Vector2 point,float zoom,Color light)
    {
        if(frame<0 || frame>=sprite.Frames.Length || !sprite.Frames[frame].Data)return;
        canvas.DrawTextureRectRegion(Texture(sprite.Pixels,-1),OriginalMapRockRectangle(sprite,frame,point,zoom),sprite.Frames[frame].Image,light);
    }
    /// <summary>Only original TROCK/SROCK and RUBBLE alias. Input is the existing ground cell center including Level, once; OverlayData selects native color states. Host sorts rocks with actors and draws DrawFlat rubble before actors.</summary>
    public bool DrawOriginalMapRockOverlay(CanvasItem canvas,ClassicMapCell cell,Vector2 groundCellCenter,float zoom,Color light)
    {
        var sprite=OriginalMapRockAt(cell);if(sprite is null || sprite.Frames.Length==0)return false;
        var frame=OriginalMapRockFrame(sprite,cell);
        if(sprite.Frames[frame].Shadow is int shadow)DrawOriginalMapRockFrame(canvas,sprite,shadow,groundCellCenter,zoom,light);
        DrawOriginalMapRockFrame(canvas,sprite,frame,groundCellCenter,zoom,light);return true;
    }
    public Rect2 OriginalMapRockOverlayBounds(ClassicMapCell cell,Vector2 groundCellCenter,float zoom)
    {
        var sprite=OriginalMapRockAt(cell);if(sprite is null || sprite.Frames.Length==0)return new Rect2(groundCellCenter,Vector2.Zero);
        var frame=OriginalMapRockFrame(sprite,cell);var has=sprite.Frames[frame].Data;
        var result=has?OriginalMapRockRectangle(sprite,frame,groundCellCenter,zoom):new Rect2(groundCellCenter,Vector2.Zero);
        if(sprite.Frames[frame].Shadow is int shadow && shadow>=0 && shadow<sprite.Frames.Length && sprite.Frames[shadow].Data)
        {var bounds=OriginalMapRockRectangle(sprite,shadow,groundCellCenter,zoom);result=has?result.Merge(bounds):bounds;}
        return result;
    }
    void DisposeOriginalMapOverlayMetadata()
    {originalMapOverlaySprites.Clear();originalMapHighOverlays.Clear();originalMapLowOverlays.Clear();originalMapOverlayPixels=null;originalMapOverlaysChecked=false;originalMapRockNames.Clear();originalMapRockFolders.Clear();originalMapRocksChecked=false;}
}
