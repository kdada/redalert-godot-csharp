using Godot;
using RedAlert.Simulation;
using System.Text.Json;
using GFile=Godot.FileAccess;

namespace RedAlert;

public sealed partial class ArtLibrary
{
    const string MapObjectArtFolder="res://Assets/Generated/ClassicExpansion/MapObjects/";
    sealed class OriginalObjectVisual
    {
        public required string Name,Kind,Sprite,Voxel;
        public required JsonElement Metadata,Artwork;
        public Vector2? Offset;
        public int Width,Height;
    }
    readonly Dictionary<string,OriginalObjectVisual> originalObjectVisuals=new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,string> originalObjectSpriteFolders=new(StringComparer.OrdinalIgnoreCase),originalObjectVehiclePaths=new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<ClassicMapObject,OriginalObjectVisual?> originalObjectLookup=[];
    readonly List<(SpriteAtlas Sprite,int Frame,Vector2 Anchor)> originalObjectLayers=[];
    bool originalObjectMetadataChecked;

    bool LoadOriginalMapObjectMetadata()
    {
        if(originalObjectMetadataChecked)return originalObjectVisuals.Count>0;
        // The producer publishes the complete catalog after every required source.
        // Missing catalogs are retryable while an editor session is importing data.
        if(!GFile.FileExists(MapObjectArtFolder+"catalog.json") || !GFile.FileExists(MapObjectArtFolder+"metadata.json"))return false;
        using var artDocument=JsonDocument.Parse(GFile.GetFileAsString(MapObjectArtFolder+"catalog.json"));
        using var metadataDocument=JsonDocument.Parse(GFile.GetFileAsString(MapObjectArtFolder+"metadata.json"));
        var catalog=artDocument.RootElement;var metadata=metadataDocument.RootElement;
        if(ExpansionInt(catalog,"schema")!=1 || ExpansionInt(metadata,"schema")!=1)throw new InvalidDataException("原版地图物件目录版本无效");
        foreach(var entry in metadata.GetProperty("objects").EnumerateObject())
        {
            var artwork=ExpansionProperty(catalog.GetProperty("objects"),entry.Name);
            if(artwork.ValueKind!=JsonValueKind.Object)continue;
            var definition=entry.Value.Clone();
            originalObjectVisuals.Add(entry.Name,new OriginalObjectVisual{Name=entry.Name,Kind=ExpansionString(definition,"kind"),Sprite=ExpansionString(artwork,"spriteName"),Voxel=ExpansionString(artwork,"voxelName"),Metadata=definition,Artwork=artwork.Clone(),Width=Math.Max(1,ExpansionInt(definition,"foundationW",1)),Height=Math.Max(1,ExpansionInt(definition,"foundationH",1))});
        }
        foreach(var entry in catalog.GetProperty("sprites").EnumerateObject())
        {var path=ExpansionResource(ExpansionString(entry.Value,"manifest"));originalObjectSpriteFolders[entry.Name]=path[..(path.LastIndexOf('/')+1)];}
        foreach(var entry in catalog.GetProperty("voxels").EnumerateObject())
            originalObjectVehiclePaths[entry.Name]=ExpansionResource(ExpansionString(entry.Value,"manifest"));
        originalObjectMetadataChecked=true;return originalObjectVisuals.Count>0;
    }
    string? OriginalMapObjectSpriteFolder(string name)=>LoadOriginalMapObjectMetadata()?originalObjectSpriteFolders.GetValueOrDefault(ExpansionName(name)):null;
    string? OriginalMapObjectVehicleManifest(string name)=>LoadOriginalMapObjectMetadata()?originalObjectVehiclePaths.GetValueOrDefault(ExpansionName(name)):null;
    public bool HasOriginalMapObjects=>LoadOriginalMapObjectMetadata();
    public int OriginalMapObjectTypeCount=>LoadOriginalMapObjectMetadata()?originalObjectVisuals.Count:0;
    OriginalObjectVisual? OriginalObjectAt(ClassicMapObject item)
    {
        if(!LoadOriginalMapObjectMetadata())return null;
        if(originalObjectLookup.TryGetValue(item,out var cached))return cached;
        var fields=item.Value.Split(',');
        var second=item.Section.Equals("structures",StringComparison.OrdinalIgnoreCase) || item.Section.Equals("units",StringComparison.OrdinalIgnoreCase) || item.Section.Equals("infantry",StringComparison.OrdinalIgnoreCase) || item.Section.Equals("aircraft",StringComparison.OrdinalIgnoreCase);
        var name=fields.Length>(second?1:0)?fields[second?1:0].Trim():"";
        originalObjectVisuals.TryGetValue(name,out var result);originalObjectLookup.Add(item,result);return result;
    }
    static int OriginalObjectValue(ClassicMapObject item,int index,int fallback=0)
    {
        var start=0;
        for(var field=0;field<index;field++){var comma=item.Value.IndexOf(',',start);if(comma<0)return fallback;start=comma+1;}
        var end=item.Value.IndexOf(',',start);if(end<0)end=item.Value.Length;
        return int.TryParse(item.Value.AsSpan(start,end-start),out var value)?value:fallback;
    }
    Vector2 OriginalObjectOffset(OriginalObjectVisual visual,SpriteAtlas sprite)
    {
        if(visual.Offset is {} cached)return cached;
        var art=ExpansionProperty(visual.Metadata,"originalArt");var rules=ExpansionProperty(visual.Metadata,"originalRules");
        var adjust=new Vector2(ExpansionInt(art,"XDrawOffset"),ExpansionInt(art,"YDrawOffset",ExpansionInt(rules,"YDrawFudge")));
        // Original smudges share a canvas center 15 px above their first cell
        // center. Larger Width/Height footprints are already painted into that
        // canvas (one color frame), never tiled or scaled by the host.
        if(visual.Kind=="smudge"){visual.Offset=adjust+new Vector2(0,-15);return visual.Offset.Value;}
        if(visual.Kind=="terrain"){visual.Offset=adjust;return adjust;}
        if(visual.Kind=="structure" && offsets.TryGetValue(visual.Sprite,out var known))
        {visual.Offset=known+adjust;return visual.Offset.Value;}
        // Ground anchors are physical cell/foundation centers supplied by the host.
        // Terrain uses its native canvas center. A building's front foot is
        // half the isometric foundation below the foundation center.
        var lowest=float.NegativeInfinity;
        void Observe(SpriteAtlas part,int frame,int dy=0)
        {if(frame>=0 && frame<part.Frames.Length && part.Frames[frame].Data)lowest=Math.Max(lowest,part.Frames[frame].Source.End.Y-part.Height/2f+dy);}
        Observe(sprite,0);
        if(float.IsNegativeInfinity(lowest))
        {
            var layers=ExpansionProperty(visual.Artwork,"layers");
            if(layers.ValueKind==JsonValueKind.Array)foreach(var layer in layers.EnumerateArray())
            {
                var name=ExpansionString(layer,"spriteName");if(name.Length==0 || Sprite(name) is not {} part)continue;
                Observe(part,Math.Max(0,ExpansionInt(layer,"start",ExpansionInt(layer,"loopStart"))),ExpansionInt(ExpansionProperty(layer,"relativeOffsets"),"y"));
            }
        }
        if(float.IsNegativeInfinity(lowest))lowest=sprite.Height/2f;
        var frontHalf=visual.Kind=="structure"?(visual.Width+visual.Height)*7.5f:0;
        var offset=new Vector2(0,frontHalf-lowest)+adjust;visual.Offset=offset;return offset;
    }
    void AddOriginalObjectLayer(SpriteAtlas sprite,int frame,Vector2 offset)
    {
        if(frame<0 || frame>=BuildingColorFrames(sprite))return;
        originalObjectLayers.Add((sprite,frame,new Vector2(sprite.Width/2f,sprite.Height/2f)-offset));
    }
    void PrepareOriginalObjectLayers(OriginalObjectVisual visual,ClassicMapObject item)
    {
        originalObjectLayers.Clear();var sprite=Sprite(visual.Sprite);if(sprite is null)return;
        var offset=OriginalObjectOffset(visual,sprite);
        var damaged=visual.Kind=="structure" && OriginalObjectValue(item,2,256)<128;
        var values=ExpansionProperty(visual.Artwork,"layers");
        JsonElement[] layers=values.ValueKind==JsonValueKind.Array?values.EnumerateArray().ToArray():[];
        foreach(var layer in layers)if(ExpansionString(layer,"role")=="bib" && Sprite(ExpansionString(layer,"spriteName")) is {} bib)
            AddOriginalObjectLayer(bib,damaged && BuildingColorFrames(bib)>1?1:0,offset);
        var frame=damaged && BuildingColorFrames(sprite)>1?1:0;
        if(visual.Kind=="smudge")frame=Math.Clamp(OriginalObjectValue(item,3),0,Math.Max(0,BuildingColorFrames(sprite)-1));
        AddOriginalObjectLayer(sprite,frame,offset);
        foreach(var layer in layers)
        {
            if(ExpansionString(layer,"role")!="animation" || ExpansionBool(layer,"missing"))continue;
            var reference=ExpansionString(layer,"referenceProperty");var isDamaged=ExpansionDamagedReference(reference);
            if(isDamaged && !damaged)continue;
            if(!isDamaged && damaged && layers.Any(other=>ExpansionString(other,"referenceProperty").Equals(reference+"Damaged",StringComparison.OrdinalIgnoreCase) && !ExpansionBool(other,"missing")))continue;
            var name=ExpansionString(layer,"spriteName");if(name.Length==0 || Sprite(name) is not {} detail)continue;
            var relative=ExpansionProperty(layer,"relativeOffsets");
            // These are immutable scenery, so retain the native standing frame.
            AddOriginalObjectLayer(detail,Math.Max(0,ExpansionInt(layer,"start",ExpansionInt(layer,"loopStart"))),offset+new Vector2(ExpansionInt(relative,"x"),ExpansionInt(relative,"y")));
        }
    }
    void DrawOriginalObjectFrame(CanvasItem canvas,SpriteAtlas sprite,int frame,Vector2 point,Vector2 anchor,float zoom,Color light)
    {
        if(frame<0 || frame>=sprite.Frames.Length)return;var source=sprite.Frames[frame];
        if(!source.Data || source.Image.Size.X<=0 || source.Image.Size.Y<=0)return;
        canvas.DrawTextureRectRegion(Texture(sprite.Pixels,-1),new Rect2(point+(source.Source.Position-anchor)*zoom,source.Image.Size*zoom),source.Image,light);
    }
    static int OriginalObjectVoxelDirection(ClassicMapObject item,VoxelAtlas voxel)
    {
        var facing=OriginalObjectValue(item,5);var count=Math.Max(1,voxel.Directions);
        // Original 0=N,64=E,128=S,192=W; the shared baker's E-facing is zero.
        return ((count/4-(int)MathF.Round((facing&255)*count/256f))%count+count)%count;
    }
    /// <summary>Immutable original scenery. Host supplies translated physical ground/foundation center and source-cell lighting. No entity or simulation mutation.</summary>
    public bool DrawOriginalMapObject(CanvasItem canvas,ClassicMapObject item,Vector2 groundAnchor,float zoom,Color light,int tick)
    {
        var visual=OriginalObjectAt(item);if(visual is null)return false;
        if(visual.Voxel.Length>0)
        {
            var voxel=Vehicle(visual.Voxel);if(voxel is null)return false;var direction=OriginalObjectVoxelDirection(item,voxel);
            var source=new Rect2(direction%voxel.Columns*voxel.Width,direction/voxel.Columns*voxel.Height,voxel.Width,voxel.Height);
            foreach(var part in voxel.Parts.Values)
                canvas.DrawTextureRectRegion(Texture(part,-1),new Rect2(groundAnchor-voxel.Anchor*zoom,new Vector2(voxel.Width,voxel.Height)*zoom),source,light);
            return true;
        }
        if(visual.Sprite.Length==0 || Sprite(visual.Sprite) is null)return false;
        PrepareOriginalObjectLayers(visual,item);
        foreach(var layer in originalObjectLayers)if(layer.Sprite.Frames[layer.Frame].Shadow is int shadow)
            DrawOriginalObjectFrame(canvas,layer.Sprite,shadow,groundAnchor,layer.Anchor,zoom,light);
        foreach(var layer in originalObjectLayers)DrawOriginalObjectFrame(canvas,layer.Sprite,layer.Frame,groundAnchor,layer.Anchor,zoom,light);
        return true;
    }
    public Rect2 OriginalMapObjectBounds(ClassicMapObject item,Vector2 groundAnchor,float zoom)
    {
        var visual=OriginalObjectAt(item);if(visual is null)return default;
        if(visual.Voxel.Length>0 && Vehicle(visual.Voxel) is {} voxel)
            return new Rect2(groundAnchor-voxel.Anchor*zoom,new Vector2(voxel.Width,voxel.Height)*zoom);
        PrepareOriginalObjectLayers(visual,item);var result=new Rect2();var hasBounds=false;
        void Include(SpriteAtlas sprite,int frame,Vector2 anchor)
        {
            if(frame<0 || frame>=sprite.Frames.Length)return;var source=sprite.Frames[frame];if(!source.Data)return;
            var rect=new Rect2(groundAnchor+(source.Source.Position-anchor)*zoom,source.Image.Size*zoom);
            result=hasBounds?result.Merge(rect):rect;hasBounds=true;
        }
        foreach(var layer in originalObjectLayers)
        {Include(layer.Sprite,layer.Frame,layer.Anchor);if(layer.Sprite.Frames[layer.Frame].Shadow is int shadow)Include(layer.Sprite,shadow,layer.Anchor);}
        return result;
    }
    void DisposeOriginalMapObjectMetadata()
    {originalObjectVisuals.Clear();originalObjectSpriteFolders.Clear();originalObjectVehiclePaths.Clear();originalObjectLookup.Clear();originalObjectLayers.Clear();originalObjectMetadataChecked=false;}
}
