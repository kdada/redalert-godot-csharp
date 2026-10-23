using Godot;
using RedAlert.Simulation;
using System.Text.Json;
using GFile=Godot.FileAccess;

namespace RedAlert;

public sealed partial class ArtLibrary
{
    const string ExpansionCatalogPath="res://Assets/Generated/ClassicExpansion/catalog.json";
    readonly record struct ExpansionClip(int Start,int Count,int Stride);
    sealed class ExpansionVisual
    {
        public required string RulesId,Kind,Sprite,Voxel;
        public required JsonElement Data;
        public JsonElement Art,Rules;
        public readonly Dictionary<string,ExpansionClip> Clips=new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<int,string> VoxelVariants=[];
        public bool SpriteClipsReady;
        public int Facings=8;
        public Vector2? BuildingOffset;
    }
    readonly Dictionary<string,ExpansionVisual> expansionUnits=new(StringComparer.OrdinalIgnoreCase),expansionBuildings=new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,string> expansionSpriteFolders=new(StringComparer.OrdinalIgnoreCase),expansionVehiclePaths=new(StringComparer.OrdinalIgnoreCase);
    readonly List<(SpriteAtlas Sprite,int Frame,Vector2 Anchor)> expansionBuildingLayers=[];
    sealed class ExpansionAnimationClock
    {
        public bool Active;
        public int StartTick;
    }
    readonly Dictionary<(int Building,string Layer),ExpansionAnimationClock> expansionAnimationClocks=[];
    World? expansionClockWorld;
    bool expansionMetadataChecked;
    ExpansionAnimationClock ExpansionAnimationState(Building building,string layer,bool active,int clock,World world)
    {
        if(!ReferenceEquals(expansionClockWorld,world)){expansionAnimationClocks.Clear();expansionClockWorld=world;}
        var key=(building.Id,layer);
        if(!expansionAnimationClocks.TryGetValue(key,out var state))
        {state=new ExpansionAnimationClock{StartTick=clock};expansionAnimationClocks.Add(key,state);}
        if(active && !state.Active)state.StartTick=clock;
        state.Active=active;return state;
    }

