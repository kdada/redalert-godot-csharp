namespace RedAlert.Simulation;

public sealed class ClassicProjectile : Entity
{
    public int SourceId,TargetId,ImpactX,ImpactY,LaunchTick,ImpactTick,Damage;
    public string Warhead="",Kind="";
    public bool Returning;
    public int TerrainImpactLevelFixed=int.MinValue;
    public override int MaxHealth=>Kind is "HORNET"?75:Kind is "ASW"?30:50;
    public override int Sight=>0;
}
public sealed record ClassicAreaEffect(int SourceId,int Owner,int X,int Y,int RadiusFixed,int CreatedTick,int ExpireTick,int Strength,string Kind)
{
    public int NextTick {get;set;}
}

public readonly record struct ClassicWeaponEvent(int Tick,int Source,int Owner,int X,int Y,UnitType? SourceUnit,BuildingType? SourceBuilding,string WeaponId);

public sealed partial class World
{
    public List<ClassicWeaponEvent> ClassicWeaponEvents {get;}=[];
    int classicWeaponEventsTick=-1;
    void EnsureClassicWeaponEventTick(){if(classicWeaponEventsTick==Tick)return;ClassicWeaponEvents.Clear();classicWeaponEventsTick=Tick;}
    void EmitClassicWeapon(Entity source,string weapon)
    {
        EnsureClassicWeaponEventTick();ClassicWeaponEvents.Add(new(Tick,source.Id,source.Owner,source.X,source.Y,source is Unit u?u.Type:null,source is Building b?b.Type:null,weapon));
    }
    public List<ClassicProjectile> ClassicProjectiles {get;}=[];
    public List<ClassicAreaEffect> ClassicAreaEffects {get;}=[];
    static int ClassicArmorIndex(Entity e)=>ClassicCombatRules.ArmorIndex(e switch{Unit u=>u.Definition.Armor,Building b=>b.Definition.Armor,ClassicProjectile p when p.Kind is "V3ROCKET" or "DMISL"=>"special_2",_=>"light"});
    static bool ClassicAirTarget(Entity e)=>e is ClassicProjectile || e is Unit {Definition.Domain:MovementDomain.Air,Altitude:>0};
    bool ClassicWaterTarget(Entity e)=>e is Building {Definition.WaterBound:true} || e is Unit u && !ClassicAirTarget(u) && Map.Inside(u.CellX,u.CellY)
        && Map.Ground[Map.Index(u.CellX,u.CellY)]==Terrain.Water && (u.Definition.Domain==MovementDomain.Water || !Map.HasBridge(u.CellX,u.CellY));
    bool ClassicSubmerged(Entity e)=>e is Unit u && u.Definition.Submersible && u.RevealedUntil<=Tick;
    static ClassicUnitTraits ClassicTraits(Entity source)=>ClassicCombatRules.UnitTraits(source switch{Unit u=>u.Definition.RulesId,Building b=>b.Definition.RulesId,_=>""});
    public bool ClassicUnitVisibleTo(int player,Unit unit)
    {
        if(!unit.Alive || unit.TransportId!=0 || unit.ParasiteHostId!=0)return false;
        if(Allied(player,unit.Owner))return true;
        return Visible(player,unit.CellX,unit.CellY) && (!ClassicSubmerged(unit) || ClassicDetected(player,unit));
    }
    public int ClassicApparentOwner(int player,Unit unit)=>unit.Type==UnitType.Spy && unit.DisguiseOwner>=0 && !Allied(player,unit.Owner) && unit.RevealedUntil<=Tick && !ClassicDetected(player,unit,true)?unit.DisguiseOwner:unit.Owner;
    public bool CanTarget(Entity source,Entity target)
    {
        if(!source.Alive || !target.Alive || source.Id==target.Id || source.Owner<0 || target.Owner<0 || source.Owner>=Players.Length || target.Owner>=Players.Length)return false;
        if(source is Unit {TransportId:not 0} or Unit {ParasiteHostId:not 0} || target is Unit {TransportId:not 0} or Unit {ParasiteHostId:not 0})return false;
        if(Allied(source.Owner,target.Owner))return source is Unit {Type:UnitType.Engineer} && target is Building || source is Unit repair && ClassicCanRepair(repair,target);
        if(source is Unit {Type:UnitType.Engineer})return target is Building;
        if(source is Unit {Type:UnitType.Spy})return target is Building b && b.Type!=BuildingType.Wall;
        if(ClassicSubmerged(target) && !ClassicDetected(source.Owner,target))return false;
        if(target is Unit {Type:UnitType.Spy,DisguiseOwner:>=0} spy && spy.RevealedUntil<=Tick && !ClassicDetected(source.Owner,spy,true))return false;
        return SelectClassicWeapon(source,target) is not null;
    }
    bool ClassicDetected(int player,Entity target,bool disguise=false)
    {
        foreach(var detector in Units)
        {
            if(!detector.Alive || detector.TransportId!=0 || !Allied(player,detector.Owner))continue;
            var traits=ClassicTraits(detector);var radius=disguise?(traits.DetectDisguise?traits.DetectRange:0):traits.Sensors;
            if(radius>0 && DistanceSquared(detector,target)<=(long)radius*radius*Rules.Scale*Rules.Scale)return true;
        }
        foreach(var detector in Buildings)
        {
            if(!ClassicBuildingOperational(detector) || !Allied(player,detector.Owner) || !Players[detector.Owner].Powered)continue;
            var traits=ClassicTraits(detector);var radius=disguise?(traits.DetectDisguise?traits.DetectRange:0):traits.Sensors;
            if(radius>0 && DistanceToTargetSquared(target,detector)<=(long)radius*radius*Rules.Scale*Rules.Scale)return true;
        }
        return false;
    }
    WeaponDef? ClassicIfvWeapon(Unit unit)=>unit.Type==UnitType.Ifv && unit.Passengers.Count>0 && Find(unit.Passengers[0]) is Unit crew?ClassicCombatRules.IfvWeapon(crew.Definition.RulesId):null;
    public bool ClassicCanRepair(Unit unit,Entity target)=>ClassicIfvWeapon(unit)?.Id.Equals("RepairBullet",StringComparison.OrdinalIgnoreCase)==true && target is Unit victim && !victim.Definition.Infantry && !ClassicAirTarget(victim)
        && !ClassicTraits(victim).Organic && Allied(unit.Owner,victim.Owner) && victim.Health<victim.MaxHealth;
    WeaponDef? SelectClassicWeapon(Entity source,Entity target)
    {
        if(source is ClassicProjectile)return null;
        WeaponDef? first=source is Unit u?u.Definition.PrimaryWeapon:source is Building b?b.Definition.PrimaryWeapon:null;
        WeaponDef? second=source is Unit su?su.Definition.SecondaryWeapon:source is Building sb?sb.Definition.SecondaryWeapon:null;
        if(source is Building coil && ClassicTeslaCharged(coil)){first=second??first;second=null;}
        if(source is Unit unit)
        {
            if(unit.Type==UnitType.Gi){first=unit.Deployed?second:first;second=null;}
            if(unit.Type is UnitType.Yuri or UnitType.YuriPrime or UnitType.Desolator)second=null;
            if(unit.Type==UnitType.Spy)return null;
            if(ClassicIfvWeapon(unit) is { } crewWeapon){first=crewWeapon;second=null;}
        }
        WeaponDef? result=null;long best=-1;
        for(var slot=0;slot<2;slot++)
        {
            var weapon=slot==0?first:second;
            if(weapon is {Damage:<0} && source is Unit repairer && ClassicCanRepair(repairer,target))return weapon;
            if(weapon is null || weapon.Damage<=0 || !ClassicWeaponSupports(source,target,weapon))continue;
            var verses=ClassicCombatRules.Warhead(weapon.Warhead).Verses[ClassicArmorIndex(target)];if(verses<=0)continue;
            var value=(long)weapon.Damage*verses;
            if(value>best){best=value;result=weapon;}
        }
        return result;
    }
    bool ClassicWeaponSupports(Entity source,Entity target,WeaponDef weapon)
    {
        var traits=ClassicTraits(source);var air=ClassicAirTarget(target);var water=ClassicWaterTarget(target);var sub=ClassicSubmerged(target);
        var id=weapon.Id.ToUpperInvariant();var warhead=weapon.Warhead.ToUpperInvariant();
        if(id=="VIRTUALSCANNER")return false;
        if(air)return weapon.AntiAir;
        if(traits.LandTargeting==1 && !water)return false;
        if(traits.NavalTargeting==6 && water)return false;
        if(id=="ASWLAUNCHER")return water && sub;
        if(sub && !weapon.AntiSubmarine && !weapon.Underwater && traits.NavalTargeting is not (2 or 3 or 5))return false;
        if(weapon.Underwater && !water)return false;
        // Sapper's Invisible5 has AG=no: C4 is a special placement action, not
        // a conventional ground projectile. Test its legal targets first.
        if(id is "SAPPER" or "FAKEC4")return target is Building || id=="SAPPER" && water;
        if(!weapon.AntiGround && !(sub && weapon.AntiSubmarine))return false;
        if(warhead.StartsWith("RAD",StringComparison.Ordinal) && ClassicTraits(target).RadImmune)return false;
        if(warhead=="CONTROLLER")
        {
            if(target is not Unit victim || ClassicTraits(victim).MindImmune || victim.ControllerId!=0)return false;
            if(source is Unit controller && controller.ControlledTargetId!=0)return false;
        }
        if(warhead=="PARASITE")return target is Unit victim && victim.Definition.Domain!=MovementDomain.Air && ClassicTraits(victim).Parasiteable;
        if(warhead=="PARASITEDOG")return target is Unit {Definition.Infantry:true};
        if(warhead=="PARASITEPLUS")return water && target is Unit {Definition.Domain:MovementDomain.Water} ship && !ship.Definition.Submersible && !ClassicTraits(ship).Organic;
        return true;
    }
    int ClassicMaximumRange(Entity source)
    {
        var first=source is Unit u?u.Definition.PrimaryWeapon:source is Building b?b.Definition.PrimaryWeapon:null;
        var second=source is Unit su?su.Definition.SecondaryWeapon:source is Building sb?sb.Definition.SecondaryWeapon:null;
        if(source is Unit ifv && ClassicIfvWeapon(ifv) is { } crew){first=crew;second=null;}
        return (Math.Max(first is null?0:ClassicWeaponRange(source,first),second is null?0:ClassicWeaponRange(source,second))+Rules.Scale-1)/Rules.Scale;
    }
    static int ClassicWeaponRange(Entity source,WeaponDef weapon)=>weapon.Id.Equals("ASWLauncher",StringComparison.OrdinalIgnoreCase)?8*Rules.Scale:Math.Max(0,weapon.RangeFixed);
    public int ClassicDamageAgainst(Entity target,int damage,string warhead)
    {
        if(warhead.StartsWith("Rad",StringComparison.OrdinalIgnoreCase) && ClassicTraits(target).RadImmune || warhead.Equals("PsiPulse",StringComparison.OrdinalIgnoreCase) && ClassicTraits(target).MindImmune)return 0;
        var rule=ClassicCombatRules.Warhead(warhead);var multiplier=rule.Verses[ClassicArmorIndex(target)];
        if(multiplier==0 || damage<=0)return 0;
        var value=(long)damage*multiplier/1000;
        if(target is Unit {Type:UnitType.Gi,Deployed:true})value=value*rule.PronePermille/1000;
        return (int)Math.Clamp(value,1,int.MaxValue);
    }
    void ApplyClassicWarhead(Entity source,Entity target,int damage,string warhead)
    {
        if(source is Unit {Kills:>=3})damage=damage*11/10;
        Damage(source,target,ClassicDamageAgainst(target,damage,warhead));
        var rule=ClassicCombatRules.Warhead(warhead);
        if(rule.SpreadFixed<=0)return;
        ApplyClassicAreaDamage(source,target.X,target.Y,damage,warhead,rule.SpreadFixed,rule.EdgePermille,target.Id,ClassicAirTarget(target));
    }
    void ApplyClassicAreaDamage(Entity source,int x,int y,int damage,string warhead,int radius,int edgePermille,int excluded=0,bool air=false,int impactSurfaceLevelFixed=int.MinValue)
    {
        foreach(var entity in Units.Cast<Entity>().Concat(Buildings).ToArray())
        {
            if(!entity.Alive || entity.Id==excluded || ClassicAirTarget(entity)!=air || entity is Unit {TransportId:not 0} or Unit {ParasiteHostId:not 0})continue;
            if(impactSurfaceLevelFixed!=int.MinValue && Math.Abs((long)TerrainEntitySurfaceFixed(entity)-impactSurfaceLevelFixed)>Rules.Scale/2)continue;
            var origin=new ClassicDamageOrigin{X=x,Y=y,Owner=source.Owner};var distance=IntegerSqrt(DistanceToTargetSquared(origin,entity));if(distance>radius)continue;
            var falloff=1000-(int)((long)(1000-edgePermille)*distance/Math.Max(1,radius));
            Damage(source,entity,ClassicDamageAgainst(entity,(int)((long)damage*falloff/1000),warhead),true);
        }
        // Resolve actor heights before a terrain hit removes their support deck.
        if(!air)ApplyTerrainAreaDamage(source,x,y,damage,warhead,radius,edgePermille);
    }
    bool TryLaunchClassicOrdnance(Unit source,Entity target,WeaponDef weapon)
    {
        string kind;int damage,count,health,reload;string warhead;
        switch(weapon.Id.ToUpperInvariant())
        {
            case "HORNETLAUNCHER":kind="HORNET";damage=40;count=3;health=75;warhead="ORCAAP";reload=50;break;
            case "ASWLAUNCHER":kind="ASW";damage=50;count=1;health=30;warhead="APSplash";reload=50;break;
            case "DREDLAUNCHER":kind="DMISL";damage=300;count=2;health=50;warhead="DMISLWH";reload=27;break;
            case "V3LAUNCHER":kind="V3ROCKET";damage=200;count=1;health=50;warhead="V3WH";reload=134;break;
            default:return false;
        }
        source.Reload=Math.Max(source.Reload,reload);
        var distance=IntegerSqrt(DistanceSquared(source,target));var travel=Math.Max(6,distance/(Rules.Scale/2));
        for(var i=0;i<count && ClassicProjectiles.Count<512;i++)
        {
            var missile=new ClassicProjectile{Id=nextId++,Owner=source.Owner,Health=health,X=source.X,Y=source.Y,SourceId=source.Id,TargetId=target.Id,ImpactX=target.X,ImpactY=target.Y,
                LaunchTick=Tick+i*2,ImpactTick=Tick+travel+i*2,Damage=damage,Warhead=warhead,Kind=kind,
                TerrainImpactLevelFixed=target is TerrainAim?Map.SurfaceHeightFixed(target.X,target.Y):int.MinValue};
            ClassicProjectiles.Add(missile);
        }
        return true;
    }
    void UpdateClassicProjectiles()
    {
        foreach(var missile in ClassicProjectiles.ToArray())
        {
            if(!missile.Alive || Players[missile.Owner].Defeated){missile.Health=0;continue;}
            if(Tick<missile.LaunchTick)continue;
            if(missile.Returning && Find(missile.SourceId) is null){missile.Health=0;continue;}
            if(Find(missile.TargetId) is { } target){missile.ImpactX=target.X;missile.ImpactY=target.Y;}
            var dx=missile.ImpactX-missile.X;var dy=missile.ImpactY-missile.Y;var remaining=Math.Max(1,missile.ImpactTick-Tick);
            missile.X+=dx/remaining;missile.Y+=dy/remaining;
            if(Tick<missile.ImpactTick)continue;
            if(missile.Returning){missile.Health=0;continue;}
            var source=Find(missile.SourceId)??new ClassicDamageOrigin{Id=missile.SourceId,Owner=missile.Owner,X=missile.X,Y=missile.Y};
            if(Find(missile.TargetId) is { } hit)ApplyClassicWarhead(source,hit,missile.Damage,missile.Warhead);
            else{var rule=ClassicCombatRules.Warhead(missile.Warhead);ApplyClassicAreaDamage(source,missile.ImpactX,missile.ImpactY,missile.Damage,missile.Warhead,rule.SpreadFixed,rule.EdgePermille,impactSurfaceLevelFixed:missile.TerrainImpactLevelFixed);}
            if((missile.Kind is "HORNET" or "ASW") && Find(missile.SourceId) is { } home)
            {
                missile.Returning=true;missile.TargetId=home.Id;missile.ImpactX=home.X;missile.ImpactY=home.Y;
                missile.ImpactTick=Tick+Math.Max(6,IntegerSqrt(DistanceSquared(missile,home))/(Rules.Scale/2));
            }
            else missile.Health=0;
        }
        ClassicProjectiles.RemoveAll(p=>!p.Alive);
    }
}
sealed class ClassicDamageOrigin : Entity
{
    public override int MaxHealth=>1;
    public override int Sight=>0;
}
internal sealed record ClassicWarhead(int[] Verses,int SpreadFixed,int EdgePermille,int PronePermille);
internal readonly record struct ClassicUnitTraits(int NavalTargeting=0,int LandTargeting=0,int Sensors=0,bool DetectDisguise=false,int DetectRange=4,bool MindImmune=false,bool RadImmune=false,bool Parasiteable=true,bool Organic=false);
internal static class ClassicCombatRules
{
    internal static int ArmorIndex(string armor)=>armor.ToLowerInvariant() switch{"none"=>0,"flak"=>1,"plate"=>2,"light"=>3,"medium"=>4,"heavy"=>5,"wood"=>6,"steel"=>7,"concrete"=>8,"special_1"=>9,"special_2"=>10,_=>0};
    internal static ClassicWarhead Warhead(string id)=>Warheads.TryGetValue(id,out var rule)?rule:Fallback;
    internal static ClassicUnitTraits UnitTraits(string id)=>Traits.TryGetValue(id,out var traits)?traits:new ClassicUnitTraits(Parasiteable:true);
    internal static readonly ClassicWarhead Fallback=new([1000,1000,1000,1000,1000,1000,1000,1000,1000,1000,1000],0,1000,1000);
    // Data only: extracted from Assets/Reference/Visual/Metadata/rules.ini.
    // Original rules SHA-256: fd1e95cea0306ea78049dc81c8cd816e18c28c496872a1ff02edd50bd082062f
    static readonly Dictionary<string,ClassicWarhead> Warheads=new(StringComparer.OrdinalIgnoreCase)
    {
        ["SonicWarhead"]=new([1000,1000,1000,1000,800,800,1000,600,600,1000,1000],102,1000,500),
        ["TankOGas"]=new([900,900,900,800,600,250,1000,500,100,900,1000],512,900,500),
        ["RailShot"]=new([1000,1000,1000,1000,1000,1000,1000,1000,1000,1000,1000],0,1000,1000),
        ["RailShot2"]=new([1000,1000,1000,1000,1000,1000,1000,1000,1000,1000,1000],0,1000,1000),
        ["SA"]=new([1000,800,700,500,250,250,750,500,250,1000,1000],0,1000,700),
        ["HARVWH"]=new([1000,800,700,500,200,200,200,150,100,2000,1000],0,1000,500),
        ["SSA"]=new([1000,1000,700,600,400,400,750,500,250,1000,1000],0,1000,500),
        ["SSAB"]=new([1000,800,700,500,250,250,750,300,200,1000,1000],0,1000,500),
        ["HE"]=new([1000,900,800,700,350,350,750,400,200,800,1000],512,500,700),
        ["V3WH"]=new([1000,900,800,900,700,700,1000,1000,500,800,0],1024,250,700),
        ["V3EWH"]=new([1000,900,800,800,700,700,1000,750,500,800,0],2048,500,700),
        ["DMISLWH"]=new([1000,900,800,1000,800,800,850,650,280,800,0],1536,250,700),
        ["DMISLEWH"]=new([1000,900,800,1000,800,800,850,650,280,800,0],3072,500,700),
        ["BlimpHE"]=new([1000,1000,1000,700,350,350,850,750,500,1000,1000],2048,500,700),
        ["KTSTLEXP"]=new([1000,1000,1000,700,350,350,850,750,500,1000,1000],2048,500,700),
        ["MaverickHE"]=new([250,250,250,1000,500,500,1000,750,500,250,1000],1024,500,700),
        ["ARTYHE"]=new([1000,800,600,1000,600,600,1000,800,600,1000,1000],1024,250,500),
        ["IonWH"]=new([1000,1000,1000,1000,1000,1000,1000,1000,30,1000,1000],2048,500,1000),
        ["AP"]=new([250,250,250,750,1000,1000,650,450,600,600,1000],307,500,500),
        ["GRIZAPE"]=new([1000,1000,1000,1000,1000,1000,650,450,600,600,1000],307,500,500),
        ["RHINAPE"]=new([1000,1000,1000,1000,1000,1000,650,450,600,600,1000],307,500,500),
        ["ApocAP"]=new([250,250,250,750,1000,1000,1000,1000,700,600,1000],307,500,500),
        ["ApocAPE"]=new([1000,1000,1000,750,1000,1000,1000,1000,700,600,1000],307,500,500),
        ["APSplash"]=new([250,250,250,750,1000,1000,650,650,600,250,1000],512,800,500),
        ["UltraAP"]=new([20,20,20,1000,400,1000,20,20,20,20,1000],0,1000,500),
        ["UltraAPE"]=new([20,20,20,1000,500,1000,20,20,20,1000,1000],0,1000,500),
        ["HowitzerWH"]=new([1000,900,800,600,400,400,500,400,250,800,1000],512,500,500),
        ["FakeC4WH"]=new([0,0,0,0,0,0,1000,1000,1000,0,1000],0,1000,1000),
        ["HollowPointNoBuilding"]=new([2000,1000,750,10,10,10,0,0,0,750,1000],0,1000,1000),
        ["RPG"]=new([1000,1000,1000,1000,1000,1000,1000,1000,1000,1000,1000],0,1000,1000),
        ["Gas"]=new([1000,1000,1000,1000,1000,1000,1000,1000,1000,1000,1000],1024,1000,3000),
        ["Fire"]=new([6000,5000,2000,600,300,50,1500,1000,20,2000,1000],512,500,6000),
        ["Fire2"]=new([6000,5000,2000,600,300,50,1500,1000,20,2000,1000],512,500,6000),
        ["HollowPoint"]=new([2000,1000,1000,10,10,10,10,10,10,10,1000],0,1000,1000),
        ["HollowPoint2"]=new([1000,1000,1000,0,0,0,0,0,0,1000,1000],0,1000,1000),
        ["HollowPoint3"]=new([1000,1000,1000,20,20,20,20,20,20,1000,1000],0,1000,1000),
        ["Super"]=new([1000,1000,1000,1000,1000,1000,1000,1000,1000,1000,1000],0,1000,1000),
        ["Mechanical"]=new([0,0,0,1000,1000,1000,1000,1000,1000,1000,1000],0,1000,1000),
        ["Organic"]=new([1000,1000,1000,0,0,0,0,0,0,0,1000],0,1000,1000),
        ["Controller"]=new([1000,1000,1000,1000,1000,1000,0,0,0,1000,1000],0,1000,1000),
        ["Parasite"]=new([1000,1000,1000,1000,1000,1000,0,0,0,0,0],0,1000,1000),
        ["ParasiteDog"]=new([1000,1000,1000,0,0,0,0,0,0,0,0],0,1000,1000),
        ["ParasitePlus"]=new([1000,1000,1000,1000,1000,1000,1000,1000,1000,1000,1000],0,1000,1000),
        ["V3HE"]=new([1000,1000,1000,300,50,50,850,500,300,1000,0],921,1000,1000),
        ["PsiPulse"]=new([1000,1000,1000,0,0,0,0,0,0,0,0],3072,850,1000),
        ["IvanWH"]=new([1000,1000,1000,1000,1000,1000,1000,2500,200,1000,1000],1536,250,1000),
        ["DeathWH"]=new([1000,1000,1000,1000,1000,1000,1000,1000,1000,1000,1000],1536,500,1000),
        ["OilExplosionWH"]=new([1500,1000,1000,900,700,500,2000,900,500,1000,1000],4096,500,1000),
        ["TerrorBombWH"]=new([1500,1000,1000,900,500,500,1000,1500,300,1000,1000],2048,500,1000),
        ["NUKE"]=new([1000,1000,1000,2000,1000,1000,600,1000,80,1000,1000],10240,20,1000),
        ["CRNUKEWH"]=new([1000,1000,1000,900,500,500,600,1500,200,1000,1000],5120,250,1000),
        ["CRTerrorBombWH"]=new([1500,1000,1000,900,500,500,2000,900,500,1000,1000],3072,100,1000),
        ["DemobombWH"]=new([1000,1000,1000,1000,500,500,800,1500,100,1000,1000],8192,100,1000),
        ["ChronoBeam"]=new([1000,1000,1000,1000,1000,1000,1000,1000,1000,1000,1000],0,1000,1000),
        ["RadBeamWarhead"]=new([1000,1000,1000,200,150,100,0,0,0,1000,1000],0,1000,1000),
        ["PrismWarhead"]=new([2000,1000,1000,1000,1000,1000,500,500,500,2000,1000],0,1000,1000),
        ["DummyWarhead"]=new([0,0,0,0,0,0,0,0,0,0,0],0,1000,1000),
        ["RadEruptionWarhead"]=new([1000,1000,1000,200,100,100,0,0,0,1000,1000],10240,1000,1000),
        ["RadSite"]=new([1000,1000,1000,500,100,100,0,0,0,1000,1000],0,1000,1000),
        ["Electric"]=new([1000,1000,1000,850,1000,1000,500,500,500,2000,1000],0,1000,1000),
        ["Shock"]=new([1000,1000,1000,850,1000,1000,500,500,500,2000,1000],0,1000,1000),
        ["ElectricAssault"]=new([0,0,0,0,0,0,1000,1000,1000,500,1000],0,1000,1000),
        ["Slimer"]=new([1000,1000,1000,1000,1000,1000,1000,1000,1000,1000,1000],0,1000,1000),
        ["Shard"]=new([1000,1000,1000,1000,1000,1000,1000,1000,1000,1000,1000],0,1000,1000),
        ["FirestormWH"]=new([1000,1000,1000,1000,1000,1000,1000,1000,1000,1000,1000],0,1000,1000),
        ["IonCannonWH"]=new([1000,1000,1000,1000,1000,1000,1000,1000,1000,1000,1000],2048,500,1000),
        ["VeinholeWH"]=new([1000,1000,1000,1000,1000,1000,1000,1000,1000,1000,1000],0,1000,1000),
        ["PlasmaWH"]=new([1000,1000,1000,1000,1000,1000,1000,1000,1000,1000,1000],0,1000,3500),
        ["SAMWH"]=new([1000,1000,1000,1000,1000,1000,0,0,0,1000,1000],307,1000,1000),
        ["ORCAAP"]=new([1000,1000,1000,1000,1000,1000,1000,1000,750,1000,1000],409,1000,500),
        ["ORCAHE"]=new([1000,1000,1000,1000,1000,1000,1000,1000,1000,1000,1000],0,1000,1500),
        ["Snapshot"]=new([1000,1000,1000,0,0,0,0,0,0,1000,1000],0,1000,1000),
        ["TankSnapshot"]=new([0,0,0,10,10,10,0,0,0,0,0],0,1000,1000),
        ["GrandCannonWH"]=new([1000,1000,1000,1000,1000,1000,500,1000,500,1000,1000],2048,200,300),
        ["FlakWH"]=new([1500,800,500,1000,200,200,0,0,0,1000,1000],1024,100,1000),
        ["FlakTWH"]=new([1500,1000,500,600,100,100,300,200,100,1000,1000],1024,200,1000),
        ["FlakGuyWH"]=new([1500,1000,500,800,200,200,0,0,0,1000,1000],1024,200,1000),
        ["CometWH"]=new([1000,1000,1000,750,500,500,2000,2000,2000,1000,1000],0,1000,1000),
        ["MirageWH"]=new([1000,1000,800,1000,1000,1000,300,200,200,1000,1000],0,1000,1000),
    };
    static readonly Dictionary<string,ClassicUnitTraits> Traits=new(StringComparer.OrdinalIgnoreCase)
    {
        ["ADOG"]=new(NavalTargeting:6,DetectDisguise:true,MindImmune:true),
        ["AEGIS"]=new(NavalTargeting:6,LandTargeting:1),
        ["ASW"]=new(NavalTargeting:2,LandTargeting:1,MindImmune:true),
        ["BEAG"]=new(MindImmune:true),
        ["CARGOPLANE"]=new(MindImmune:true),
        ["CDEST"]=new(NavalTargeting:1,Sensors:8),
        ["CMIN"]=new(MindImmune:true,RadImmune:true),
        ["CMON"]=new(RadImmune:true),
        ["DESO"]=new(RadImmune:true),
        ["DEST"]=new(NavalTargeting:1,Sensors:8),
        ["DLPH"]=new(NavalTargeting:5,LandTargeting:1,Sensors:4,Organic:true),
        ["DMISL"]=new(MindImmune:true),
        ["DOG"]=new(NavalTargeting:6,DetectDisguise:true,MindImmune:true),
        ["DRON"]=new(NavalTargeting:6,MindImmune:true,RadImmune:true,Parasiteable:false),
        ["GHOST"]=new(NavalTargeting:4),
        ["HARV"]=new(MindImmune:true,RadImmune:true),
        ["HORNET"]=new(MindImmune:true),
        ["HORV"]=new(RadImmune:true),
        ["NAFLAK"]=new(NavalTargeting:6,LandTargeting:1),
        ["NAPSIS"]=new(Sensors:15,DetectRange:15,DetectDisguise:true),
        ["NAPSYB"]=new(DetectDisguise:true),
        ["NASAM"]=new(NavalTargeting:6,LandTargeting:1),
        ["ORCA"]=new(MindImmune:true),
        ["PDPLANE"]=new(MindImmune:true),
        ["PTROOP"]=new(DetectDisguise:true,MindImmune:true),
        ["SQD"]=new(NavalTargeting:3,LandTargeting:1,Sensors:4,MindImmune:true,Organic:true,Parasiteable:false),
        ["SUB"]=new(NavalTargeting:5,LandTargeting:1,Sensors:4),
        ["TANY"]=new(NavalTargeting:4),
        ["V3ROCKET"]=new(MindImmune:true),
        ["YURI"]=new(DetectDisguise:true,MindImmune:true),
        ["YURIPR"]=new(MindImmune:true),
        ["ZEP"]=new(Parasiteable:false),
    };
    internal static WeaponDef Weapon(string id)=>ExtraWeapons[id];
    static readonly Dictionary<string,WeaponDef> ExtraWeapons=new(StringComparer.OrdinalIgnoreCase)
    {
        ["HoverMissile"]=new("HoverMissile",25,6144,17,2,true,true,false,false,"AAHeatSeeker2","HE"),
        ["RepairBullet"]=new("RepairBullet",-50,1843,27,1,false,true,false,false,"Invisible","Mechanical"),
        ["CRM60"]=new("CRM60",20,6144,5,1,false,true,false,false,"InvisibleLow","SSA"),
        ["CRFlakGuyGun"]=new("CRFlakGuyGun",30,7168,5,1,false,true,false,false,"FlakTProj","FlakTWH"),
        ["CRMP5"]=new("CRMP5",125,6144,7,1,false,true,false,false,"InvisibleLow","HollowPoint3"),
        ["AWPE"]=new("AWPE",125,14336,20,1,false,true,false,false,"InvisibleLow","HollowPoint"),
        ["CRElectricBolt"]=new("CRElectricBolt",60,6144,15,1,false,true,false,false,"InvisibleLow","Shock"),
        ["CRNuke"]=new("CRNuke",200,1024,27,1,false,true,false,false,"InvisibleLow","CRNUKEWH"),
        ["CRMindControl"]=new("CRMindControl",200,1024,4,1,false,true,false,false,"Psychic","PsiPulse"),
        ["CRRadBeamWeapon"]=new("CRRadBeamWeapon",175,7168,10,1,false,true,false,false,"InvisibleLow","RadBeamWarhead"),
        ["CRNeutronRifle"]=new("CRNeutronRifle",10,6144,40,1,false,true,false,false,"InvisibleLow","ChronoBeam"),
        ["CRTerrorBomb"]=new("CRTerrorBomb",250,1536,4,1,false,true,false,false,"Invisible","TerrorBombWH"),
        ["CowShot"]=new("CowShot",300,12288,25,1,false,true,false,false,"InvisibleHigh","PrismWarhead"),
        ["CometFragment"]=new("CometFragment",30,3072,40,1,false,true,false,false,"SmallCometP","CometWH"),
        ["PsiWave"]=new("PsiWave",250,1024,17,1,false,true,false,false,"Psychic","PsiPulse"),
        ["RadEruptionWeapon"]=new("RadEruptionWeapon",1,4096,20,1,false,true,false,false,"InvisibleLow","RadEruptionWarhead"),
        ["Sapper"]=new("Sapper",2500,1536,34,1,false,false,false,false,"Invisible5","Super"),
        ["FakeC4"]=new("FakeC4",5000,1536,4,1,false,true,false,false,"InvisibleLow","FakeC4WH"),
        ["TerrorBomb"]=new("TerrorBomb",225,1536,4,1,false,true,false,false,"Invisible","TerrorBombWH"),
        ["Demobomb"]=new("Demobomb",300,1024,27,1,false,true,false,false,"InvisibleLow","DemobombWH"),
        ["IvanBomber"]=new("IvanBomber",400,1536,17,1,false,true,false,false,"Invisible","IvanBomb"),
        ["Para"]=new("Para",15,5120,5,1,false,true,false,false,"InvisibleLow","SSA"),
        ["M60"]=new("M60",15,4096,7,1,false,true,false,false,"InvisibleLow","SA"),
        ["HornetBomb"]=new("HornetBomb",40,5120,1,1,false,true,false,false,"NormalBomb","ORCAAP"),
        ["ASWBomb"]=new("ASWBomb",50,3072,1,1,false,true,false,false,"DepthCharge","APSplash"),
        ["NeutronRifle"]=new("NeutronRifle",8,5120,40,1,false,true,false,false,"InvisibleLow","ChronoBeam"),
    };

