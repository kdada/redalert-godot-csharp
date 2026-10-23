namespace RedAlert.Simulation;

// Original rules.ini Wall/Wood warhead flags plus the original C4/Ivan placement
// actions. Tree HP/armor policy lives separately in ClassicTerrainCombatRules.
internal static class ClassicTerrainWeaponRules
{
    internal const int C4DelayTicks=9,IvanDelayTicks=150,IvanDamage=400;
    internal static readonly string[] BridgeWarheads=["SONICWARHEAD","TANKOGAS","HE","V3WH","V3EWH","DMISLWH","DMISLEWH","BLIMPHE","KTSTLEXP","MAVERICKHE","ARTYHE","IONWH","AP","GRIZAPE","RHINAPE","APOCAP","APOCAPE","APSPLASH","ULTRAAP","ULTRAAPE","HOWITZERWH","RPG","FIRE","FIRE2","V3HE","OILEXPLOSIONWH","NUKE","PRISMWARHEAD","ELECTRIC","SHOCK","IONCANNONWH","PLASMAWH","ORCAAP","ORCAHE","GRANDCANNONWH","SUPER","IVANWH","IVANBOMB","FAKEC4WH"];
}

public sealed partial class World
{
    sealed record TerrainBomb(int Target,int X,int Y,int Due,int Source,int Owner,bool C4,int SurfaceLevelFixed);
    readonly List<TerrainBomb> terrainBombs=[];
    sealed class TerrainAim : Entity
    {
        public override int MaxHealth=>1;
        public override int Sight=>0;
    }
    static string TerrainWeaponWarhead(WeaponDef weapon)=>weapon.Id.ToUpperInvariant() switch
    {"V3LAUNCHER"=>"V3WH","DREDLAUNCHER"=>"DMISLWH","HORNETLAUNCHER"=>"ORCAAP","ASWLAUNCHER"=>"APSplash",_=>weapon.Warhead};
    bool TerrainWeaponAllowed(Unit unit,WeaponDef weapon,TerrainTarget? target)
    {
        var id=weapon.Id.ToUpperInvariant();var wh=TerrainWeaponWarhead(weapon).ToUpperInvariant();
        if(id=="VIRTUALSCANNER" || wh is "CONTROLLER" or "PARASITE" or "PARASITEDOG" or "PARASITEPLUS" or "CHRONOBEAM")return false;
        if(weapon.Underwater || ClassicTraits(unit).LandTargeting==1)return false;
        var bomb=id is "SAPPER" or "FAKEC4" || wh is "IVANBOMB" or "FAKEC4WH";
        if(!bomb && (!weapon.AntiGround || weapon.Damage<=0))return false;
        if(bomb && target is null)return false;
        if(target?.Kind==TerrainTargetKind.Bridge && !bomb && !ClassicTerrainWeaponRules.BridgeWarheads.Contains(wh,StringComparer.Ordinal))return false;
        return ClassicCombatRules.Warhead(TerrainWeaponWarhead(weapon)).Verses[ClassicCombatRules.ArmorIndex(target?.Armor??"none")]>0;
    }
    WeaponDef? SelectTerrainWeapon(Unit unit,TerrainTarget? target)
    {
        if(unit.Type is UnitType.Engineer or UnitType.Spy)return null;
        var first=unit.Definition.PrimaryWeapon;var second=unit.Definition.SecondaryWeapon;
        if(unit.Type==UnitType.Gi){first=unit.Deployed?second:first;second=null;}
        if(unit.Type is UnitType.Yuri or UnitType.YuriPrime or UnitType.Desolator)second=null;
        if(ClassicIfvWeapon(unit) is {} crew){first=crew;second=null;}
        WeaponDef? result=null;long best=-1;
        foreach(var weapon in new[]{first,second})
        {
            if(weapon is null || !TerrainWeaponAllowed(unit,weapon,target))continue;
            var value=(long)Math.Max(1,weapon.Damage)*ClassicCombatRules.Warhead(TerrainWeaponWarhead(weapon)).Verses[ClassicCombatRules.ArmorIndex(target?.Armor??"none")];
            if(value>best){best=value;result=weapon;}
        }
        return result;
    }
    bool TerrainVisibleTo(int player,TerrainTarget target)
    {
        for(var y=target.CellY;y<target.CellY+target.Height;y++)for(var x=target.CellX;x<target.CellX+target.Width;x++)
            if(Visible(player,x,y) && (target.Kind!=TerrainTargetKind.Bridge || Map.IsBridgeCellIntact(x,y) && Map.BridgeIds[Map.Index(x,y)]==target.BridgeId))return true;
        return false;
    }
    public bool CanAttackTerrain(Unit unit,int targetId)=>unit.Alive && unit.Owner>=0 && unit.Owner<Players.Length && unit.TransportId==0 && unit.ParasiteHostId==0
        && Map.TerrainTargetById(targetId) is {Alive:true,Immune:false} target
        && TerrainVisibleTo(unit.Owner,target) && SelectTerrainWeapon(unit,target) is not null;
    public bool CanAttackGround(Unit unit,int x,int y)=>unit.Alive && unit.Owner>=0 && unit.Owner<Players.Length && unit.TransportId==0 && unit.ParasiteHostId==0
        && Map.IsPlayable(x,y) && Visible(unit.Owner,x,y) && SelectTerrainWeapon(unit,Map.TerrainTargetAt(x,y) is {Immune:false} t?t:null) is not null;

