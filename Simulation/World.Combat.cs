namespace RedAlert.Simulation;

public sealed partial class World
{
    bool InAttackRange(Unit u,Entity? target)
    {
        if(target is null)return false;
        if(u.Type==UnitType.Engineer || u.SpecialOrder)return DistanceToTargetSquared(u,target)<=(long)(Rules.Scale*3/2)*(Rules.Scale*3/2);
        var weapon=SelectClassicWeapon(u,target);if(weapon is null)return false;
        var range=ClassicWeaponRange(u,weapon);
        return DistanceToTargetSquared(u,target)<=(long)range*range;
    }
    static long DistanceToTargetSquared(Entity source,Entity target)
    {
        if(target is not Building b)return DistanceSquared(source,target);
        var x=Math.Clamp(source.X,b.CellLeft*Rules.Scale,(b.CellLeft+b.Definition.Width)*Rules.Scale);
        var y=Math.Clamp(source.Y,b.CellTop*Rules.Scale,(b.CellTop+b.Definition.Height)*Rules.Scale);
        var dx=(long)source.X-x;var dy=(long)source.Y-y;return dx*dx+dy*dy;
    }
    Entity? AcquireTarget(Entity source,int range)
    {
        Entity? best=null;var bestDistance=(long)range*range*Rules.Scale*Rules.Scale;
        void Consider(Entity target)
        {
            if(!target.Alive || Allied(source.Owner,target.Owner) || Players[target.Owner].Defeated || !Visible(source.Owner,target.CellX,target.CellY) || !CanTarget(source,target))return;
            var weapon=SelectClassicWeapon(source,target);if(weapon is null || ClassicCombatRules.Warhead(weapon.Warhead).Verses[ClassicArmorIndex(target)]<=20)return;
            var distance=DistanceToTargetSquared(source,target);var effective=ClassicWeaponRange(source,weapon);
            if(distance>(long)effective*effective)return;
            if(distance<bestDistance || distance==bestDistance && (best is null || target.Id<best.Id)){best=target;bestDistance=distance;}
        }
        foreach(var u in Units)Consider(u);
        foreach(var b in Buildings)Consider(b);
        foreach(var missile in ClassicProjectiles)Consider(missile);
        return best;
    }
    void UpdateUnitCombat(Unit u)
    {
        if(!u.Alive || !ClassicUnitCanAct(u) || u.TransportId!=0 || u.ParasiteHostId!=0 || u.Definition.AirportBound && (u.Returning || u.Ammo<=0))return;
        if(u.Order==UnitOrder.AttackGround){UpdateTerrainCombat(u);return;}
        if(u.Type==UnitType.Engineer)
        {
            if(u.Order==UnitOrder.Attack && Find(u.TargetId) is Building b && u.Reload==0)
            {
                if(InAttackRange(u,b))
                {
                    if(Allied(u.Owner,b.Owner))b.Health=b.MaxHealth;
                    else
                    {
                        var previousOwner=b.Owner;RefundClassicProducerQueue(b,previousOwner);
                        b.Owner=u.Owner;b.Repairing=false;
                    }
                    u.Health=0;
                }
                else SetGoal(u,b.CellLeft,b.CellTop,UnitOrder.Attack);
            }
            return;
        }
        if(u.Order!=UnitOrder.Attack)u.SpecialOrder=false;
        if(u.SpecialOrder && UpdateClassicSpecialOrder(u))return;
        if(u.Type==UnitType.Spy || u.Type==UnitType.Desolator && u.Deployed)return;
        var target=Find(u.TargetId);
        if(target is not null && (!CanTarget(u,target) || !Visible(u.Owner,target.CellX,target.CellY)))target=null;
        if(target is null || u.Order!=UnitOrder.Attack && !InAttackRange(u,target))
        {
            u.TargetId=0;
            if(u.Order!=UnitOrder.Move && u.Type is not (UnitType.AircraftCarrier or UnitType.Dreadnought or UnitType.V3Launcher) && !u.Definition.AirportBound && (Tick+u.Id)%5==0)target=AcquireTarget(u,ClassicMaximumRange(u));
        }
        if(target is null){ReleaseErasure(u);return;}
        u.TargetId=target.Id;
        if(u.Order==UnitOrder.Attack && !u.Deployed)SetGoal(u,target.CellX,target.CellY,UnitOrder.Attack);
        if(!InAttackRange(u,target)){ReleaseErasure(u);return;}
        if(u.Order==UnitOrder.AttackMove){u.Path.Clear();u.PathIndex=0;}
        u.FacingX=target.X-u.X;u.FacingY=target.Y-u.Y;
        var weapon=SelectClassicWeapon(u,target);if(weapon is null)return;
        if(weapon.Warhead.Equals("ChronoBeam",StringComparison.OrdinalIgnoreCase)){if(u.ErasingTargetId!=0 || u.Reload==0)UpdateErasure(u,target,weapon);return;}
        if(u.Reload==0)Fire(u,target,weapon.Damage,weapon.Reload,u.Definition.Splash);
    }
    public void UpdateClassicBuildingCombat(Building building)
    {
        if(building.Reload>0)building.Reload--;
        if(building.Definition.PrimaryWeapon is null && building.Definition.SecondaryWeapon is null)return;
        if(!ClassicBuildingOperational(building) || Players[building.Owner].Defeated)return;
        if(building.Definition.Power<0 && !Players[building.Owner].Powered && !ClassicTeslaCharged(building))return;
        var target=AcquireTarget(building,ClassicMaximumRange(building));
        if(target is not null && building.Reload==0)Fire(building,target,building.Definition.Damage,building.Definition.Reload,0);
    }
    public bool ClassicTeslaCharged(Building building)=>building.Type==BuildingType.TeslaCoil && Units.Count(u=>u.Alive && u.Type==UnitType.TeslaTrooper && u.Owner==building.Owner
        && u.SpecialOrder && u.TargetId==building.Id && DistanceToTargetSquared(u,building)<=(long)(Rules.Scale*3/2)*(Rules.Scale*3/2))>=2;
    // Keep this signature for building defense integration; weapon selection and
    // original warhead verses apply to units AND defenses at the same boundary.
    void Fire(Entity source,Entity target,int damage,int reload,int splash)
    {
        var weapon=SelectClassicWeapon(source,target);if(weapon is null || !CanTarget(source,target))return;
        var weaponId=weapon.Id.ToUpperInvariant();
        var minimum=weaponId switch{"V3LAUNCHER"=>5*Rules.Scale,"DREDLAUNCHER"=>8*Rules.Scale,_=>0};
        if(DistanceToTargetSquared(source,target)<(long)minimum*minimum)return;
        if(source is Unit spawner && (weaponId is "HORNETLAUNCHER" or "ASWLAUNCHER") && ClassicProjectiles.Count(p=>p.Alive && p.SourceId==spawner.Id)>=(weaponId=="HORNETLAUNCHER"?3:1))return;
        if(source is Unit ammoUnit && ammoUnit.Definition.Ammo>0 && ammoUnit.Ammo<=0)return;
        source.Reload=weapon.Reload;source.LastFiredTick=Tick;source.LastTarget=target.Id;EmitClassicWeapon(source,weapon.Id);
        if(source is Unit attacker)
        {
            if(weaponId is not ("SUBTORPEDO" or "SONICZAP" or "SQUIDGRAB"))attacker.RevealedUntil=Tick+20;
            if(attacker.Definition.Ammo>0)attacker.Ammo--;
            if(TryFireClassicAbility(attacker,target,weapon))return;
            if(TryLaunchClassicOrdnance(attacker,target,weapon))return;
        }
        for(var i=0;i<Math.Max(1,weapon.Burst) && target.Alive;i++)ApplyClassicWarhead(source,target,weapon.Damage,weapon.Warhead);
    }
    void Damage(Entity source,Entity target,int damage,bool secondary=false)
    {
        if(!target.Alive || damage<=0 || ClassicInvulnerableUntil(target)>Tick)return;
        if(target is Unit passenger && passenger.TransportId!=0)return;
        target.Health-=damage;
        if(target.Health<=0 && source is Unit u && !Allied(source.Owner,target.Owner))u.Kills++;
        Events.Add(new CombatEvent(Tick,source.Id,target.Id,target.X,target.Y,damage,target.Health<=0,
            source.X,source.Y,source is Unit attacker?attacker.Type:null,source is Building gun?gun.Type:null,
            target is Unit{Definition.Infantry:true},source.Owner,secondary));
        if(!target.Alive)ApplyClassicDestroyed(target);
    }
}
