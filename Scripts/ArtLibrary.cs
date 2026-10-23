using Godot;
using RedAlert.Simulation;
using System.Text.Json;
using GFile=Godot.FileAccess;

namespace RedAlert;

// Presentation-only decoding/cache. No pixels, animation clocks or floating-point angles feed simulation.
public sealed partial class ArtLibrary : IDisposable
{
    readonly Dictionary<string,SpriteAtlas> sprites=[];
    readonly Dictionary<string,VoxelAtlas> vehicles=[];
    readonly Dictionary<string,ImageTexture> cameos=[];
    readonly HashSet<string> unavailable=[];
    public int SpriteCount=>sprites.Count;
    public int VehicleCount=>vehicles.Count;
    public int TexturesCreated {get;private set;}
    readonly Dictionary<string,Vector2> offsets=[];
    public ArtLibrary()
    {
        const string path="res://Assets/Generated/Sprites/layout.json";
        if(!GFile.FileExists(path))return;
        using var data=JsonDocument.Parse(GFile.GetFileAsString(path));
        foreach(var item in data.RootElement.GetProperty("offsets").EnumerateObject())offsets.Add(item.Name,new(item.Value.GetProperty("x").GetSingle(),item.Value.GetProperty("y").GetSingle()));
        if(offsets.TryGetValue("tgsla",out var coil))offsets["ngtsla"]=coil;
    }

