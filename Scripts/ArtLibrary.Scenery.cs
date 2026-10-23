using Godot;
using System.Text.Json;
using GFile=Godot.FileAccess;

namespace RedAlert;

public sealed partial class ArtLibrary
{
    const string ResourceArtFolder="res://Assets/Generated/Resources/OriginalCell/";
    sealed record ResourceArtwork(string Manifest,Vector2 Anchor,int[] DensityFrames);
    readonly Dictionary<string,ResourceArtwork> resourceArtwork=new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,string> resourceOverlayImages=new(StringComparer.OrdinalIgnoreCase);
    string[] resourceOreChoices=[],resourceGemChoices=[];
    bool resourceArtworkChecked;
    bool LoadResourceArtwork()
    {
        if(resourceArtworkChecked)return resourceArtwork.Count>0;
        if(!GFile.FileExists(ResourceArtFolder+"catalog.json"))return false;
        using var document=JsonDocument.Parse(GFile.GetFileAsString(ResourceArtFolder+"catalog.json"));var root=document.RootElement;
        if(ExpansionInt(root,"schema")!=1 || !ExpansionBool(root,"complete"))throw new InvalidDataException("原矿石/水晶目录不完整");
        foreach(var item in root.GetProperty("sprites").EnumerateObject())
        {
            var source=item.Value;var anchor=ExpansionProperty(source,"nativeAnchor");
            var frames=source.GetProperty("densityFrames").EnumerateArray().Select(f=>f.GetInt32()).ToArray();
            // Inferred visual-density consumption, not a proven original engine
            // OverlayData mapping: TIB02 f0 is densest and f1..11 grow. Its measured
            // order is exactly one cyclic shift. All other sources retain0..11.
            // Preserve every native byte/frame and expose the actual selected frame.
            if(item.Name.Equals("tib02",StringComparison.OrdinalIgnoreCase) && frames.Length==12 && source.TryGetProperty("densityOrderByOpaquePixels",out var ordered) &&
                ordered.EnumerateArray().Select(f=>f.GetInt32()).SequenceEqual(frames.Skip(1).Append(frames[0])))
                frames=frames.Skip(1).Append(frames[0]).ToArray();
            resourceArtwork.Add(item.Name,new ResourceArtwork(ExpansionResource(ExpansionString(source,"manifest")),new Vector2(anchor.GetProperty("x").GetSingle(),anchor.GetProperty("y").GetSingle()),frames));
        }
        foreach(var item in root.GetProperty("overlayImages").EnumerateObject())resourceOverlayImages.Add(item.Name,item.Value.GetString()!);
        resourceOreChoices=root.GetProperty("defaultOre").EnumerateArray().Select(s=>s.GetString()!).ToArray();
        resourceGemChoices=root.GetProperty("defaultGems").EnumerateArray().Select(s=>s.GetString()!).ToArray();
        resourceArtworkChecked=true;return resourceArtwork.Count>0;
    }
    public bool HasOriginalResources=>LoadResourceArtwork();
    public int OriginalResourceSpriteCount=>LoadResourceArtwork()?resourceArtwork.Count:0;
    public string OriginalResourceSpriteName(string overlayName,int resourceKind,uint variation)
    {
        if(!LoadResourceArtwork())return "";
        if(resourceOverlayImages.TryGetValue(overlayName,out var original))return original;
        var choices=resourceKind==2?resourceGemChoices:resourceOreChoices;
        return choices.Length>0?choices[(int)(variation%(uint)choices.Length)]:"";
    }
    SpriteAtlas? ResourceSprite(string name)
    {
        var key="$resource-native/"+name;
        if(sprites.TryGetValue(key,out var cached))return cached;
        if(!resourceArtwork.TryGetValue(name,out var artwork) || !GFile.FileExists(artwork.Manifest))return null;
        using var document=JsonDocument.Parse(GFile.GetFileAsString(artwork.Manifest));var root=document.RootElement;
        var images=root.GetProperty("images");if(images.GetArrayLength()!=1)throw new InvalidDataException("矿物图集必须为单图");
        var folder=artwork.Manifest[..(artwork.Manifest.LastIndexOf('/')+1)];var image=images[0];
        var frames=root.GetProperty("frames").EnumerateArray().Select(f=>new Frame(Rect(f.GetProperty("sourceRect")),Rect(f.GetProperty("imageRect")),f.GetProperty("hasData").GetBoolean(),null)).ToArray();
        cached=new SpriteAtlas{Width=root.GetProperty("canvasWidth").GetInt32(),Height=root.GetProperty("canvasHeight").GetInt32(),Frames=frames,Pixels=ReadPixels(folder+image.GetProperty("color").GetString())};
        sprites.Add(key,cached);return cached;
    }
    public bool DrawResourcePatch(CanvasItem canvas,string overlayName,int resourceKind,uint variation,int density,Vector2 center,float zoom,Color light)
    {
        var name=OriginalResourceSpriteName(overlayName,resourceKind,variation);
        if(!resourceArtwork.TryGetValue(name,out var artwork) || artwork.DensityFrames.Length==0 || ResourceSprite(name) is not {} sprite)return false;
        var index=artwork.DensityFrames[Math.Clamp(density,0,artwork.DensityFrames.Length-1)];
        if(index<0 || index>=sprite.Frames.Length || !sprite.Frames[index].Data)return false;
        var frame=sprite.Frames[index];
        // One native 60x60 cell overlay. SourceRect and the original canvas pivot
        // are retained; no miniature GOLD substitute or synthetic repeated clumps.
        canvas.DrawTextureRectRegion(Texture(sprite.Pixels,-1),new Rect2(center+(frame.Source.Position-artwork.Anchor)*zoom,frame.Image.Size*zoom),frame.Image,light);
        return true;
    }
    public bool DrawOrePatch(CanvasItem canvas,uint variation,int density,Vector2 center,float zoom,Color light)
        =>DrawResourcePatch(canvas,"",1,variation,density,center,zoom,light);
    public Godot.Collections.Dictionary GetOriginalResourceArtworkAudit(string overlayName,int resourceKind,uint variation,int density)
    {
        var name=OriginalResourceSpriteName(overlayName,resourceKind,variation);
        var result=new Godot.Collections.Dictionary{["sprite"]=name,["resourceKind"]=resourceKind,["density"]=density,["available"]=false};
        if(!resourceArtwork.TryGetValue(name,out var artwork) || artwork.DensityFrames.Length==0 || ResourceSprite(name) is not {} sprite)return result;
        var index=artwork.DensityFrames[Math.Clamp(density,0,artwork.DensityFrames.Length-1)];var frame=sprite.Frames[index];var opaque=0;
        for(var y=(int)frame.Image.Position.Y;y<(int)frame.Image.End.Y;y++)for(var x=(int)frame.Image.Position.X;x<(int)frame.Image.End.X;x++)
            if(sprite.Pixels.Pixels[(y*sprite.Pixels.Width+x)*4+3]>0)opaque++;
        result["available"]=frame.Data && opaque>0;result["sourceFrame"]=index;result["opaquePixels"]=opaque;result["canvas"]=new Vector2(sprite.Width,sprite.Height);result["anchor"]=artwork.Anchor;return result;
    }
}