    static JsonElement ExpansionProperty(JsonElement value,string name)
    {
        if(value.ValueKind!=JsonValueKind.Object)return default;
        if(value.TryGetProperty(name,out var found))return found;
        foreach(var item in value.EnumerateObject())if(item.Name.Equals(name,StringComparison.OrdinalIgnoreCase))return item.Value;
        return default;
    }
    static string ExpansionString(JsonElement value,string name,string fallback="")
    {var item=ExpansionProperty(value,name);return item.ValueKind==JsonValueKind.String?item.GetString()??fallback:fallback;}
    static int ExpansionInt(JsonElement value,string name,int fallback=0)
    {var item=ExpansionProperty(value,name);return item.ValueKind==JsonValueKind.Number && item.TryGetInt32(out var number)?number:item.ValueKind==JsonValueKind.String && int.TryParse(item.GetString(),out number)?number:fallback;}
    static bool ExpansionBool(JsonElement value,string name,bool fallback=false)
    {
        var item=ExpansionProperty(value,name);
        if(item.ValueKind is JsonValueKind.True or JsonValueKind.False)return item.GetBoolean();
        if(item.ValueKind==JsonValueKind.String)return item.GetString()?.ToLowerInvariant() is "yes" or "true" or "1";
        return fallback;
    }
    static string ExpansionResource(string path)=>path.StartsWith("res://",StringComparison.Ordinal)?path:"res://"+path;
    static string ExpansionName(string name)=>System.IO.Path.GetFileNameWithoutExtension(name).ToLowerInvariant();
    bool LoadExpansionMetadata()
    {
        if(expansionMetadataChecked)return expansionUnits.Count>0;
        if(!GFile.FileExists(ExpansionCatalogPath))return false;
        using var document=JsonDocument.Parse(GFile.GetFileAsString(ExpansionCatalogPath));var root=document.RootElement;
        if(ExpansionInt(root,"schema")!=1)throw new InvalidDataException("原版完整美术目录版本无效");
        JsonDocument? supplement=null;
        const string extra="res://Assets/Generated/ClassicExpansion/consumer-metadata.json";
        if(GFile.FileExists(extra))supplement=JsonDocument.Parse(GFile.GetFileAsString(extra));
        try
        {
            foreach(var kind in new[]{"units","buildings"})
            {
                var target=kind=="units"?expansionUnits:expansionBuildings;
                foreach(var entry in root.GetProperty(kind).EnumerateObject())
                {
                    var data=entry.Value.Clone();
                    var visual=new ExpansionVisual{RulesId=entry.Name,Kind=ExpansionString(data,"kind"),Sprite=ExpansionString(data,"spriteName"),Voxel=ExpansionString(data,"voxelName"),Data=data};
                    if(supplement is not null)
                    {
                        var fields=ExpansionProperty(ExpansionProperty(supplement.RootElement,kind),entry.Name);
                        var art=ExpansionProperty(fields,"art");var rules=ExpansionProperty(fields,"rules");
                        if(art.ValueKind==JsonValueKind.Object)visual.Art=art.Clone();
                        if(rules.ValueKind==JsonValueKind.Object)visual.Rules=rules.Clone();
                    }
                    var sequences=ExpansionProperty(data,"sequence");
                    if(sequences.ValueKind==JsonValueKind.Object)foreach(var sequence in sequences.EnumerateObject())
                    {
                        if(sequence.Value.ValueKind!=JsonValueKind.Array || sequence.Value.GetArrayLength()<3)continue;
                        var values=sequence.Value;
                        if(values[0].ValueKind!=JsonValueKind.Number || values[1].ValueKind!=JsonValueKind.Number || values[2].ValueKind!=JsonValueKind.Number)continue;
                        visual.Clips[sequence.Name]=new ExpansionClip(values[0].GetInt32(),values[1].GetInt32(),values[2].GetInt32());
                    }
                    var variants=ExpansionProperty(data,"voxelVariants");
                    if(variants.ValueKind==JsonValueKind.Array)foreach(var variant in variants.EnumerateArray())
                    {var name=ExpansionString(variant,"voxelName");if(name.Length>0)visual.VoxelVariants[ExpansionInt(variant,"turretIndex")]=name;}
                    target.Add(entry.Name,visual);
                }
            }
            foreach(var entry in root.GetProperty("sprites").EnumerateObject())
            {var path=ExpansionResource(ExpansionString(entry.Value,"manifest"));expansionSpriteFolders[entry.Name]=path[..(path.LastIndexOf('/')+1)];}
            foreach(var entry in root.GetProperty("voxels").EnumerateObject())
                expansionVehiclePaths[entry.Name]=ExpansionResource(ExpansionString(entry.Value,"manifest"));
            expansionMetadataChecked=true;
            return expansionUnits.Count>0;
        }
        finally{supplement?.Dispose();}
    }
    // Called by the main partial's standard lazy loaders. These return resource paths.
    string? ExpandedSpriteFolder(string currentName)=>LoadExpansionMetadata()?expansionSpriteFolders.GetValueOrDefault(ExpansionName(currentName)):null;
    string? ExpandedVehicleManifest(string name)=>LoadExpansionMetadata()?expansionVehiclePaths.GetValueOrDefault(ExpansionName(name)):null;
    public bool HasExpandedArtwork=>LoadExpansionMetadata();
    public int ExpandedUnitVariantCount=>LoadExpansionMetadata()?expansionUnits.Count:0;
    public int ExpandedBuildingVariantCount=>LoadExpansionMetadata()?expansionBuildings.Count:0;