    internal static WeaponDef? IfvWeapon(string infantry)=>IfvModes.TryGetValue(infantry,out var mode)?Weapon(IfvWeapons[Math.Clamp(mode,0,12)]):null;
    static readonly string[] IfvWeapons=["HoverMissile","RepairBullet","CRM60","CRFlakGuyGun","CRMP5","AWPE","CRElectricBolt","CRNuke","CRMindControl","CRRadBeamWeapon","CRNeutronRifle","CRTerrorBomb","CowShot"];
    static readonly Dictionary<string,int> IfvModes=new(StringComparer.OrdinalIgnoreCase)
    {
        ["E1"]=2,
        ["SNIPE"]=5,
        ["E2"]=2,
        ["FLAKT"]=3,
        ["SHK"]=6,
        ["ENGINEER"]=1,
        ["SENGINEER"]=1,
        ["GHOST"]=4,
        ["TANY"]=4,
        ["YURI"]=8,
        ["IVAN"]=7,
        ["DESO"]=9,
        ["CLEG"]=10,
        ["DOG"]=0,
        ["ADOG"]=0,
        ["VLADIMIR"]=0,
        ["PENTGEN"]=0,
        ["SSRV"]=0,
        ["PRES"]=12,
        ["COW"]=12,
        ["ALL"]=12,
        ["POLARB"]=12,
        ["JOSH"]=12,
        ["SPY"]=2,
        ["TERROR"]=11,
        ["CCOMAND"]=4,
        ["PTROOP"]=4,
        ["CIVAN"]=7,
        ["YURIPR"]=8,
    };
}
