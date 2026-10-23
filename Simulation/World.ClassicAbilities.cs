namespace RedAlert.Simulation;

// All durations are simulation ticks, converted from vanilla's fastest 60
// logic frames/second to this deterministic 20-tick simulation. No wall clock.
public sealed partial class World
{
    readonly Queue<Entity> classicDeathQueue=[];
    bool handlingClassicDeaths;
    public bool ClassicUnitCanMove(Unit u)=>u.Alive && !u.Deployed && ClassicUnitCanAct(u) && u.ParasiteHostId==0
        && !(u.ParasiteId!=0 && Find(u.ParasiteId) is Unit {Type:UnitType.GiantSquid});
    bool ClassicUnitCanAct(Unit u)=>u.DisabledUntil<=Tick && (u.ErasedById==0 || Find(u.ErasedById) is null);
    public bool ClassicBuildingOperational(Building b)=>b.Alive && b.Complete && (b.ErasedById==0 || Find(b.ErasedById) is null);
    static int ClassicInvulnerableUntil(Entity e)=>e switch{Unit u=>u.InvulnerableUntil,Building b=>b.InvulnerableUntil,_=>0};
    public bool DeployClassicUnit(Unit u)
    {
        if(!u.Alive || !ClassicUnitCanAct(u) || u.TransportId!=0)return false;
        switch(u.Type)
        {
            case UnitType.Gi:case UnitType.Desolator:
                u.Deployed=!u.Deployed;u.Path.Clear();u.PathIndex=0;u.Order=UnitOrder.Idle;u.TargetId=0;
                if(u.Type==UnitType.Desolator && u.Deployed){CreateDesolatorRadiation(u);EmitClassicWeapon(u,"RadEruptionWeapon");}return true;
            case UnitType.Yuri:case UnitType.YuriPrime:
                if(u.Reload>0)return false;
                var weapon=ClassicCombatRules.Weapon("PsiWave");u.Reload=weapon.Reload;u.LastFiredTick=Tick;EmitClassicWeapon(u,weapon.Id);
                ApplyClassicAreaDamage(u,u.X,u.Y,weapon.Damage,weapon.Warhead,3*Rules.Scale,850);return true;
            default:return false;
        }
    }
    public bool OrderSpecial(GameCommand c,PlayerState player)
    {
        var selected=new List<Unit>();
        foreach(var id in c.Actors.Distinct().Order())
        {
            var actor=Find(id);if(actor is null)continue;
            if(actor is not Unit u || u.Owner!=player.Id || !ClassicUnitCanAct(u) || u.TransportId!=0)return false;
            selected.Add(u);
        }
        if(selected.Count==0)return false;
        var target=Find(c.Target);var acted=false;
        foreach(var u in selected)
        {
            if(target is null)
            {
                if(u.Type is UnitType.Gi or UnitType.Desolator or UnitType.Yuri or UnitType.YuriPrime)acted|=DeployClassicUnit(u);
                else if(u.Type is UnitType.Terrorist or UnitType.DemolitionTruck){u.Health=0;ApplyClassicDestroyed(u);acted=true;}
                continue;
            }
            if(u.Type==UnitType.Spy && target is Unit {Definition.Infantry:true} disguise && !Allied(u.Owner,disguise.Owner))
            {u.DisguiseOwner=disguise.Owner;u.SpecialOrder=false;u.TargetId=0;acted=true;continue;}
            if(ClassicCanRepair(u,target))
            {u.SpecialOrder=true;u.TargetId=target.Id;SetGoal(u,target.CellX,target.CellY,UnitOrder.Attack);acted=true;continue;}
            if(u.Type==UnitType.TeslaTrooper && target is Building {Type:BuildingType.TeslaCoil} coil && Allied(u.Owner,coil.Owner))
            {u.SpecialOrder=true;u.TargetId=coil.Id;SetGoal(u,coil.CellX,coil.CellY,UnitOrder.Attack);acted=true;continue;}
            if(Allied(u.Owner,target.Owner) || !Visible(player.Id,target.CellX,target.CellY) || ClassicAirTarget(target) || ClassicSubmerged(target) && !ClassicDetected(u.Owner,target))continue;
            if(u.Type is UnitType.Yuri or UnitType.YuriPrime or UnitType.PsiCommando)ReleaseMindControl(u);
            var special=u.Type switch
            {
                UnitType.Spy=>target is Building building && building.Type!=BuildingType.Wall,
                UnitType.Tanya or UnitType.ChronoCommando or UnitType.PsiCommando=>target is Building || ClassicWaterTarget(target),
                UnitType.CrazyIvan or UnitType.ChronoIvan=>target is Unit or Building,
                _=>false
            };
            if(!special && !CanTarget(u,target))continue;
            u.SpecialOrder=special;u.TargetId=target.Id;u.Deployed=false;SetGoal(u,target.CellX,target.CellY,UnitOrder.Attack);acted=true;
        }
        return acted;
    }
    bool UpdateClassicSpecialOrder(Unit u)
    {
        var target=Find(u.TargetId);
        if(target is Unit repaired && ClassicCanRepair(u,repaired))
        {
            if(!InAttackRange(u,repaired))SetGoal(u,repaired.CellX,repaired.CellY,UnitOrder.Attack);
            else if(u.Reload==0){var tool=ClassicIfvWeapon(u)!;repaired.Health=Math.Min(repaired.MaxHealth,repaired.Health-tool.Damage);CureClassicParasite(repaired);u.Reload=tool.Reload;EmitClassicWeapon(u,tool.Id);}
            return true;
        }
        if(u.Type==UnitType.TeslaTrooper && target is Building {Type:BuildingType.TeslaCoil} coil && Allied(u.Owner,coil.Owner))
        {
            if(!InAttackRange(u,coil))SetGoal(u,coil.CellX,coil.CellY,UnitOrder.Attack);
            else{u.Path.Clear();u.PathIndex=0;}
            return true;
        }
        if(target is null || Allied(u.Owner,target.Owner)){u.SpecialOrder=false;u.TargetId=0;u.Order=UnitOrder.Idle;return false;}
        if(!InAttackRange(u,target)){SetGoal(u,target.CellX,target.CellY,UnitOrder.Attack);return true;}
        if(u.Reload>0)return true;
        if(u.Type==UnitType.Spy && target is Building infiltrated)
        {
            InfiltrateClassicBuilding(u,infiltrated);u.Health=0;u.SpecialOrder=false;return true;
        }
        if(u.Type is UnitType.Tanya or UnitType.ChronoCommando or UnitType.PsiCommando)
        {
            if(ClassicInvulnerableUntil(target)<=Tick)PlantClassicBomb(u,target,true);
        }
        else if(u.Type is UnitType.CrazyIvan or UnitType.ChronoIvan)PlantClassicBomb(u,target,false);
        u.Reload=17;u.SpecialOrder=false;u.Order=UnitOrder.Idle;u.TargetId=0;return true;
    }
    bool TryFireClassicAbility(Unit source,Entity target,WeaponDef weapon)
    {
        var id=weapon.Id.ToUpperInvariant();
        if(weapon.Damage<0 && target is Unit repaired && ClassicCanRepair(source,repaired))
        {repaired.Health=Math.Min(repaired.MaxHealth,repaired.Health-weapon.Damage);CureClassicParasite(repaired);return true;}
        if(id is "CRNUKE" or "CRTERRORBOMB")
        {
            var wh=ClassicCombatRules.Warhead(weapon.Warhead);source.Health=0;ApplyClassicDestroyed(source);
            ApplyClassicAreaDamage(source,source.X,source.Y,weapon.Damage,weapon.Warhead,wh.SpreadFixed,wh.EdgePermille,source.Id);return true;
        }
        if(id=="COMET")
        {
            ApplyClassicWarhead(source,target,weapon.Damage,weapon.Warhead);
            foreach(var fragment in Units.Cast<Entity>().Concat(Buildings).Where(e=>e.Alive && e.Id!=target.Id && !Allied(source.Owner,e.Owner) && !ClassicAirTarget(e)
                && DistanceToTargetSquared(target,e)<=(long)3*3*Rules.Scale*Rules.Scale).OrderBy(e=>DistanceToTargetSquared(target,e)).ThenBy(e=>e.Id).Take(5).ToArray())
                Damage(source,fragment,ClassicDamageAgainst(fragment,30,"CometWH"),true);
            return true;
        }
        if(id=="SONICZAP")
        {
            ApplyClassicWarhead(source,target,weapon.Damage,weapon.Warhead);
            var dx=(long)target.X-source.X;var dy=(long)target.Y-source.Y;var length= Math.Max(1,IntegerSqrt(dx*dx+dy*dy));
            foreach(var entity in Units.Cast<Entity>().Concat(Buildings).ToArray())
            {
                if(!entity.Alive || entity.Id==source.Id || !ClassicWaterTarget(entity))continue;
                var ex=(long)entity.X-source.X;var ey=(long)entity.Y-source.Y;var along=ex*dx+ey*dy;
                if(along>=0 && along<=dx*dx+dy*dy && Math.Abs(ex*dy-ey*dx)/length<=Rules.Scale/4)
                    Damage(source,entity,ClassicDamageAgainst(entity,10,weapon.Warhead),true);
            }
            return true;
        }
        switch(weapon.Warhead.ToUpperInvariant())
        {
            case "CONTROLLER":
                if(target is Unit victim && !ClassicTraits(victim).MindImmune && ClassicInvulnerableUntil(victim)<=Tick)
                {
                    ReleaseMindControl(source);source.ControlledTargetId=victim.Id;victim.ControllerId=source.Id;victim.OriginalOwner=victim.Owner;victim.Owner=source.Owner;
                    StopClassicUnit(victim);source.TargetId=0;source.Order=UnitOrder.Idle;
                    Events.Add(new CombatEvent(Tick,source.Id,victim.Id,victim.X,victim.Y,0,false,source.X,source.Y,source.Type,SourceOwner:source.Owner));
                }
                return true;
            case "PARASITEDOG":
                if(target is Unit {Definition.Infantry:true})Damage(source,target,target.Health);return true;
            case "PARASITE":case "PARASITEPLUS":
                if(target is not Unit host || ClassicInvulnerableUntil(host)>Tick)return true;
                if(host.Definition.Infantry){Damage(source,host,host.Health);return true;}
                if(host.ParasiteId!=0)return true;
                source.ParasiteHostId=host.Id;host.ParasiteId=source.Id;source.NextParasiteTick=Tick+Math.Max(1,weapon.Reload);
                source.X=host.X;source.Y=host.Y;source.Path.Clear();source.PathIndex=0;return true;
            case "IVANBOMB":PlantClassicBomb(source,target,false);return true;
            case "FAKEC4WH":PlantClassicBomb(source,target,true);return true;
        }
        if(id=="SAPPER"){PlantClassicBomb(source,target,true);return true;}
        if(source.Type is UnitType.Terrorist or UnitType.DemolitionTruck)
        {source.Health=0;ApplyClassicDestroyed(source);return true;}
        return false;
    }
    void PlantClassicBomb(Unit source,Entity target,bool c4)
    {
        if(ClassicInvulnerableUntil(target)>Tick)return;
        var due=Tick+(c4?9:150);
        if(source.LastFiredTick!=Tick)EmitClassicWeapon(source,c4?"Sapper":"IvanBomber");
        if(target is Unit u){if(u.BombTick>=0)return;u.BombTick=due;u.BombOwner=source.Owner;u.BombSourceId=source.Id;u.BombIsC4=c4;}
        else if(target is Building b){if(b.BombTick>=0)return;b.BombTick=due;b.BombOwner=source.Owner;b.BombSourceId=source.Id;b.BombIsC4=c4;}
    }
    void DetonateClassicBomb(Entity target)
    {
        var owner=target is Unit u?u.BombOwner:((Building)target).BombOwner;
        var sourceId=target is Unit su?su.BombSourceId:((Building)target).BombSourceId;
        var c4=target is Unit cu?cu.BombIsC4:((Building)target).BombIsC4;
        if(target is Unit unit)unit.BombTick=-1;else ((Building)target).BombTick=-1;
        if(owner<0 || owner>=Players.Length)return;
        var source=Find(sourceId)??new ClassicDamageOrigin{Id=sourceId,Owner=owner,X=target.X,Y=target.Y};
        if(c4){if(target.Alive)Damage(source,target,Math.Max(1,target.Health));}
        else
        {
            var wh=ClassicCombatRules.Warhead("IvanWH");
            ApplyClassicAreaDamage(source,target.X,target.Y,400,"IvanWH",wh.SpreadFixed,wh.EdgePermille);
        }
    }
    void InfiltrateClassicBuilding(Unit spy,Building building)
    {
        var thief=Players[spy.Owner];var victim=Players[building.Owner];
        switch(building.Type)
        {
            case BuildingType.Refinery:
                var amount=victim.Credits/2;victim.Credits-=amount;thief.Credits+=amount;break;
            case BuildingType.PowerPlant:case BuildingType.NuclearReactor:victim.PowerBlackoutUntil=Math.Max(victim.PowerBlackoutUntil,Tick+334);break;
            case BuildingType.Barracks:thief.VeteranInfantry=true;break;
            case BuildingType.WarFactory:thief.VeteranVehicles=true;break;
            case BuildingType.BattleLab:
                if(Rules.Side(building.FoundationCountry)==Faction.Allied)thief.StolenAlliedTech=true;else thief.StolenSovietTech=true;break;
            case BuildingType.Radar:case BuildingType.SpySatellite:
                victim.CrateMapRevealed=false;Array.Clear(victim.Visibility);break;
            case BuildingType.IronCurtain:victim.IronCurtainReadyTick=Tick+1500;break;
            case BuildingType.Chronosphere:victim.ChronosphereReadyTick=Tick+2100;victim.ChronosphereSelectingDestination=false;break;
            case BuildingType.WeatherControl:victim.WeatherReadyTick=Tick+3000;break;
            case BuildingType.NuclearMissile:victim.NukeReadyTick=Tick+3000;break;
        }
        Events.Add(new CombatEvent(Tick,spy.Id,building.Id,building.X,building.Y,0,false,spy.X,spy.Y,spy.Type,building.Type,SourceOwner:spy.Owner));
    }
    void UpdateErasure(Unit source,Entity target,WeaponDef weapon)
    {
        if(ClassicInvulnerableUntil(target)>Tick){ReleaseErasure(source);return;}
        var erasedBy=target is Unit u?u.ErasedById:target is Building b?b.ErasedById:0;
        if(erasedBy!=0 && erasedBy!=source.Id && Find(erasedBy) is not null)return;
        if(source.ErasingTargetId!=target.Id){ReleaseErasure(source);source.ErasingTargetId=target.Id;source.LastFiredTick=Tick;source.LastTarget=target.Id;}
        var complete=false;
        if(target is Unit unit){unit.ErasedById=source.Id;unit.EraseProgress+=Math.Max(1,weapon.Damage)*3;complete=unit.EraseProgress>=unit.MaxHealth;}
        else if(target is Building building){building.ErasedById=source.Id;building.EraseProgress+=Math.Max(1,weapon.Damage)*3;complete=building.EraseProgress>=building.MaxHealth;}
        if(!complete)return;
        Damage(source,target,target.Health);ReleaseErasure(source);source.Reload=weapon.Reload;source.TargetId=0;
    }
    void ReleaseErasure(Unit source)
    {
        if(source.ErasingTargetId==0)return;
        var target=Find(source.ErasingTargetId);
        if(target is Unit u && u.ErasedById==source.Id){u.ErasedById=0;u.EraseProgress=0;}
        else if(target is Building b && b.ErasedById==source.Id){b.ErasedById=0;b.EraseProgress=0;}
        source.ErasingTargetId=0;
    }
    public void ReleaseMindControl(Unit controller)
    {
        foreach(var victim in Units.Where(u=>u.ControllerId==controller.Id).ToArray())
        {
            if(victim.OriginalOwner>=0 && victim.OriginalOwner<Players.Length && !Players[victim.OriginalOwner].Defeated)victim.Owner=victim.OriginalOwner;
            else victim.Health=0;
            victim.ControllerId=0;victim.OriginalOwner=-1;StopClassicUnit(victim);
        }
        controller.ControlledTargetId=0;
    }
    static void StopClassicUnit(Unit u){u.TargetId=0;u.Order=UnitOrder.Idle;u.Path.Clear();u.PathIndex=0;u.GoalX=u.CellX;u.GoalY=u.CellY;u.SpecialOrder=false;}
    public void CureClassicParasite(Unit host)
    {
        if(Find(host.ParasiteId) is Unit parasite){parasite.Health=0;ApplyClassicDestroyed(parasite);}
        host.ParasiteId=0;
    }
    public void UpdateClassicUnitState(Unit u)
    {
        if(u.ErasingTargetId!=0 && (u.TargetId!=u.ErasingTargetId || u.TransportId!=0 || u.ParasiteHostId!=0 || !ClassicUnitCanAct(u)))ReleaseErasure(u);
        if(u.ControllerId!=0 && Find(u.ControllerId) is null)
        {
            if(u.OriginalOwner>=0 && u.OriginalOwner<Players.Length && !Players[u.OriginalOwner].Defeated)u.Owner=u.OriginalOwner;else u.Health=0;
            u.ControllerId=0;u.OriginalOwner=-1;StopClassicUnit(u);
        }
        if(u.ControlledTargetId!=0 && Find(u.ControlledTargetId) is null)u.ControlledTargetId=0;
        if(u.ErasedById!=0 && Find(u.ErasedById) is null){u.ErasedById=0;u.EraseProgress=0;}
        if(u.BombTick>=0 && Tick>=u.BombTick)DetonateClassicBomb(u);
        if(u.ParasiteHostId!=0)
        {
            if(Find(u.ParasiteHostId) is not Unit host){u.ParasiteHostId=0;StopClassicUnit(u);return;}
            u.X=host.X;u.Y=host.Y;
            if(Tick>=u.NextParasiteTick)
            {
                var weapon=u.Definition.PrimaryWeapon;u.NextParasiteTick=Tick+Math.Max(1,weapon?.Reload??20);
                if(weapon is not null)Damage(u,host,ClassicDamageAgainst(host,weapon.Damage,weapon.Warhead),true);
                if(u.Type==UnitType.GiantSquid && host.Alive && host.Health*4<host.MaxHealth)Damage(u,host,host.Health,true);
            }
        }
        if(u.Type==UnitType.Desolator && u.Deployed && u.TransportId==0 && u.ParasiteHostId==0 && ClassicUnitCanAct(u) && Tick%20==u.Id%20)CreateDesolatorRadiation(u);
    }
    void CreateDesolatorRadiation(Unit u)
    {
        u.RadiationUntil=Tick+167;
        ClassicAreaEffects.RemoveAll(e=>e.SourceId==u.Id && e.Kind=="Radiation");
        ClassicAreaEffects.Add(new(u.Id,u.Owner,u.X,u.Y,10*Rules.Scale,Tick,u.RadiationUntil,500,"Radiation"){NextTick=Tick});
    }
    public void UpdateClassicWorldState()
    {
        EnsureClassicWeaponEventTick();UpdateClassicProjectiles();UpdateClassicSuperWeaponCharge();
        foreach(var b in Buildings.ToArray())
        {
            if(b.ErasedById!=0 && Find(b.ErasedById) is null){b.ErasedById=0;b.EraseProgress=0;}
            if(b.Alive && b.BombTick>=0 && Tick>=b.BombTick)DetonateClassicBomb(b);
        }
        foreach(var effect in ClassicAreaEffects.ToArray())
        {
            if(Tick<effect.NextTick || Tick>effect.ExpireTick)continue;
            var source=Find(effect.SourceId)??new ClassicDamageOrigin{Id=effect.SourceId,Owner=effect.Owner,X=effect.X,Y=effect.Y};
            if(effect.Kind=="Radiation")
            {
                var remaining=effect.ExpireTick-Tick;var level=Math.Min(500,effect.Strength*remaining/Math.Max(1,effect.ExpireTick-effect.CreatedTick));
                var origin=new ClassicDamageOrigin{X=effect.X,Y=effect.Y};
                foreach(var u in Units.ToArray())
                {
                    if(!u.Alive || ClassicTraits(u).RadImmune || ClassicAirTarget(u) || u.TransportId!=0 || u.ParasiteHostId!=0)continue;
                    var distance=IntegerSqrt(DistanceSquared(origin,u));if(distance>effect.RadiusFixed)continue;
                    var localLevel=(int)((long)level*(effect.RadiusFixed-distance)/Math.Max(1,effect.RadiusFixed));
                    Damage(source,u,ClassicDamageAgainst(u,localLevel/5,"RadSite"),true);
                }
                effect.NextTick=Tick+6;
            }
            else if(effect.Kind=="Nuke")
            {
                ApplyClassicAreaDamage(source,effect.X,effect.Y,600,"NUKE",10*Rules.Scale,20);
                ClassicAreaEffects.Add(new(effect.SourceId,effect.Owner,effect.X,effect.Y,10*Rules.Scale,Tick,Tick+167,500,"Radiation"){NextTick=Tick+6});
                effect.NextTick=int.MaxValue;
            }
            else if(effect.Kind=="Weather")
            {
                ApplyClassicAreaDamage(source,effect.X,effect.Y,250,"IonWH",2*Rules.Scale,500);
                ApplyClassicAreaDamage(source,effect.X,effect.Y,250,"IonWH",2*Rules.Scale,500,air:true);
                var noise=BattleMap.Noise(effect.X/Rules.Scale,effect.Y/Rules.Scale,(uint)(Tick+effect.SourceId));
                var x=effect.X+((int)(noise%11)-5)*Rules.Scale;var y=effect.Y+((int)(noise/11%11)-5)*Rules.Scale;
                ApplyClassicAreaDamage(source,x,y,250,"IonWH",2*Rules.Scale,500);effect.NextTick=Tick+4;
            }
        }
        ClassicAreaEffects.RemoveAll(e=>Tick>e.ExpireTick || e.NextTick==int.MaxValue);
    }
    void UpdateClassicSuperWeaponCharge()
    {
        foreach(var player in Players)
        {
            bool Has(BuildingType type)=>Settings.SuperWeapons && Buildings.Any(b=>b.Owner==player.Id && b.Alive && b.Complete && b.Type==type);
            int Charge(int ready,BuildingType type,int duration)
            {
                if(!Has(type))return -1;
                if(ready<0)return Tick+duration;
                return player.PowerBlackoutUntil>Tick || player.GeneratedPower<player.UsedPower || !Buildings.Any(b=>b.Owner==player.Id && b.Type==type && ClassicBuildingOperational(b))?ready+1:ready;
            }
            player.IronCurtainReadyTick=Charge(player.IronCurtainReadyTick,BuildingType.IronCurtain,1500);
            player.ChronosphereReadyTick=Charge(player.ChronosphereReadyTick,BuildingType.Chronosphere,2100);
            player.WeatherReadyTick=Charge(player.WeatherReadyTick,BuildingType.WeatherControl,3000);
            player.NukeReadyTick=Charge(player.NukeReadyTick,BuildingType.NuclearMissile,3000);
            if(player.ChronosphereReadyTick<0)player.ChronosphereSelectingDestination=false;
        }
    }
    public bool OrderSuperWeapon(GameCommand c,PlayerState player)
    {
        if(!Settings.SuperWeapons || c.Actors.Length!=1 || Find(c.Actors[0]) is not Building building || building.Owner!=player.Id)return false;
        if(c.Target==-1)
        {
            if(building.Type!=BuildingType.Chronosphere)return false;
            player.ChronosphereSelectingDestination=false;return true;
        }
        if(!Map.IsPlayable(c.X,c.Y) || !ClassicBuildingOperational(building) || player.PowerBlackoutUntil>Tick || player.GeneratedPower<player.UsedPower)return false;
        var x=c.X*Rules.Scale+Rules.Scale/2;var y=c.Y*Rules.Scale+Rules.Scale/2;
        switch(building.Type)
        {
            case BuildingType.IronCurtain:
                if(player.IronCurtainReadyTick<0 || Tick<player.IronCurtainReadyTick)return false;
                foreach(var entity in Units.Cast<Entity>().Concat(Buildings).ToArray())
                {
                    if(!entity.Alive || ClassicAirTarget(entity) || Math.Abs(entity.X-x)>Rules.Scale*3/2 || Math.Abs(entity.Y-y)>Rules.Scale*3/2)continue;
                    if(entity is Unit organic && (organic.Definition.Infantry || ClassicTraits(organic).Organic))Damage(building,organic,organic.Health,true);
                    else if(entity is Unit unit)unit.InvulnerableUntil=Tick+250;
                    else if(entity is Building protectedBuilding)protectedBuilding.InvulnerableUntil=Tick+250;
                }
                player.IronCurtainReadyTick=Tick+1500;return true;
            case BuildingType.WeatherControl:
                if(player.WeatherReadyTick<0 || Tick<player.WeatherReadyTick)return false;
                ClassicAreaEffects.Add(new(building.Id,player.Id,x,y,10*Rules.Scale,Tick+84,Tick+144,250,"Weather"){NextTick=Tick+84});player.WeatherReadyTick=Tick+3000;return true;
            case BuildingType.NuclearMissile:
                if(player.NukeReadyTick<0 || Tick<player.NukeReadyTick)return false;
                ClassicAreaEffects.Add(new(building.Id,player.Id,x,y,10*Rules.Scale,Tick,Tick+30,600,"Nuke"){NextTick=Tick+30});player.NukeReadyTick=Tick+3000;return true;
            case BuildingType.Chronosphere:
                if(player.ChronosphereReadyTick<0 || Tick<player.ChronosphereReadyTick)return false;
                if(!player.ChronosphereSelectingDestination){player.ChronosphereSourceX=c.X;player.ChronosphereSourceY=c.Y;player.ChronosphereSelectingDestination=true;return true;}
                var targets=Units.Where(u=>u.Alive && u.TransportId==0 && u.ParasiteHostId==0 && u.Definition.Domain!=MovementDomain.Air
                    && Math.Abs(u.CellX-player.ChronosphereSourceX)<=1 && Math.Abs(u.CellY-player.ChronosphereSourceY)<=1).OrderBy(u=>u.Id).ToArray();
                if(targets.Length==0){player.ChronosphereSelectingDestination=false;return false;}
                foreach(var unit in targets)
                {
                    var nx=unit.X+(c.X-player.ChronosphereSourceX)*Rules.Scale;var ny=unit.Y+(c.Y-player.ChronosphereSourceY)*Rules.Scale;var cx=nx/Rules.Scale;var cy=ny/Rules.Scale;
                    if(unit.Definition.Infantry || !Map.IsPlayable(cx,cy) || unit.Definition.Domain==MovementDomain.Water && (Map.Ground[Map.Index(cx,cy)]!=Terrain.Water || Map.HasBridge(cx,cy))
                        || unit.Definition.Domain==MovementDomain.Land && !unit.Definition.Amphibious && !Map.Walkable(cx,cy) || !ClassicTeleportSpaceFree(unit,nx,ny))
                    {Damage(building,unit,unit.Health,true);continue;}
                    if(Map.Inside(unit.CellX,unit.CellY))occupied[Map.Index(unit.CellX,unit.CellY)]=Math.Max(0,occupied[Map.Index(unit.CellX,unit.CellY)]-1);
                    unit.X=nx;unit.Y=ny;occupied[Map.Index(cx,cy)]++;StopClassicUnit(unit);unit.DisabledUntil=Tick+20;
                }
                player.ChronosphereSelectingDestination=false;player.ChronosphereReadyTick=Tick+2100;return true;
            default:return false;
        }
    }
    bool ClassicTeleportSpaceFree(Unit unit,int x,int y)
    {
        var cx=x/Rules.Scale;var cy=y/Rules.Scale;
        if(!Map.IsPlayable(cx,cy) || Map.Buildings[Map.Index(cx,cy)]!=0)return false;
        return !Units.Any(u=>u.Alive && u.Id!=unit.Id && u.TransportId==0 && u.ParasiteHostId==0 && u.Definition.Domain==unit.Definition.Domain
            && Math.Abs(u.X-x)<Rules.Scale*2/3 && Math.Abs(u.Y-y)<Rules.Scale*2/3);
    }
    public void ApplyClassicDestroyed(Entity entity)
    {
        if(entity.Alive)return;classicDeathQueue.Enqueue(entity);if(handlingClassicDeaths)return;handlingClassicDeaths=true;
        try
        {
            while(classicDeathQueue.TryDequeue(out var destroyed))
            {
                if(destroyed is ClassicProjectile aircraft && (aircraft.Kind is "HORNET" or "ASW"))
                {
                    if(Find(aircraft.SourceId) is Unit launcher)launcher.Reload=Math.Max(launcher.Reload,aircraft.Kind=="HORNET"?200:134);
                }
                else if(destroyed is Unit unit)
                {
                    if(unit.ClassicDeathHandled)continue;unit.ClassicDeathHandled=true;
                    ReleaseMindControl(unit);ReleaseErasure(unit);
                    if(Find(unit.ControllerId) is Unit controller)controller.ControlledTargetId=0;
                    if(Find(unit.ParasiteHostId) is Unit host)host.ParasiteId=0;
                    if(Find(unit.ParasiteId) is Unit parasite){parasite.ParasiteHostId=0;StopClassicUnit(parasite);}
                    if(unit.BombTick>=0)DetonateClassicBomb(unit);
                    if(unit.Type is UnitType.Terrorist or UnitType.DemolitionTruck)
                    {
                        var weapon=ClassicCombatRules.Weapon(unit.Type==UnitType.Terrorist?"TerrorBomb":"Demobomb");var wh=ClassicCombatRules.Warhead(weapon.Warhead);
                        if(unit.LastFiredTick!=Tick)EmitClassicWeapon(unit,weapon.Id);
                        ApplyClassicAreaDamage(unit,unit.X,unit.Y,weapon.Damage,weapon.Warhead,wh.SpreadFixed,wh.EdgePermille,unit.Id);
                        if(unit.Type==UnitType.DemolitionTruck)ClassicAreaEffects.Add(new(unit.Id,unit.Owner,unit.X,unit.Y,8*Rules.Scale,Tick,Tick+34,100,"Radiation"){NextTick=Tick+6});
                    }
                }
                else if(destroyed is Building building)
                {
                    if(building.ClassicDeathHandled)continue;building.ClassicDeathHandled=true;
                    if(building.BombTick>=0)DetonateClassicBomb(building);
                }
            }
        }
        finally{handlingClassicDeaths=false;}
    }
}

public sealed partial class Unit
{
    public int InvulnerableUntil,ControllerId,ControlledTargetId,ErasedById,EraseProgress,ErasingTargetId,ParasiteHostId,ParasiteId,NextParasiteTick;
    public int OriginalOwner=-1,BombTick=-1,BombOwner=-1,BombSourceId,DisguiseOwner=-1,RevealedUntil,DisabledUntil,RadiationUntil;
    public bool SpecialOrder,BombIsC4,ClassicDeathHandled;
}
public sealed partial class Building
{
    public int InvulnerableUntil,ErasedById,EraseProgress,BombTick=-1,BombOwner=-1,BombSourceId;
    public bool BombIsC4,ClassicDeathHandled;
}
public sealed partial class PlayerState
{
    public int IronCurtainReadyTick=-1,ChronosphereReadyTick=-1,WeatherReadyTick=-1,NukeReadyTick=-1;
    public int ChronosphereSourceX,ChronosphereSourceY,PowerBlackoutUntil,RadarBlackoutUntil;
    public bool ChronosphereSelectingDestination,VeteranInfantry,VeteranVehicles;
}