    static int ExpansionColorFrames(SpriteAtlas sprite)=>BuildingColorFrames(sprite);
    void PrepareExpansionSpriteClips(ExpansionVisual visual,SpriteAtlas sprite)
    {
        if(visual.SpriteClipsReady)return;visual.SpriteClipsReady=true;
        var metadata=ExpansionProperty(visual.Data,"frameMetadata");
        var walk=Math.Max(1,ExpansionInt(visual.Art,"WalkFrames",ExpansionInt(metadata,"WalkFrames",1)));
        var fire=Math.Max(0,ExpansionInt(visual.Art,"FiringFrames",ExpansionInt(metadata,"FiringFrames")));
        var stand=Math.Max(1,ExpansionInt(visual.Art,"StandingFrames",1));
        // Non-sequence SHPs describe per-facing spans. SQD has four facings;
        // DRON/DLPH have eight. Extra source death frames stay outside these spans.
        var available=Math.Max(1,ExpansionColorFrames(sprite)/(walk+fire+stand));var inferred=1;
        while(inferred<32 && inferred*2<=available)inferred*=2;
        visual.Facings=Math.Clamp(ExpansionInt(visual.Art,"Facings",visual.Clips.Count>0?8:inferred),1,32);
        if(visual.Clips.Count>0)return;
        var startStand=ExpansionInt(visual.Art,"StartStandFrame");
        var startWalk=ExpansionInt(visual.Art,"StartWalkFrame",visual.Facings*stand);
        var startFire=ExpansionInt(visual.Art,"StartFiringFrame",startWalk+visual.Facings*walk);
        visual.Clips["Ready"]=new(startStand,stand,stand);visual.Clips["Walk"]=new(startWalk,walk,walk);
        if(fire>0)visual.Clips["FireUp"]=new(startFire,fire,fire);
    }
    static ExpansionClip ExpansionSequence(ExpansionVisual visual,params string[] actions)
    {foreach(var action in actions)if(visual.Clips.TryGetValue(action,out var clip) && clip.Count>0)return clip;return new ExpansionClip(0,1,1);}
    static bool ExpansionHasSequence(ExpansionVisual visual,string action)=>visual.Clips.TryGetValue(action,out var clip) && clip.Count>0;
    bool DrawExpandedSpriteUnit(CanvasItem canvas,ExpansionVisual visual,int owner,int id,int dx,int dy,bool moving,bool deployed,int tick,int lastFiredTick,bool infantry,Vector2 point,float zoom)
    {
        var sprite=Sprite(visual.Sprite);if(sprite is null)return false;
        PrepareExpansionSpriteClips(visual,sprite);
        var fired=ExpansionSequence(visual,deployed?"DeployedFire":"FireUp",deployed?"FireProne":"Fire","Attack");
        var elapsed=tick-lastFiredTick;var firing=elapsed>=0 && elapsed<fired.Count;
        ExpansionClip clip;int phase;
        if(deployed)
        {
            clip=firing?fired:ExpansionSequence(visual,"Deployed","Prone","Deploy","Ready");
            phase=firing?elapsed:0;
        }
        else if(firing){clip=fired;phase=elapsed;}
        else if(moving){clip=ExpansionSequence(visual,"Walk","Crawl","Ready");phase=tick/2+id;}
        else
        {
            clip=ExpansionSequence(visual,"Ready","Guard");phase=0;
            var idle=(tick+id*29)%360;var idleName=(id&1)==0?"Idle1":"Idle2";
            if(ExpansionHasSequence(visual,idleName) && visual.Clips[idleName] is var alternate && idle<alternate.Count)
            {clip=alternate;phase=idle;}
        }
        var sx=dx-dy;var sy=(dx+dy)*.5f;var count=visual.Facings;
        var direction=((int)MathF.Round(-(MathF.Atan2(sy,sx)+MathF.PI/2)*count/MathF.Tau)+count)%count;
        var frame=clip.Start+direction*clip.Stride+Math.Max(0,phase)%Math.Max(1,clip.Count);
        if(frame<0 || frame>=ExpansionColorFrames(sprite))
        {var ready=ExpansionSequence(visual,"Ready","Guard");frame=Math.Clamp(ready.Start+direction*ready.Stride,0,Math.Max(0,ExpansionColorFrames(sprite)-1));}
        var anchor=new Vector2(sprite.Width/2f,sprite.Height/2f+(infantry?3:0));
        if(sprite.Frames[frame].Shadow is int shadow)DrawFrame(canvas,sprite,shadow,-1,point,anchor,zoom);
        DrawFrame(canvas,sprite,frame,owner,point,anchor,zoom);return true;
    }
    public bool DrawExpandedInfantry(CanvasItem canvas,Unit unit,Vector2 point,float zoom,int tick,Country country,int drawOwner=-1)
    {
        if(!LoadExpansionMetadata() || !expansionUnits.TryGetValue(unit.Definition.RulesId,out var visual) || visual.Kind!="sprite")return false;
        var moving=unit.PathIndex<unit.Path.Count && unit.BlockedTicks==0;
        return DrawExpandedSpriteUnit(canvas,visual,drawOwner>=0?drawOwner:unit.Owner,unit.Id,moving?unit.BodyFacingX:unit.FacingX,moving?unit.BodyFacingY:unit.FacingY,moving,unit.Deployed,tick,unit.LastFiredTick,true,point,zoom);
    }
    string ExpansionVehicleVariant(ExpansionVisual visual,Unit unit,World world)
    {
        var empty=ExpansionString(visual.Data,"noSpawnVoxelName");
        if(empty.Length>0 && unit.LastFiredTick>=0 && (unit.Reload>0 || world.ClassicProjectiles.Any(projectile=>projectile.Alive && projectile.SourceId==unit.Id)))return empty;
        var turret=ExpansionProperty(visual.Data,"turretAndWeapons");var mapping=ExpansionProperty(turret,"turretMappings");var index=ExpansionInt(mapping,"NormalTurretIndex");
        if(ExpansionBool(turret,"isChargeTurret") && unit.TargetId>0)
        {
            var count=Math.Max(1,ExpansionInt(turret,"turretCount",1));var reload=Math.Max(1,unit.Definition.Reload);
            index=Math.Min(count-1,(reload-Math.Clamp(unit.Reload,0,reload))*count/reload);
            if(unit.LastFiredTick>=0 && world.Tick-unit.LastFiredTick<2)index=count-1;
        }
        if(unit.Passengers.Count>0 && world.Find(unit.Passengers[0]) is Unit passenger && expansionUnits.TryGetValue(passenger.Definition.RulesId,out var infantry))
        {
            var mode=ExpansionInt(infantry.Rules,"IFVMode",-1);
            if(mode>=0 && mapping.ValueKind==JsonValueKind.Object)foreach(var entry in mapping.EnumerateObject())
                if(entry.Name.EndsWith("TurretWeapon",StringComparison.Ordinal) && entry.Value.TryGetInt32(out var weapon) && weapon==mode)
                {index=ExpansionInt(mapping,entry.Name[..^6]+"Index");break;}
        }
        return visual.VoxelVariants.GetValueOrDefault(index,visual.Voxel);
    }
    bool DrawExpansionVoxel(CanvasItem canvas,string name,int bodyX,int bodyY,int turretX,int turretY,int owner,Vector2 point,float zoom)
    {
        var vehicle=Vehicle(name);if(vehicle is null)return false;
        var body=Direction(bodyX,bodyY,vehicle.Directions);var turret=Direction(turretX,turretY,vehicle.Directions);
        DrawPart(canvas,vehicle,"body",body,owner,point,zoom);var away=turretX+turretY<0;
        if(away)DrawPart(canvas,vehicle,"barrel",turret,owner,point,zoom);
        DrawPart(canvas,vehicle,"turret",turret,owner,point,zoom);
        if(!away)DrawPart(canvas,vehicle,"barrel",turret,owner,point,zoom);return true;
    }
    public bool DrawExpandedVehicle(CanvasItem canvas,Unit unit,Vector2 point,float zoom,World world,int drawOwner=-1)
    {
        if(!LoadExpansionMetadata() || !expansionUnits.TryGetValue(unit.Definition.RulesId,out var visual))return false;
        var owner=drawOwner>=0?drawOwner:unit.Owner;
        if(visual.Kind=="sprite")return DrawExpandedSpriteUnit(canvas,visual,owner,unit.Id,unit.FacingX,unit.FacingY,unit.PathIndex<unit.Path.Count && unit.BlockedTicks==0,unit.Deployed,world.Tick,unit.LastFiredTick,false,point,zoom);
        return DrawExpansionVoxel(canvas,ExpansionVehicleVariant(visual,unit,world),unit.BodyFacingX,unit.BodyFacingY,unit.FacingX,unit.FacingY,owner,point,zoom);
    }
    public bool DrawExpandedProjectile(CanvasItem canvas,ClassicProjectile projectile,Vector2 projectedAirPoint,float zoom,World world,int drawOwner=-1)
    {
        if(!LoadExpansionMetadata() || !expansionUnits.TryGetValue(projectile.Kind,out var visual))return false;
        var dx=projectile.ImpactX-projectile.X;var dy=projectile.ImpactY-projectile.Y;
        if(projectile.Returning && world.Find(projectile.SourceId) is {} source){dx=source.X-projectile.X;dy=source.Y-projectile.Y;}
        var owner=drawOwner>=0?drawOwner:projectile.Owner;
        return visual.Kind=="voxel"?DrawExpansionVoxel(canvas,visual.Voxel,dx,dy,dx,dy,owner,projectedAirPoint,zoom):DrawExpandedSpriteUnit(canvas,visual,owner,projectile.Id,dx,dy,true,false,world.Tick,-100,false,projectedAirPoint,zoom);
    }