    sealed record Frame(Rect2 Source,Rect2 Image,bool Data,int? Shadow);
    sealed class PixelAtlas
    {
        public required byte[] Pixels;
        public required byte[] Mask;
        public int Width,Height;
        public readonly Dictionary<int,ImageTexture> Textures=[];
    }
    sealed class SpriteAtlas
    {
        public int Width,Height;
        public required Frame[] Frames;
        public required PixelAtlas Pixels;
    }
    sealed class VoxelAtlas
    {
        public int Directions,Columns,Width,Height;
        public Vector2 Anchor;
        public readonly Dictionary<string,PixelAtlas> Parts=[];
    }
    static Image LoadImage(string path)
    {
        var absolute=ProjectSettings.GlobalizePath(path);
        if(System.IO.File.Exists(absolute))return Image.LoadFromFile(absolute);
        using var texture=GD.Load<Texture2D>(path)??throw new InvalidDataException("图片资源不存在："+path);
        return texture.GetImage()??throw new InvalidDataException("无法读取图片："+path);
    }
    static PixelAtlas ReadPixels(string colorPath,string? maskPath=null,byte[]? rawMask=null)
    {
        using var image=LoadImage(colorPath);image.Convert(Image.Format.Rgba8);
        var w=image.GetWidth();var h=image.GetHeight();
        if(w<=0 || h<=0 || (long)w*h>16_777_216)throw new InvalidDataException("图集尺寸无效");
        var mask=rawMask??new byte[w*h];
        if(maskPath is not null)
        {
            using var data=LoadImage(maskPath);data.Convert(Image.Format.L8);
            if(data.GetWidth()!=w || data.GetHeight()!=h)throw new InvalidDataException("换色遮罩尺寸不匹配");
            mask=data.GetData();
        }
        if(mask.Length!=w*h)throw new InvalidDataException("换色遮罩长度不匹配");
        return new PixelAtlas{Width=w,Height=h,Pixels=image.GetData(),Mask=mask};
    }
    ImageTexture Texture(PixelAtlas atlas,int owner)
    {
        if(atlas.Textures.TryGetValue(owner,out var texture))return texture;
        var pixels=(byte[])atlas.Pixels.Clone();
        if(owner>=0)
        {
            var tint=Battlefield.PlayerColors[owner%8];
            for(var i=0;i<atlas.Mask.Length;i++)
            {
                if(atlas.Mask[i]==0 || pixels[i*4+3]==0)continue;
                var p=i*4;var brightness=Math.Max(pixels[p],Math.Max(pixels[p+1],pixels[p+2]))/252f;
                pixels[p]=(byte)Math.Clamp((int)(tint.R*255*brightness),0,255);pixels[p+1]=(byte)Math.Clamp((int)(tint.G*255*brightness),0,255);pixels[p+2]=(byte)Math.Clamp((int)(tint.B*255*brightness),0,255);
            }
        }
        using var image=Image.CreateFromData(atlas.Width,atlas.Height,false,Image.Format.Rgba8,pixels);
        texture=ImageTexture.CreateFromImage(image);atlas.Textures.Add(owner,texture);TexturesCreated++;return texture;
    }
    static Rect2 Rect(JsonElement e)=>new(e.GetProperty("x").GetInt32(),e.GetProperty("y").GetInt32(),e.GetProperty("width").GetInt32(),e.GetProperty("height").GetInt32());
    SpriteAtlas? Sprite(string name)
    {
        if(sprites.TryGetValue(name,out var sprite))return sprite;
        if(unavailable.Contains("s/"+name))return null;
        var folder=OriginalMapRockSpriteFolder(name)??OriginalMapObjectSpriteFolder(name)??ExpandedSpriteFolder(name)??$"res://Assets/Generated/Sprites/{name}/";
        if(!GFile.FileExists(folder+"manifest.json"))folder=$"res://Assets/Generated/Details/{name}/";
        if(!GFile.FileExists(folder+"manifest.json")){unavailable.Add("s/"+name);return null;}
        using var json=JsonDocument.Parse(GFile.GetFileAsString(folder+"manifest.json"));var root=json.RootElement;
        var image=root.GetProperty("images")[0];
        var frames=root.GetProperty("frames").EnumerateArray().Select(f=>new Frame(Rect(f.GetProperty("sourceRect")),Rect(f.GetProperty("imageRect")),f.GetProperty("hasData").GetBoolean(),f.GetProperty("pairedFrame").ValueKind==JsonValueKind.Number?f.GetProperty("pairedFrame").GetInt32():null)).ToArray();
        sprite=new SpriteAtlas{Width=root.GetProperty("canvasWidth").GetInt32(),Height=root.GetProperty("canvasHeight").GetInt32(),Frames=frames,Pixels=ReadPixels(folder+image.GetProperty("color").GetString(),folder+image.GetProperty("teamMask").GetString())};
        sprites.Add(name,sprite);return sprite;
    }
    VoxelAtlas? Vehicle(string name)
    {
        if(vehicles.TryGetValue(name,out var vehicle))return vehicle;
        if(unavailable.Contains("v/"+name))return null;
        var manifest=OriginalMapObjectVehicleManifest(name)??ExpandedVehicleManifest(name)??$"res://Assets/Generated/Vehicles/{name}.json";
        if(!GFile.FileExists(manifest)){unavailable.Add("v/"+name);return null;}
        var folder=manifest[..(manifest.LastIndexOf('/')+1)];
        using var json=JsonDocument.Parse(GFile.GetFileAsString(manifest));var root=json.RootElement;var a=root.GetProperty("atlas");
        vehicle=new VoxelAtlas{Directions=root.GetProperty("directions").GetInt32(),Columns=a.GetProperty("columns").GetInt32(),Width=a.GetProperty("cellWidth").GetInt32(),Height=a.GetProperty("cellHeight").GetInt32(),Anchor=new Vector2(a.GetProperty("anchor")[0].GetSingle(),a.GetProperty("anchor")[1].GetSingle())};
        foreach(var part in root.GetProperty("parts").EnumerateArray())
            vehicle.Parts.Add(part.GetProperty("name").GetString()!,ReadPixels(folder+part.GetProperty("png").GetString(),rawMask:GFile.GetFileAsBytes(folder+part.GetProperty("teamRaw").GetString())));
        vehicles.Add(name,vehicle);return vehicle;
    }
    void DrawFrame(CanvasItem canvas,SpriteAtlas sprite,int frame,int owner,Vector2 anchorPoint,Vector2 anchor,float scale)
    {
        if(frame<0 || frame>=sprite.Frames.Length)return;
        var f=sprite.Frames[frame];if(!f.Data || f.Image.Size.X==0 || f.Image.Size.Y==0)return;
        var destination=new Rect2(anchorPoint+(f.Source.Position-anchor)*scale,f.Image.Size*scale);
        canvas.DrawTextureRectRegion(Texture(sprite.Pixels,owner),destination,f.Image);
    }
    public bool DrawInfantry(CanvasItem canvas,Unit unit,Vector2 point,float zoom,int tick,Country country,int drawOwner=-1)
    {
        if(DrawExpandedInfantry(canvas,unit,point,zoom,tick,country,drawOwner))return true;
        var name=unit.Type switch{UnitType.Gi=>"gi",UnitType.Conscript=>"cons",UnitType.Sniper=>"snipe",UnitType.Engineer=>"engineer",UnitType.Dog=>Rules.Side(country)==Faction.Allied?"adog":"dog",_=>""};
        if(name.Length==0)return false;
        var sprite=Sprite(name);if(sprite is null)return false;
        var sx=unit.FacingX-unit.FacingY;var sy=(unit.FacingX+unit.FacingY)*.5f;
        var direction=((int)MathF.Round(-(MathF.Atan2(sy,sx)+MathF.PI/2)*4/MathF.PI)+8)%8;
        var moving=unit.PathIndex<unit.Path.Count && unit.BlockedTicks==0;
        var frame=unit.Type!=UnitType.Dog && tick-unit.LastFiredTick<6?164+direction*6+Math.Max(0,tick-unit.LastFiredTick)%6:moving?8+direction*6+(tick/2+unit.Id)%6:direction;
        var anchor=new Vector2(sprite.Width/2f,sprite.Height/2f+3);
        if(sprite.Frames[frame].Shadow is int shadow)DrawFrame(canvas,sprite,shadow,-1,point,anchor,zoom);
        DrawFrame(canvas,sprite,frame,unit.Owner,point,anchor,zoom);return true;
    }
    public bool DrawBuilding(CanvasItem canvas,Building building,Country country,Vector2 point,float zoom,World world)
    {
        if(DrawExpandedBuilding(canvas,building,country,point,zoom,world))return true;
        var allied=Rules.Side(country)==Faction.Allied;
        var name=building.Type switch
        {
            BuildingType.ConstructionYard=>allied?"ggcnst":"ngcnst",BuildingType.PowerPlant=>allied?"ggpowr":"ngpowr",BuildingType.Barracks=>allied?"ggpile":"nghand",BuildingType.Refinery=>allied?"ggrefn":"ngrefn",BuildingType.WarFactory=>allied?"ggweap":"ngweap",BuildingType.GrandCannon=>"gggcan",BuildingType.TeslaCoil=>"ngtsla",BuildingType.Pillbox=>"ggpill",BuildingType.Radar=>allied?"ggairc":"ngradr",BuildingType.BattleLab=>allied?"ggtech":"ngtech",_=>""
        };
        if(name.Length==0)return false;
        var sprite=Sprite(name);if(sprite is null)return false;
        var d=building.Definition;var offset=offsets.GetValueOrDefault(name,new Vector2(0,-(sprite.Height/2f-(d.Width+d.Height)*7.5f)));
        if(!DrawBuildingArtwork(canvas,building,name,sprite,offset,point,zoom,world))return false;
        if(!building.Complete)return true;
        if(building.Type==BuildingType.GrandCannon && Vehicle("grand-cannon") is {} gun)
        {
            var target=world.Find(building.LastTarget);var direction=target is null?0:Direction(target.X-building.X,target.Y-building.Y,gun.Directions);
            DrawPart(canvas,gun,"turret",direction,building.Owner,point,zoom);DrawPart(canvas,gun,"barrel",direction,building.Owner,point,zoom);
        }
        return true;
    }
    void DrawPart(CanvasItem canvas,VoxelAtlas vehicle,string part,int direction,int owner,Vector2 point,float zoom)
    {
        if(!vehicle.Parts.TryGetValue(part,out var pixels))return;
        var source=new Rect2(direction%vehicle.Columns*vehicle.Width,direction/vehicle.Columns*vehicle.Height,vehicle.Width,vehicle.Height);
        canvas.DrawTextureRectRegion(Texture(pixels,owner),new Rect2(point-vehicle.Anchor*zoom,new Vector2(vehicle.Width,vehicle.Height)*zoom),source);
    }
    public bool DrawVehicle(CanvasItem canvas,Unit unit,Vector2 point,float zoom,World world,int drawOwner=-1)
    {
        if(DrawExpandedVehicle(canvas,unit,point,zoom,world,drawOwner))return true;
        var name=unit.Type switch{UnitType.Grizzly=>"grizzly",UnitType.Rhino=>"rhino",UnitType.ChronoMiner=>"chrono-miner",UnitType.WarMiner=>"war-miner",UnitType.TeslaTank=>"tesla-tank",UnitType.Ifv=>"ifv",UnitType.FlakTrack=>"flak-track",UnitType.PrismTank=>"prism-tank",UnitType.Apocalypse=>"apocalypse",_=>""};
        if(name.Length==0)return false;
        var vehicle=Vehicle(name);if(vehicle is null)return false;
        var body=Direction(unit.BodyFacingX,unit.BodyFacingY,vehicle.Directions);var turret=Direction(unit.FacingX,unit.FacingY,vehicle.Directions);
        void Part(string part,int direction)=>DrawPart(canvas,vehicle,part,direction,unit.Owner,point,zoom);
        Part("body",body);
        var away=(unit.FacingX+unit.FacingY)<0;
        if(away)Part("barrel",turret);
        Part("turret",turret);
        if(!away)Part("barrel",turret);
        return true;
    }
    static int Direction(int dx,int dy,int count)
    {
        var angle=MathF.Atan2(-(dx+dy),dx-dy);return ((int)MathF.Round(angle*count/MathF.Tau)+count)%count;
    }
    public Texture2D? Cameo(string name)
    {
        name=name.Trim().ToLowerInvariant();if(name.EndsWith(".shp",StringComparison.Ordinal))name=name[..^4];
        if(name.Length==0)return null;
        if(cameos.TryGetValue(name,out var texture))return texture;
        var path=$"res://Assets/Generated/ClassicExpansion/Cameos/{name}.png";
        if(!GFile.FileExists(path))path=$"res://Assets/Reference/Cameos/{name}.png";
        if(!GFile.FileExists(path))return null;
        var pixels=ReadPixels(path);texture=Texture(pixels,-1);cameos.Add(name,texture);return texture;
    }
    public static string BuildingCameo(BuildingType type,Country country)
    {
        var a=Rules.Side(country)==Faction.Allied;
        return type switch{BuildingType.PowerPlant=>a?"powricon":"npwricon",BuildingType.Barracks=>a?"brrkicon":"handicon",BuildingType.Refinery=>a?"reficon":"nreficon",BuildingType.WarFactory=>a?"gwepicon":"nwepicon",BuildingType.ConstructionYard=>a?"mcvicon":"smcvicon",BuildingType.Radar=>a?"heliicon":"nradicon",BuildingType.BattleLab=>a?"techicon":"ntchicon",BuildingType.GrandCannon=>"gcanicon",BuildingType.Pillbox=>"pillicon",BuildingType.TeslaCoil=>"tslaicon",BuildingType.Wall=>a?"wallicon":"nwalicon",_=>""};
    }
    public static string UnitCameo(UnitType type)=>type switch{UnitType.Gi=>"giicon",UnitType.Conscript=>"e2icon",UnitType.Grizzly=>"gtnkicon",UnitType.Rhino=>"htnkicon",UnitType.ChronoMiner=>"ahrvicon",UnitType.WarMiner=>"harvicon",UnitType.Sniper=>"snipicon",UnitType.TeslaTank=>"ttnkicon",_=>""};
    public void Validate()
    {
        foreach(var name in new[]{"gi","cons","snipe","engineer","adog","dog","ggpowr","ggpile","ggrefn","ggrefnbb","ggweap","ggweapbb","ngpowr","nghand","ngrefn","ngrefnbb","ngweap","ngweapbb","ggcnst","ngcnst","gggcan","ngtsla","ggpill","ggairc","ngradr","ggtech","ngtech"})
        {
            var sprite=Sprite(name)??throw new InvalidDataException("必要精灵缺失："+name);
            _=Texture(sprite.Pixels,0);
            foreach(var f in sprite.Frames)if(f.Image.Position.X<0 || f.Image.Position.Y<0 || f.Image.End.X>sprite.Pixels.Width || f.Image.End.Y>sprite.Pixels.Height)throw new InvalidDataException("精灵裁剪越界："+name);
        }
        foreach(var name in new[]{"grizzly","rhino","chrono-miner","war-miner","tesla-tank","ifv","flak-track","prism-tank","apocalypse","grand-cannon"})
        {
            var vehicle=Vehicle(name)??throw new InvalidDataException("必要车辆缺失："+name);
            if(vehicle.Directions!=32 || (name!="grand-cannon" && !vehicle.Parts.ContainsKey("body")))throw new InvalidDataException("车辆朝向/车体错误");
            foreach(var part in vehicle.Parts.Values)_=Texture(part,0);
        }
        ValidateBuildingDetails();
        _=Cameo("gtnkicon")??throw new InvalidDataException("建设图标缺失");
    }
    public void Dispose()
    {
        foreach(var sprite in sprites.Values)foreach(var texture in sprite.Pixels.Textures.Values)texture.Dispose();
        foreach(var vehicle in vehicles.Values)foreach(var part in vehicle.Parts.Values)foreach(var texture in part.Textures.Values)texture.Dispose();
        foreach(var texture in cameos.Values)texture.Dispose();
        sprites.Clear();vehicles.Clear();cameos.Clear();DisposeBuildingDetails();DisposeClassic();DisposeMenuImages();DisposeOriginalMapTerrainMetadata();DisposeExpansionMetadata();DisposeOriginalMapObjectMetadata();DisposeVehiclePoses();
    }
}
