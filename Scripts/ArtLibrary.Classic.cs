using Godot;
using RedAlert.Simulation;
using System.Text.Json;
using GFile=Godot.FileAccess;

namespace RedAlert;

public sealed partial class ArtLibrary
{
    readonly Dictionary<string,ImageTexture> interfaceFrames=[];
    readonly Dictionary<string,Vector2> sceneryAnchors=[];
    ImageTexture? groundAtlas;
    readonly Dictionary<string,Rect2[]> groundRegions=[];
    readonly Dictionary<(string Name,int Frame),Rect2> terrainTemplates=[];
    readonly Dictionary<(string Name,int Frame),Color> terrainUnderfills=[];
    readonly Dictionary<Rect2,Color> groundUnderfills=[];
    bool groundChecked;
    BattleMap? plannedMap;
    uint plannedSeed;
    ClassicTerrainPlan? terrainPlan;
    public ClassicTerrainPlan? CurrentTerrainPlan=>terrainPlan;
    public int InterfaceFrameCount=>interfaceFrames.Count;
    public int TerrainTileCount=>terrainTemplates.Count;

    // Original sidec01/02 art: radar 168x110, side1 168x69, side2 168x50,
    // side3 168x26; tab00..03, repair, sell and power retain all button states.
    public Texture2D? InterfaceFrame(string side,string name,int frame=0)
    {
        side=side.Equals("soviet",StringComparison.OrdinalIgnoreCase)?"soviet":"allied";
        name=name.ToLowerInvariant();
        name=name switch{"sidebar-top" or "top"=>"side1","sidebar-middle" or "sidebar-row" or "middle"=>"side2","sidebar-bottom" or "bottom"=>"side3","radar-frame"=>"radar",_=>name};
        var key=$"{side}/{name}/{frame}";
        if(interfaceFrames.TryGetValue(key,out var cached))return cached;
        var folder=$"res://Assets/Generated/Interface/{side}/{name}/";
        if(!GFile.FileExists(folder+"manifest.json"))return null;
        using var document=JsonDocument.Parse(GFile.GetFileAsString(folder+"manifest.json"));
        var root=document.RootElement;var frames=root.GetProperty("frames");
        if(frame<0 || frame>=frames.GetArrayLength())return null;
        var selected=frames[frame];var imageSet=selected.GetProperty("imageSet").GetInt32();
        var imageFile=root.GetProperty("images")[imageSet].GetProperty("color").GetString();
        using var atlas=LoadImage(folder+imageFile);atlas.Convert(Image.Format.Rgba8);
        using var canvas=Image.CreateEmpty(root.GetProperty("canvasWidth").GetInt32(),root.GetProperty("canvasHeight").GetInt32(),false,Image.Format.Rgba8);
        canvas.Fill(Colors.Transparent);
        var original=Rect(selected.GetProperty("sourceRect"));var packed=Rect(selected.GetProperty("imageRect"));
        if(packed.Size.X>0 && packed.Size.Y>0)canvas.BlitRect(atlas,new Rect2I((int)packed.Position.X,(int)packed.Position.Y,(int)packed.Size.X,(int)packed.Size.Y),new Vector2I((int)original.Position.X,(int)original.Position.Y));
        var used=canvas.GetUsedRect();sceneryAnchors[key]=new Vector2(canvas.GetWidth()/2f,used.End.Y-2);
        cached=ImageTexture.CreateFromImage(canvas);interfaceFrames.Add(key,cached);TexturesCreated++;return cached;
    }
    bool LoadGround()
    {
        if(groundChecked)return groundAtlas is not null;
        groundChecked=true;
        const string folder="res://Assets/Generated/Terrain/";
        if(!GFile.FileExists(folder+"catalog.json"))return false;
        using var doc=JsonDocument.Parse(GFile.GetFileAsString(folder+"catalog.json"));
        using var pixels=LoadImage(folder+doc.RootElement.GetProperty("atlas").GetString());
        pixels.Convert(Image.Format.Rgba8);var rgba=pixels.GetData();var atlasWidth=pixels.GetWidth();
        groundAtlas=ImageTexture.CreateFromImage(pixels);TexturesCreated++;
        foreach(var group in doc.RootElement.GetProperty("groups").EnumerateObject())
        {
            var regions=group.Value.EnumerateArray().Select(Rect).ToArray();groundRegions.Add(group.Name,regions);
            foreach(var region in regions)CacheGroundUnderfill(rgba,atlasWidth,region);
        }
        foreach(var tile in doc.RootElement.GetProperty("tiles").EnumerateArray())
        {
            var key=(System.IO.Path.GetFileNameWithoutExtension(tile.GetProperty("file").GetString()!),tile.GetProperty("frame").GetInt32());
            var region=Rect(tile.GetProperty("region"));terrainTemplates[key]=region;
            terrainUnderfills[key]=CacheGroundUnderfill(rgba,atlasWidth,region);
        }
        return true;
    }
    Color CacheGroundUnderfill(byte[] rgba,int width,Rect2 region)
    {
        if(groundUnderfills.TryGetValue(region,out var cached))return cached;
        long red=0,green=0,blue=0,count=0;
        for(var y=(int)region.Position.Y;y<(int)region.End.Y;y++)for(var x=(int)region.Position.X;x<(int)region.End.X;x++)
        {
            var i=(y*width+x)*4;if(rgba[i+3]==0)continue;
            red+=rgba[i];green+=rgba[i+1];blue+=rgba[i+2];count++;
        }
        cached=count==0?new Color("939955"):new Color(red/(255f*count),green/(255f*count),blue/(255f*count),1);
        groundUnderfills.Add(region,cached);return cached;
    }
    static Color OpaqueGroundUnderfill(Color color,Color light)
    {var result=color*light;result.A=1;return result;}
    public ClassicTerrainPlan GroundPlan(BattleMap map,uint seed)
    {
        if(ReferenceEquals(plannedMap,map) && plannedSeed==seed && terrainPlan is not null)return terrainPlan;
        if(!LoadGround())throw new InvalidDataException("原版地表图集缺失");
        using var document=JsonDocument.Parse(GFile.GetFileAsString("res://Assets/Generated/Terrain/layout.json"));
        var root=document.RootElement;
        var lat=root.GetProperty("latByMask").EnumerateArray().Select(n=>n.GetInt32()).ToArray();
        var shores=root.GetProperty("shoreByMask").EnumerateArray().Select(s=>new ClassicTerrainPlan.Shore(s.GetProperty("template").GetString()!,s.GetProperty("frames").EnumerateArray().Select(n=>n.GetInt32()).ToArray())).ToArray();
        var plan=new ClassicTerrainPlan(map,seed,lat,shores);
        for(var y=0;y<map.Height;y++)for(var x=0;x<map.Width;x++)
        {
            var tile=plan[x,y];
            if(!terrainTemplates.ContainsKey((tile.Template,tile.Frame)))throw new InvalidDataException($"地表拼接引用缺失：{tile.Template}/{tile.Frame}");
        }
        terrainPlan=plan;plannedMap=map;plannedSeed=seed;return plan;
    }
    public bool DrawTerrainTemplate(CanvasItem target,string template,int frame,Vector2 center,float zoom,Color light)
    {
        if(!LoadGround() || !terrainTemplates.TryGetValue((template,frame),out var region))return false;
        target.DrawTextureRectRegion(groundAtlas!,new Rect2(center-new Vector2(30,15)*zoom,new Vector2(60,30)*zoom),region,light);return true;
    }
    public bool DrawGround(CanvasItem target,string kind,int variation,Vector2 center,float zoom,Color light)
    {
        if(!LoadGround())return false;
        if(!groundRegions.TryGetValue(kind,out var regions) || regions.Length==0)regions=groundRegions["clear"];
        var region=regions[(variation&int.MaxValue)%regions.Length];
        target.DrawTextureRectRegion(groundAtlas!,new Rect2(center-new Vector2(30,15)*zoom,new Vector2(60,30)*zoom),region,light);
        return true;
    }
    public bool DrawScenery(CanvasItem target,string name,int frame,Vector2 bottom,float zoom,Color light)
    {
        var texture=InterfaceFrame("allied",name,frame);
        if(texture is null)return false;
        var anchor=name.StartsWith("gold",StringComparison.Ordinal)?texture.GetSize()/2:sceneryAnchors.GetValueOrDefault($"allied/{name}/{frame}",texture.GetSize()/2);
        // The retained scenery SHPs use the original second-half shadow convention.
        // Reuse its silhouette and offsets, with the same ground anchor as its color frame.
        var shadowFrame=name.StartsWith("gold",StringComparison.Ordinal)?frame+12:1;
        var shadow=InterfaceFrame("allied",name,shadowFrame);
        if(shadow is not null && shadowFrame!=frame)
            target.DrawTextureRect(shadow,new Rect2(bottom-anchor*zoom,shadow.GetSize()*zoom),false,new Color(0,0,0,.38f));
        target.DrawTextureRect(texture,new Rect2(bottom-anchor*zoom,texture.GetSize()*zoom),false,light);
        return true;
    }
    public void ValidateClassic()
    {
        if(!LoadGround() || TerrainTileCount<16)throw new InvalidDataException("原版温带地表图集缺失");
        foreach(var side in new[]{"allied","soviet"})
        foreach(var name in new[]{"radar","side1","side2","side3","tab00","tab01","tab02","tab03","repair","sell"})
            if(InterfaceFrame(side,name) is null)throw new InvalidDataException($"原版界面帧缺失：{side}/{name}");
    }
    void DisposeClassic()
    {
        foreach(var frame in interfaceFrames.Values)frame.Dispose();
        interfaceFrames.Clear();sceneryAnchors.Clear();groundAtlas?.Dispose();groundAtlas=null;groundRegions.Clear();terrainTemplates.Clear();terrainUnderfills.Clear();groundUnderfills.Clear();terrainPlan=null;plannedMap=null;DisposeOriginalTerrain();
    }
}