    void AddExpansionBuildingLayer(SpriteAtlas sprite,int frame,Vector2 offset)
    {
        if(frame<0 || frame>=ExpansionColorFrames(sprite))return;
        expansionBuildingLayers.Add((sprite,frame,new Vector2(sprite.Width/2f,sprite.Height/2f)-offset));
    }
    static bool ExpansionDamagedReference(string name)=>name.EndsWith("Damaged",StringComparison.OrdinalIgnoreCase);
    Vector2 ExpansionBuildingOffset(ExpansionVisual visual,SpriteAtlas sprite,Building building)
    {
        if(visual.BuildingOffset is {} cached)return cached;
        if(offsets.TryGetValue(visual.Sprite,out var supplied)){visual.BuildingOffset=supplied;return supplied;}
        // Source canvases can contain large transparent margins (and NAMISL's
        // original base is completely empty). Use retained native color crop feet.
        var lowest=float.NegativeInfinity;
        void Observe(SpriteAtlas item,int frame,float yOffset=0)
        {if(frame>=0 && frame<item.Frames.Length && item.Frames[frame].Data)lowest=Math.Max(lowest,item.Frames[frame].Source.End.Y-item.Height/2f+yOffset);}
        Observe(sprite,0);
        var layers=ExpansionProperty(visual.Data,"layers");
        if(!sprite.Frames[0].Data && layers.ValueKind==JsonValueKind.Array)foreach(var layer in layers.EnumerateArray())
        {
            var role=ExpansionString(layer,"role");var reference=ExpansionString(layer,"referenceProperty");
            if(role=="construction" || ExpansionDamagedReference(reference) || reference.StartsWith("ProductionAnim",StringComparison.OrdinalIgnoreCase) || reference.StartsWith("SpecialAnim",StringComparison.OrdinalIgnoreCase))continue;
            var name=ExpansionString(layer,"spriteName");if(name.Length==0 || Sprite(name) is not {} art)continue;
            Observe(art,Math.Max(0,ExpansionInt(layer,"start",ExpansionInt(layer,"loopStart"))),ExpansionInt(ExpansionProperty(layer,"relativeOffsets"),"y"));
        }
        if(float.IsNegativeInfinity(lowest))lowest=sprite.Height/2f;
        var result=new Vector2(0,(building.Definition.Width+building.Definition.Height)*7.5f-lowest);
        visual.BuildingOffset=result;return result;
    }
    int ExpansionWallFrame(ExpansionVisual visual,Building building,SpriteAtlas sprite,World world)
    {
        if(ExpansionString(visual.Art,"ToOverlay").Length==0)return building.Health*2<building.MaxHealth && ExpansionColorFrames(sprite)>1?1:0;
        var mask=0;
        var neighbors=new[]{(0,-1,1),(1,0,2),(0,1,4),(-1,0,8)};
        foreach(var (dx,dy,bit) in neighbors)
            if(world.Buildings.Any(other=>other.Alive && other.Owner==building.Owner && other.Definition.RulesId==building.Definition.RulesId && other.CellLeft==building.CellLeft+dx && other.CellTop==building.CellTop+dy))mask|=bit;
        var levels=Math.Max(1,ExpansionInt(visual.Art,"DamageLevels",1));var stage=building.Health*4<building.MaxHealth?2:building.Health*2<building.MaxHealth?1:0;
        return Math.Clamp(mask+Math.Min(stage,levels-1)*16,0,Math.Max(0,ExpansionColorFrames(sprite)-1));
    }
    public bool DrawExpandedBuilding(CanvasItem canvas,Building building,Country country,Vector2 point,float zoom,World world,int drawOwner=-1)
    {
        if(!LoadExpansionMetadata() || !expansionBuildings.TryGetValue(building.Definition.RulesId,out var visual))return false;
        var sprite=Sprite(visual.Sprite);if(sprite is null)return false;
        var offset=ExpansionBuildingOffset(visual,sprite,building);var layers=ExpansionProperty(visual.Data,"layers");
        expansionBuildingLayers.Clear();var owner=drawOwner>=0?drawOwner:building.Owner;
        var damaged=building.Health*2<building.MaxHealth;
        JsonElement[] sourceLayers=layers.ValueKind==JsonValueKind.Array?layers.EnumerateArray().ToArray():[];
        foreach(var layer in sourceLayers)if(ExpansionString(layer,"role")=="bib" && Sprite(ExpansionString(layer,"spriteName")) is {} bib)
            AddExpansionBuildingLayer(bib,damaged && ExpansionColorFrames(bib)>1?1:0,offset);
        if(!building.Complete)
        {
            foreach(var layer in sourceLayers)if(ExpansionString(layer,"role")=="construction" && Sprite(ExpansionString(layer,"spriteName")) is {} buildup)
            {
                var total=Math.Max(1,building.TotalBuildTicks);var elapsed=Math.Clamp(total-building.RemainingBuildTicks,0,total);
                AddExpansionBuildingLayer(buildup,Math.Min(ExpansionColorFrames(buildup)-1,(int)((long)elapsed*ExpansionColorFrames(buildup)/total)),offset);
            }
        }
        else
        {
            AddExpansionBuildingLayer(sprite,ExpansionWallFrame(visual,building,sprite,world),offset);
            var poweredTick=BuildingPoweredTick(building,world);
            foreach(var layer in sourceLayers)
            {
                if(ExpansionString(layer,"role")!="animation" || ExpansionBool(layer,"missing"))continue;
                var reference=ExpansionString(layer,"referenceProperty");var isDamaged=ExpansionDamagedReference(reference);
                if(isDamaged && !damaged)continue;
                if(!isDamaged && damaged && sourceLayers.Any(other=>ExpansionString(other,"referenceProperty").Equals(reference+"Damaged",StringComparison.OrdinalIgnoreCase) && !ExpansionBool(other,"missing")))continue;
                var name=ExpansionString(layer,"spriteName");if(name.Length==0 || Sprite(name) is not {} detail)continue;
                var production=building.Queue.Count>0 || ExpansionBool(visual.Rules,"ConstructionYard") && world.Buildings.Any(other=>other.Alive && other.Owner==building.Owner && !other.Complete);
                var firing=world.Tick-building.LastFiredTick>=0 && world.Tick-building.LastFiredTick<40;
                var special=ExpansionBool(visual.Rules,"Refinery")?BuildingMinerDocking(building,world):ExpansionBool(visual.Rules,"UnitRepair")?world.Units.Any(unit=>unit.Alive && unit.Owner==building.Owner && unit.Health<unit.MaxHealth && Math.Abs(unit.CellX-building.CellX)<=3 && Math.Abs(unit.CellY-building.CellY)<=3):firing;
                var isProduction=reference.StartsWith("ProductionAnim",StringComparison.OrdinalIgnoreCase);
                var isSpecial=reference.StartsWith("SpecialAnim",StringComparison.OrdinalIgnoreCase);
                var isSuper=reference.StartsWith("SuperAnim",StringComparison.OrdinalIgnoreCase);
                var active=isProduction?production:isSpecial?special:isSuper?firing:true;
                var clock=ExpansionBool(layer,"powered")?poweredTick:world.Tick;
                var state=ExpansionAnimationState(building,reference,active,clock,world);
                if(!active && (isProduction || isSpecial))continue;
                var start=Math.Max(0,ExpansionInt(layer,"loopStart",ExpansionInt(layer,"start")));
                var end=ExpansionInt(layer,"loopEnd",ExpansionInt(layer,"end",start)+1);
                if(end<=start)end=start+1;var length=Math.Min(end,ExpansionColorFrames(detail))-start;if(length<=0)continue;
                var rate=Math.Max(1,ExpansionInt(layer,"rate",200));
                var phase=(int)((long)Math.Max(0,clock-state.StartTick)*rate/(Rules.TicksPerSecond*20));
                var once=ExpansionInt(layer,"loopCount",isSuper?1:-1)>0;
                if(isSuper && !active)phase=0;
                var frame=start+(once?Math.Min(length-1,phase):phase%length);var relative=ExpansionProperty(layer,"relativeOffsets");
                // ZAdjust/YSort order source layers; they are not screen offsets.
                AddExpansionBuildingLayer(detail,frame,offset+new Vector2(ExpansionInt(relative,"x"),ExpansionInt(relative,"y")));
            }
        }
        foreach(var layer in expansionBuildingLayers)if(layer.Sprite.Frames[layer.Frame].Shadow is int shadow)
            DrawFrame(canvas,layer.Sprite,shadow,-1,point,layer.Anchor,zoom);
        foreach(var layer in expansionBuildingLayers)DrawFrame(canvas,layer.Sprite,layer.Frame,owner,point,layer.Anchor,zoom);
        var turretName=ExpansionString(visual.Data,"turretVoxelName");
        if(building.Complete && turretName.Length>0 && Vehicle(turretName) is {} turret)
        {
            var target=world.Find(building.LastTarget);var direction=target is null?0:Direction(target.X-building.X,target.Y-building.Y,turret.Directions);
            var turretOffset=offset+new Vector2(ExpansionInt(visual.Art,"TurretAnimX"),ExpansionInt(visual.Art,"TurretAnimY"));
            DrawPart(canvas,turret,"turret",direction,owner,point+turretOffset*zoom,zoom);DrawPart(canvas,turret,"barrel",direction,owner,point+turretOffset*zoom,zoom);
        }
        // Empty native base frames are valid; silo body comes from SuperAnim layers.
        return true;
    }
    void DisposeExpansionMetadata()
    {expansionUnits.Clear();expansionBuildings.Clear();expansionSpriteFolders.Clear();expansionVehiclePaths.Clear();expansionBuildingLayers.Clear();expansionAnimationClocks.Clear();expansionClockWorld=null;expansionMetadataChecked=false;}
}
