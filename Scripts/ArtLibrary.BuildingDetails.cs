using Godot;
using RedAlert.Simulation;
using System.Text.Json;
using GFile=Godot.FileAccess;

namespace RedAlert;

public sealed partial class ArtLibrary
{
    sealed record BuildingAnimation(string Name,int Start,int Length,int DamagedStart,int Ticks,
        bool Powered,string Condition,bool HideInactive);
    sealed record BuildingDetails(string Buildup,string? Bib,BuildingAnimation[] Layers);
    sealed class BuildingAnimationClock
    {
        public int LastTick,PoweredTicks;
    }
    readonly Dictionary<string,BuildingDetails> buildingDetails=[];
    readonly Dictionary<int,BuildingAnimationClock> buildingClocks=[];
    // Reused scratch storage: shadows are drawn below every color layer, including the bib.
    readonly List<(SpriteAtlas Sprite,int Frame,Vector2 Anchor)> buildingLayers=[];
    bool buildingDetailsChecked;
    World? buildingClockWorld;

    bool LoadBuildingDetails()
    {
        if(buildingDetailsChecked)return buildingDetails.Count>0;
        buildingDetailsChecked=true;
        const string path="res://Assets/Generated/Details/catalog.json";
        if(!GFile.FileExists(path))return false;
        using var document=JsonDocument.Parse(GFile.GetFileAsString(path));
        foreach(var item in document.RootElement.GetProperty("buildings").EnumerateObject())
        {
            var definition=item.Value;
            var animations=definition.GetProperty("layers").EnumerateArray().Select(layer=>new BuildingAnimation(
                layer.GetProperty("name").GetString()!,layer.GetProperty("start").GetInt32(),
                layer.GetProperty("length").GetInt32(),layer.GetProperty("damagedStart").GetInt32(),
                Math.Max(1,layer.GetProperty("ticks").GetInt32()),layer.GetProperty("powered").GetBoolean(),
                layer.GetProperty("condition").GetString()!,layer.GetProperty("inactive").GetString()=="hide")).ToArray();
            buildingDetails.Add(item.Name,new BuildingDetails(definition.GetProperty("buildup").GetString()!,
                definition.TryGetProperty("bib",out var bib)?bib.GetString():null,animations));
        }
        return buildingDetails.Count>0;
    }
    static int BuildingColorFrames(SpriteAtlas sprite)=>sprite.Frames.Length>0 && sprite.Frames[0].Shadow is not null?sprite.Frames.Length/2:sprite.Frames.Length;
    static int BuildingDamageFrame(SpriteAtlas sprite,Building building)
    {
        var colors=BuildingColorFrames(sprite);
        if(colors>=3 && building.Health*4<building.MaxHealth)return 2;
        return colors>=2 && building.Health*2<building.MaxHealth?1:0;
    }
    void AddBuildingLayer(SpriteAtlas sprite,int frame,Vector2 offset)
    {
        if(frame<0 || frame>=BuildingColorFrames(sprite))return;
        buildingLayers.Add((sprite,frame,new Vector2(sprite.Width/2f,sprite.Height/2f)-offset));
    }
    void DrawBuildingLayers(CanvasItem canvas,int owner,Vector2 point,float zoom)
    {
        foreach(var layer in buildingLayers)
            if(layer.Sprite.Frames[layer.Frame].Shadow is int shadow)
                DrawFrame(canvas,layer.Sprite,shadow,-1,point,layer.Anchor,zoom);
        foreach(var layer in buildingLayers)DrawFrame(canvas,layer.Sprite,layer.Frame,owner,point,layer.Anchor,zoom);
    }
    int BuildingPoweredTick(Building building,World world)
    {
        if(!ReferenceEquals(buildingClockWorld,world))
        {
            buildingClocks.Clear();buildingClockWorld=world;
        }
        if(buildingClocks.Count>world.Buildings.Count+32)
            foreach(var id in buildingClocks.Keys.Where(id=>world.Find(id) is not Building).ToArray())buildingClocks.Remove(id);
        if(!buildingClocks.TryGetValue(building.Id,out var clock))
        {
            clock=new BuildingAnimationClock{LastTick=world.Tick,PoweredTicks=world.Tick};
            buildingClocks.Add(building.Id,clock);
        }
        if(world.Tick<clock.LastTick)clock.PoweredTicks=world.Tick;
        else if(world.Players[building.Owner].Powered)clock.PoweredTicks+=world.Tick-clock.LastTick;
        clock.LastTick=world.Tick;
        return clock.PoweredTicks;
    }
    static bool BuildingMinerDocking(Building building,World world)
    {
        var x=building.CellLeft+building.Definition.Width;var y=building.CellTop+building.Definition.Height;
        return world.Units.Any(unit=>unit.Alive && unit.Owner==building.Owner && unit.Definition.Miner &&
            unit.Returning && unit.Cargo>0 && Math.Abs(unit.CellX-x)<=1 && Math.Abs(unit.CellY-y)<=1);
    }
    bool DrawBuildingArtwork(CanvasItem canvas,Building building,string name,SpriteAtlas sprite,
        Vector2 offset,Vector2 point,float zoom,World world)
    {
        LoadBuildingDetails();buildingDetails.TryGetValue(name,out var details);
        buildingLayers.Clear();
        var bib=details?.Bib;
        // Retain the old foundation path if an optional detail catalog is absent.
        if(bib is null && name is "ggrefn" or "ngrefn" or "ggweap" or "ngweap")bib=name+"bb";
        if(!building.Complete)
        {
            if(details is null || Sprite(details.Buildup) is not {} buildup)return false;
            if(bib is not null && Sprite(bib) is {} foundation)AddBuildingLayer(foundation,0,offset);
            var total=Math.Max(1,building.TotalBuildTicks);
            var elapsed=Math.Clamp(total-building.RemainingBuildTicks,0,total);
            var frame=Math.Min(BuildingColorFrames(buildup)-1,(int)((long)elapsed*BuildingColorFrames(buildup)/total));
            AddBuildingLayer(buildup,frame,offset);
            DrawBuildingLayers(canvas,building.Owner,point,zoom);
            return true;
        }
        if(bib is not null && Sprite(bib) is {} baseSprite)
            AddBuildingLayer(baseSprite,BuildingDamageFrame(baseSprite,building),offset);
        AddBuildingLayer(sprite,BuildingDamageFrame(sprite,building),offset);
        if(details is not null && details.Layers.Length>0)
        {
            var poweredTick=BuildingPoweredTick(building,world);
            var damaged=building.Health*2<building.MaxHealth;
            var constructing=building.Type==BuildingType.ConstructionYard && world.Buildings.Any(other=>
                other.Alive && other.Owner==building.Owner && !other.Complete);
            var docking=building.Type==BuildingType.Refinery && BuildingMinerDocking(building,world);
            foreach(var animation in details.Layers)
            {
                var active=animation.Condition switch
                {
                    "construction"=>constructing,
                    "idleConstruction"=>!constructing,
                    "docking"=>docking,
                    _=>true
                };
                if(!active && animation.HideInactive)continue;
                var detail=Sprite(animation.Name);if(detail is null)continue;
                var tick=animation.Powered?poweredTick:world.Tick;
                var frame=(damaged?animation.DamagedStart:animation.Start)+
                    (active?(int)(((long)Math.Max(0,tick)/animation.Ticks+building.Id)%animation.Length):0);
                AddBuildingLayer(detail,frame,offset);
            }
        }
        DrawBuildingLayers(canvas,building.Owner,point,zoom);
        return true;
    }
    void ValidateBuildingDetails()
    {
        if(!LoadBuildingDetails())throw new InvalidDataException("原版建筑附加动画目录缺失");
        foreach(var (name,details) in buildingDetails)
        {
            var make=Sprite(details.Buildup)??throw new InvalidDataException("原版建造动画缺失："+name);
            if(BuildingColorFrames(make)<2)throw new InvalidDataException("原版建造帧数无效："+name);
            foreach(var animation in details.Layers)
            {
                var sprite=Sprite(animation.Name)??throw new InvalidDataException("原版建筑叠加层缺失："+animation.Name);
                if(animation.Length<=0 || animation.Start<0 || animation.DamagedStart<0 ||
                    animation.Start+animation.Length>BuildingColorFrames(sprite) ||
                    animation.DamagedStart+animation.Length>BuildingColorFrames(sprite))
                    throw new InvalidDataException("原版建筑序列越界："+animation.Name);
            }
        }
    }
    void DisposeBuildingDetails()
    {
        buildingLayers.Clear();buildingClocks.Clear();buildingDetails.Clear();buildingClockWorld=null;
    }
}