    public bool TerrainAttackInRange(Unit unit,int targetId,int cellX,int cellY)
    {
        var weapon=SelectTerrainWeapon(unit,Map.TerrainTargetById(targetId));if(weapon is null)return false;
        var dx=(long)cellX*Rules.Scale+Rules.Scale/2-unit.X;var dy=(long)cellY*Rules.Scale+Rules.Scale/2-unit.Y;
        var minimum=weapon.Id.ToUpperInvariant() switch{"V3LAUNCHER"=>5*Rules.Scale,"DREDLAUNCHER"=>8*Rules.Scale,_=>0};
        var distance=dx*dx+dy*dy;return distance>=(long)minimum*minimum && distance<=(long)weapon.RangeFixed*weapon.RangeFixed;
    }

    bool OrderTerrainAttack(GameCommand command,PlayerState player)
    {
        if(command.Actors.Length==0 || !Map.IsPlayable(command.X,command.Y) || !Visible(player.Id,command.X,command.Y) || command.Target>0)return false;
        var target=command.Target<0?Map.TerrainTargetById(command.Target):null;
        if(command.Target<0 && (target is not {Alive:true,Immune:false} || Map.TerrainTargetAt(command.X,command.Y)?.Id!=target.Id))return false;
        var selected=new List<Unit>();
        foreach(var id in command.Actors.Distinct().Order())
        {
            var entity=Find(id);if(entity is null)continue;
            if(entity is not Unit unit || unit.Owner!=player.Id)return false;
            if(CanAttackGround(unit,command.X,command.Y) && (target is null || CanAttackTerrain(unit,target.Id)))selected.Add(unit);
        }
        if(selected.Count==0)return false;
        foreach(var unit in selected)
        {
            unit.TargetId=target?.Id??0;unit.GroundTargetX=command.X*Rules.Scale+Rules.Scale/2;unit.GroundTargetY=command.Y*Rules.Scale+Rules.Scale/2;
            unit.SpecialOrder=false;unit.PendingTransportId=0;ReleaseErasure(unit);
            if(unit.Definition.AirportBound && unit.Ammo>0)unit.Returning=false;
            unit.Order=UnitOrder.AttackGround;unit.Path.Clear();unit.PathIndex=0;unit.PathVersion=-1;
            SetGoal(unit,command.X,command.Y,UnitOrder.AttackGround);
        }
        return true;
    }
    static long TerrainAimDistanceSquared(Unit unit)
    {
        var dx=(long)unit.GroundTargetX-unit.X;var dy=(long)unit.GroundTargetY-unit.Y;return dx*dx+dy*dy;
    }
    bool TerrainInAttackRange(Unit unit)
    {
        var weapon=SelectTerrainWeapon(unit,Map.TerrainTargetById(unit.TargetId));
        return weapon is not null && TerrainAimDistanceSquared(unit)<=(long)weapon.RangeFixed*weapon.RangeFixed;
    }
    void StopTerrainAttack(Unit unit)
    {unit.Order=UnitOrder.Idle;unit.TargetId=0;unit.Path.Clear();unit.PathIndex=0;}
    void UpdateTerrainCombat(Unit unit)
    {
        var target=Map.TerrainTargetById(unit.TargetId);
        if(unit.TargetId<0 && target is not {Alive:true,Immune:false}){StopTerrainAttack(unit);return;}
        var x=unit.GroundTargetX/Rules.Scale;var y=unit.GroundTargetY/Rules.Scale;
        if(!Visible(unit.Owner,x,y)){StopTerrainAttack(unit);return;}
        var weapon=SelectTerrainWeapon(unit,target);if(weapon is null){StopTerrainAttack(unit);return;}
        if(!TerrainInAttackRange(unit))
        {if(!unit.Deployed)SetGoal(unit,x,y,UnitOrder.AttackGround);return;}
        unit.Path.Clear();unit.PathIndex=0;unit.FacingX=unit.GroundTargetX-unit.X;unit.FacingY=unit.GroundTargetY-unit.Y;
        if(unit.Reload>0)return;
        var id=weapon.Id.ToUpperInvariant();var wh=TerrainWeaponWarhead(weapon).ToUpperInvariant();
        var minimum=id switch{"V3LAUNCHER"=>5*Rules.Scale,"DREDLAUNCHER"=>8*Rules.Scale,_=>0};
        if(TerrainAimDistanceSquared(unit)<(long)minimum*minimum)return;
        if(unit.Definition.Ammo>0 && unit.Ammo<=0)return;
        if(id is "HORNETLAUNCHER" or "ASWLAUNCHER" && ClassicProjectiles.Count(p=>p.Alive && p.SourceId==unit.Id)>=(id=="HORNETLAUNCHER"?3:1))return;
        unit.Reload=weapon.Reload;unit.LastFiredTick=Tick;unit.LastTarget=unit.TargetId;unit.RevealedUntil=Tick+20;EmitClassicWeapon(unit,weapon.Id);
        if(unit.Definition.Ammo>0)unit.Ammo--;
        if(target is not null && (id is "SAPPER" or "FAKEC4" || wh is "IVANBOMB" or "FAKEC4WH"))
        {
            var c4=id is "SAPPER" or "FAKEC4" || wh=="FAKEC4WH";
            if(!terrainBombs.Any(b=>b.Target==target.Id))terrainBombs.Add(new(target.Id,unit.GroundTargetX,unit.GroundTargetY,Tick+(c4?ClassicTerrainWeaponRules.C4DelayTicks:ClassicTerrainWeaponRules.IvanDelayTicks),unit.Id,unit.Owner,c4,Map.SurfaceHeightFixed(unit.GroundTargetX,unit.GroundTargetY)));
            StopTerrainAttack(unit);return;
        }
        var aim=new TerrainAim{Id=unit.TargetId,Owner=-1,Health=1,X=unit.GroundTargetX,Y=unit.GroundTargetY};
        if(TryLaunchClassicOrdnance(unit,aim,weapon))return;
        var damage=unit.Kills>=3?(int)Math.Min(int.MaxValue,(long)weapon.Damage*11/10):weapon.Damage;
        var rule=ClassicCombatRules.Warhead(weapon.Warhead);var before=Events.Count;var surface=Map.SurfaceHeightFixed(aim.X,aim.Y);
        for(var i=0;i<Math.Max(1,weapon.Burst);i++)ApplyClassicAreaDamage(unit,aim.X,aim.Y,damage,weapon.Warhead,rule.SpreadFixed,rule.EdgePermille,impactSurfaceLevelFixed:surface);
        if(Events.Count==before)Events.Add(new(Tick,unit.Id,unit.TargetId,aim.X,aim.Y,0,false,unit.X,unit.Y,unit.Type,SourceOwner:unit.Owner));
        if(unit.Type is UnitType.Terrorist or UnitType.DemolitionTruck){unit.Health=0;ApplyClassicDestroyed(unit);}
    }
    void UpdateTerrainBombs()
    {
        foreach(var bomb in terrainBombs.Where(b=>b.Due<=Tick).ToArray())
        {
            terrainBombs.Remove(bomb);if(Map.TerrainTargetById(bomb.Target) is not {Alive:true} target)continue;
            var source=Find(bomb.Source)??new ClassicDamageOrigin{Id=bomb.Source,Owner=bomb.Owner,X=bomb.X,Y=bomb.Y};
            if(bomb.C4)DamageTerrain(source,target,Math.Max(1,target.Health),bomb.X,bomb.Y);
            else
            {
                var rule=ClassicCombatRules.Warhead("IvanWH");
                ApplyClassicAreaDamage(source,bomb.X,bomb.Y,ClassicTerrainWeaponRules.IvanDamage,"IvanWH",rule.SpreadFixed,rule.EdgePermille,impactSurfaceLevelFixed:bomb.SurfaceLevelFixed);
            }
        }
    }
    int TerrainEntitySurfaceFixed(Entity entity)
    {
        if(entity is Unit unit && unit.Definition.Domain==MovementDomain.Water && Map.Inside(unit.CellX,unit.CellY)
            && (!unit.Definition.Amphibious || Map.Ground[Map.Index(unit.CellX,unit.CellY)]==Terrain.Water))return Map.WaterSurfaceLevel(unit.CellX,unit.CellY)*Rules.Scale;
        return Map.SurfaceHeightFixed(entity.X,entity.Y)+(entity is Unit air?air.Altitude:0);
    }
    void ApplyTerrainAreaDamage(Entity source,int x,int y,int damage,string warhead,int radius,int edgePermille)
    {
        if(damage<=0)return;
        var wh=warhead.ToUpperInvariant();var verses=ClassicCombatRules.Warhead(warhead).Verses;
        // Torpedo/ASW detonations stay below a high bridge's support plane.
        var submerged=wh=="APSPLASH" || source is Unit boat && new[]{boat.Definition.PrimaryWeapon,boat.Definition.SecondaryWeapon}.Any(w=>w is {Underwater:true} && w.Warhead.Equals(warhead,StringComparison.OrdinalIgnoreCase));
        foreach(var target in Map.TerrainTargets)
        {
            if(!target.Alive || target.Immune || target.Kind==TerrainTargetKind.Bridge && (submerged || !ClassicTerrainWeaponRules.BridgeWarheads.Contains(wh,StringComparer.Ordinal)))continue;
            var nearestX=Math.Clamp(x,target.CellX*Rules.Scale,(target.CellX+target.Width)*Rules.Scale-1);
            var nearestY=Math.Clamp(y,target.CellY*Rules.Scale,(target.CellY+target.Height)*Rules.Scale-1);
            var dx=(long)x-nearestX;var dy=(long)y-nearestY;var distance=IntegerSqrt(dx*dx+dy*dy);if(distance>radius)continue;
            var falloff=1000-(int)((long)(1000-edgePermille)*distance/Math.Max(1,radius));
            var multiplier=verses[ClassicCombatRules.ArmorIndex(target.Armor)];if(multiplier<=0 || falloff<=0)continue;
            var value=(long)damage*falloff/1000*multiplier/1000;
            DamageTerrain(source,target,(int)Math.Clamp(value,1,int.MaxValue),x,y);
        }
    }
    void DamageTerrain(Entity source,TerrainTarget target,int damage,int x,int y)
    {
        if(!target.Alive || target.Immune || damage<=0)return;
        var before=target.Health;Unit[] deckUnits=[];
        if(target.Kind==TerrainTargetKind.Bridge && damage>=before)
            deckUnits=Units.Where(u=>u.Alive && u.TransportId==0 && u.ParasiteHostId==0 && u.Definition.Domain==MovementDomain.Land
                && Map.Inside(u.CellX,u.CellY) && Map.Bridges[Map.Index(u.CellX,u.CellY)] && Map.BridgeIds[Map.Index(u.CellX,u.CellY)]==target.BridgeId).ToArray();
        var destroyed=Map.DamageTerrain(target.Id,damage);var actual=before-target.Health;if(actual<=0)return;
        Events.Add(new(Tick,source.Id,target.Id,x,y,actual,destroyed,source.X,source.Y,source is Unit unit?unit.Type:null,SourceOwner:source.Owner));
        if(destroyed)
            foreach(var fallen in deckUnits)
            {
                // The authoritative land layer falls with the deck. Ships use
                // WaterSurfaceLevel and are deliberately outside this selection.
                Damage(source,fallen,Math.Max(1,fallen.Health));
            }
    }
    void HashTerrainCombat(ref StableHash hash)
    {
        Map.HashTerrainState(ref hash);hash.Add(terrainBombs.Count);
        foreach(var bomb in terrainBombs){hash.Add(bomb.Target);hash.Add(bomb.X);hash.Add(bomb.Y);hash.Add(bomb.Due);hash.Add(bomb.Source);hash.Add(bomb.Owner);hash.Add(bomb.C4);hash.Add(bomb.SurfaceLevelFixed);}
    }
}
