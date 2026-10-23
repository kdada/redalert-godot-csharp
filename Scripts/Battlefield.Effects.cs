using Godot;
using RedAlert.Simulation;
using EffectSprite=RedAlert.ArtLibrary.CombatSprite;

namespace RedAlert;

public partial class Battlefield
{
    enum WeaponKind { Impact, Cannon, GrandCannon, Rifle, Sniper, Tesla, Dog, Missile, Flak, PrismPulse }
    struct WeaponVisual
    {
        public bool Active,SourceSeen,TargetSeen,HasSource,Destroyed,TargetInfantry;
        public WeaponKind Kind;
        public Vector2 Source,Target;
        public Country Country;
        public int Owner,Barrel,Damage,Flight,Delay,Lifetime;
        public uint Seed;
        public ulong Born;
        public float SourceHeight;
    }
    const int MaximumWeaponEffects=384;
    readonly WeaponVisual[] weaponEffects=new WeaponVisual[MaximumWeaponEffects];
    World? weaponWorld;
    int weaponCursor,weaponCount,weaponCaptureTick=-1,weaponDetail=-1,weaponViewer=-2,weaponDrawBudget;
    bool weaponSpectating;
    public int ActiveWeaponEffects=>weaponCount;
    public int WeaponEffectDrawCommands {get;private set;}

    void ClearWeaponEffects()
    {
        Array.Clear(weaponEffects);weaponCount=0;weaponCursor=0;
    }
    World? PrepareWeaponContext()
    {
        var world=Controller.Battle;
        var detail=Math.Clamp(Controller.VisualDetail,0,2);
        if(!ReferenceEquals(world,weaponWorld) || world is not null && world.Tick<weaponCaptureTick ||
           weaponViewer!=Controller.LocalPlayer || weaponSpectating!=Controller.IsSpectating || weaponDetail!=detail)
        {
            ClearWeaponEffects();weaponCaptureTick=-1;weaponWorld=world;
            weaponViewer=Controller.LocalPlayer;weaponSpectating=Controller.IsSpectating;weaponDetail=detail;
        }
        return world;
    }
    bool WeaponVisible(Vector2 cell)
    {
        var world=Controller.Battle;if(world is null)return false;
        var x=(int)MathF.Floor(cell.X);var y=(int)MathF.Floor(cell.Y);
        return world.Map.IsPlayable(x,y) && (Controller.IsSpectating || Controller.ViewVisible(x,y));
    }
    static WeaponKind WeaponFor(UnitType? unit,BuildingType? building)
    {
        if(building==BuildingType.GrandCannon)return WeaponKind.GrandCannon;
        if(building==BuildingType.TeslaCoil || unit==UnitType.TeslaTank)return WeaponKind.Tesla;
        if(building==BuildingType.Pillbox)return WeaponKind.Rifle;
        return unit switch
        {
            UnitType.Grizzly or UnitType.Rhino or UnitType.Apocalypse=>WeaponKind.Cannon,
            UnitType.Gi or UnitType.Conscript or UnitType.WarMiner=>WeaponKind.Rifle,
            UnitType.Sniper=>WeaponKind.Sniper,UnitType.Dog=>WeaponKind.Dog,
            UnitType.Ifv=>WeaponKind.Missile,UnitType.FlakTrack=>WeaponKind.Flak,
            UnitType.PrismTank=>WeaponKind.PrismPulse,_=>WeaponKind.Impact
        };
    }
    static uint WeaponHash(uint value)
    {
        unchecked{value^=value>>16;value*=0x7feb352d;value^=value>>15;value*=0x846ca68b;return value^(value>>16);}
    }
    static float WeaponNoise(uint seed,int sample)=>
        (WeaponHash(unchecked(seed+(uint)sample*0x9e3779b9))&65535)/65535f;
    void AddWeaponVisual(WeaponVisual effect)
    {
        var capacity=weaponDetail>=2?MaximumWeaponEffects:128;
        weaponCursor%=capacity;
        if(!weaponEffects[weaponCursor].Active)weaponCount++;
        effect.Active=true;weaponEffects[weaponCursor]=effect;weaponCursor=(weaponCursor+1)%capacity;
    }

    // Called once after each simulation step by the existing CaptureEvents wrapper.
    // This method only snapshots presentation data; it never mutates an entity/event.
    void CaptureWeaponEffects()
    {
        var world=PrepareWeaponContext();if(world is null)return;
        if(weaponCaptureTick==world.Tick)return;
        weaponCaptureTick=world.Tick;
        if(weaponDetail==0){ClearWeaponEffects();return;}
        var now=Time.GetTicksMsec();
        var capacity=weaponDetail>=2?MaximumWeaponEffects:128;
        // A large splash batch cannot cause unbounded capture work or allocations.
        var start=Math.Max(0,world.Events.Count-capacity*2);
        for(var index=start;index<world.Events.Count;index++)
        {
            var e=world.Events[index];
            var target=new Vector2(e.X/(float)Rules.Scale,e.Y/(float)Rules.Scale);
            var targetSeen=WeaponVisible(target);
            // Legacy events may omit metadata. Resolve at capture only; draw never
            // looks up a source, so source death cannot erase an in-flight effect.
            var supplied=e.SourceUnit is not null || e.SourceBuilding is not null || e.SourceX!=0 || e.SourceY!=0;
            var source=!supplied && e.Source>0?world.Find(e.Source):null;
            var unit=e.SourceUnit??(source as Unit)?.Type;
            var building=e.SourceBuilding??(source as Building)?.Type;
            var origin=supplied?new Vector2(e.SourceX/(float)Rules.Scale,e.SourceY/(float)Rules.Scale):
                source is not null?new Vector2(source.X/(float)Rules.Scale,source.Y/(float)Rules.Scale):target;
            var hasSource=e.Source>0 && (supplied || source is not null) && !e.Secondary;
            var sourceSeen=hasSource && WeaponVisible(origin);
            if(!sourceSeen && !targetSeen)continue;
            var kind=hasSource?WeaponFor(unit,building):WeaponKind.Impact;
            var distance=origin.DistanceTo(target);
            var flight=kind switch
            {
                WeaponKind.GrandCannon=>(int)Math.Clamp(350+distance*34,380,760),
                WeaponKind.Cannon=>(int)Math.Clamp(110+distance*33,140,440),
                WeaponKind.Missile=>(int)Math.Clamp(180+distance*42,200,560),
                WeaponKind.Flak=>(int)Math.Clamp(80+distance*25,100,280),
                WeaponKind.PrismPulse=>(int)Math.Clamp(70+distance*18,90,230),_=>0
            };
            var owner=supplied?e.SourceOwner:source?.Owner??0;
            var country=owner>=0 && owner<world.Players.Length?world.Players[owner].Setup.Country:Country.Russia;
            var height=building switch
            {
                BuildingType.TeslaCoil=>52f,BuildingType.GrandCannon=>34f,BuildingType.Pillbox=>18f,
                _=>unit is UnitType.Gi or UnitType.Conscript or UnitType.Sniper?16f:unit==UnitType.Dog?5f:14f
            };
            var effect=new WeaponVisual
            {
                Kind=kind,Source=origin,Target=target,HasSource=hasSource,SourceSeen=sourceSeen,TargetSeen=targetSeen,
                TargetInfantry=e.TargetInfantry,Destroyed=e.Explosion,Owner=Math.Clamp(owner,0,7),Country=country,
                Damage=e.Damage,Flight=flight,Born=now,SourceHeight=height,
                Seed=WeaponHash(unchecked((uint)e.Tick*397u^(uint)e.Source*31u^(uint)e.Target)),
                Lifetime=flight+(e.Explosion?1450:kind==WeaponKind.Tesla?360:kind==WeaponKind.Dog?410:520)
            };
            // One authoritative hit gets two cosmetic barrel flashes/shells. The
            // second shell applies no damage and never adds a simulation event.
            if(unit==UnitType.Apocalypse && hasSource)
            {
                effect.Barrel=-1;AddWeaponVisual(effect);
                effect.Barrel=1;effect.Delay=105;effect.Destroyed=false;effect.Seed=WeaponHash(effect.Seed+1);
                effect.Lifetime=flight+600;AddWeaponVisual(effect);
            }
            else AddWeaponVisual(effect);
        }
    }

    bool SpendWeaponDraw(int commands=1)
    {
        if(weaponDrawBudget<commands)return false;
        weaponDrawBudget-=commands;WeaponEffectDrawCommands+=commands;return true;
    }
    void WeaponLine(Vector2 a,Vector2 b,Color color,float width=1)
    {
        if(SpendWeaponDraw())DrawLine(a,b,color,Math.Max(.7f,width*Zoom));
    }
    void WeaponSprite(EffectSprite sprite,Vector2 point,float scale,float phase,float opacity=1)
    {
        if(opacity<=.01f || !SpendWeaponDraw())return;
        if(!Controller.Graphics.DrawCombatSprite(this,sprite,point,scale*Zoom,phase,opacity))
        {
            // Missing optional atlas still yields a cheap local flash, never a beam.
            var color=sprite==EffectSprite.Smoke?new Color(.35f,.35f,.31f,opacity*.4f):new Color(1,.65f,.24f,opacity*(1-Math.Clamp(phase,0,1)));
            DrawCircle(point,Math.Max(1,(3+phase*8)*scale*Zoom),color);
        }
    }
    bool WeaponInView(Vector2 source,Vector2 target)
    {
        var margin=160*Zoom;
        return Math.Max(source.X,target.X)>=-margin && Math.Min(source.X,target.X)<=Size.X+margin &&
               Math.Max(source.Y,target.Y)>=-margin && Math.Min(source.Y,target.Y)<=Size.Y+margin;
    }
    bool WeaponPathVisible(Vector2 a,Vector2 b)
    {
        // Connecting energy/tracers never bridge an unseen patch of terrain.
        var steps=Math.Clamp((int)MathF.Ceiling(a.DistanceTo(b)*2),1,32);
        for(var i=0;i<=steps;i++)if(!WeaponVisible(a.Lerp(b,i/(float)steps)))return false;
        return true;
    }
    Vector2 WeaponOrigin(in WeaponVisual e,Vector2 projectedSource,Vector2 direction)
    {
        var reach=e.Kind switch{WeaponKind.Cannon or WeaponKind.GrandCannon=>25f,WeaponKind.Dog=>0f,WeaponKind.Tesla=>0f,_=>10f};
        var side=new Vector2(-direction.Y,direction.X)*e.Barrel*4;
        return projectedSource+(direction*reach+side-new Vector2(0,e.SourceHeight))*Zoom;
    }
    Vector2 WeaponFlightPoint(in WeaponVisual e,Vector2 start,Vector2 finish,float fraction)
    {
        var p=start.Lerp(finish,fraction);
        if(e.Kind==WeaponKind.GrandCannon)
        {
            var arc=Math.Clamp(e.Source.DistanceTo(e.Target)*7,36,92);
            p.Y-=MathF.Sin(fraction*MathF.PI)*arc*Zoom;
        }
        return p;
    }

    // Called at the old effects position in _Draw, before the shroud overlay.
    void DrawWeaponEffects()
    {
        var world=PrepareWeaponContext();WeaponEffectDrawCommands=0;
        if(world is null || weaponDetail==0)return;
        weaponDrawBudget=weaponDetail>=2?900:260;
        var now=Time.GetTicksMsec();
        var capacity=weaponDetail>=2?MaximumWeaponEffects:128;
        for(var offset=0;offset<capacity;offset++)
        {
            var index=(weaponCursor-1-offset+capacity)%capacity;
            ref var e=ref weaponEffects[index];if(!e.Active)continue;
            var elapsed=(float)(now>=e.Born?now-e.Born:0)-e.Delay;
            if(elapsed>e.Lifetime){e.Active=false;weaponCount--;continue;}
            if(elapsed<0 || weaponDrawBudget<=0)continue;
            var source=ProjectGround(e.Source);var target=ProjectGround(e.Target);
            if(!WeaponInView(source,target))continue;
            var rawDirection=target-source;
            var direction=rawDirection.LengthSquared()>.01f?rawDirection.Normalized():Vector2.Right;
            var start=WeaponOrigin(e,source,direction);
            var finish=target-new Vector2(0,e.TargetInfantry?8:12)*Zoom;
            var originVisible=e.SourceSeen && WeaponVisible(e.Source);
            var targetVisible=e.TargetSeen && WeaponVisible(e.Target);
            var connect=originVisible && targetVisible;
            DrawWeaponMuzzle(e,start,direction,elapsed,originVisible);
            if(connect)
            {
                if(e.Flight>0 && elapsed<e.Flight)DrawWeaponProjectile(e,start,finish,elapsed/e.Flight,elapsed);
                else if((e.Kind is WeaponKind.Rifle or WeaponKind.Sniper) && elapsed<75)
                    DrawWeaponTracer(e,start,finish,elapsed);
                else if(e.Kind==WeaponKind.Tesla && elapsed<265 && WeaponPathVisible(e.Source,e.Target))
                    DrawTeslaBolt(e,start,finish,elapsed);
                else if(e.Kind==WeaponKind.Dog && elapsed<300)DrawDogAttack(e,source,elapsed);
            }
            if(targetVisible && elapsed>=e.Flight)DrawWeaponImpact(e,finish,elapsed-e.Flight);
        }
    }

    void DrawWeaponMuzzle(in WeaponVisual e,Vector2 start,Vector2 direction,float age,bool visible)
    {
        if(!visible || !e.HasSource || e.Kind is WeaponKind.Dog or WeaponKind.Impact)return;
        if(e.Kind==WeaponKind.Tesla)
        {
            if(age<200)WeaponSprite(EffectSprite.ElectricImpact,start,.3f,age/280);
            return;
        }
        if(age>110)return;
        var sniper=e.Kind==WeaponKind.Sniper;
        var scale=e.Kind==WeaponKind.GrandCannon?1.05f:e.Kind==WeaponKind.Cannon?.7f:sniper?.38f:.4f;
        WeaponSprite(sniper?EffectSprite.SniperFlash:EffectSprite.Muzzle,start,scale,age/110);
        if(weaponDetail>=2 && age<70)
            WeaponLine(start,start+direction*(sniper?9:6)*Zoom,new Color(1,.96f,.73f,1-age/90),1.5f);
    }
    void DrawWeaponTracer(in WeaponVisual e,Vector2 start,Vector2 finish,float age)
    {
        var t=Math.Clamp(age/65,0,1);
        var tail=Math.Max(0,t-(e.Kind==WeaponKind.Sniper?.045f:.09f));
        if(!WeaponPathVisible(e.Source.Lerp(e.Target,tail),e.Source.Lerp(e.Target,t)))return;
        WeaponLine(start.Lerp(finish,tail),start.Lerp(finish,t),e.Kind==WeaponKind.Sniper?
            new Color(.94f,.96f,.84f,1-age/85):new Color(1,.81f,.42f,1-age/90),e.Kind==WeaponKind.Sniper?.8f:1.1f);
    }
    void DrawWeaponProjectile(in WeaponVisual e,Vector2 start,Vector2 finish,float t,float age)
    {
        var cell=e.Source.Lerp(e.Target,t);if(!WeaponVisible(cell))return;
        var position=WeaponFlightPoint(e,start,finish,t);
        var previousT=Math.Max(0,t-.035f);
        var previous=WeaponFlightPoint(e,start,finish,previousT);
        var direction=(position-previous).Normalized();if(direction==Vector2.Zero)direction=Vector2.Right;
        var pulse=e.Kind==WeaponKind.PrismPulse;
        var trailCount=weaponDetail>=2?3:1;
        if(!pulse)
        for(var i=1;i<=trailCount;i++)
        {
            var fraction=t-i*(e.Kind==WeaponKind.Missile?.045f:.055f);if(fraction<0)continue;
            if(!WeaponVisible(e.Source.Lerp(e.Target,fraction)))continue;
            var puff=WeaponFlightPoint(e,start,finish,fraction);
            WeaponSprite(EffectSprite.Smoke,puff,.22f+i*.035f,.15f+i*.13f,.45f-i*.07f);
        }
        if(pulse)
        {
            var side=new Vector2(-direction.Y,direction.X)*3*Zoom;var tip=direction*5*Zoom;
            WeaponLine(position-tip,position+side,new Color(.4f,.8f,1),1.5f);
            WeaponLine(position+side,position+tip,new Color(.83f,.98f,1),1.5f);
            WeaponLine(position+tip,position-side,new Color(.83f,.98f,1),1.5f);
            WeaponLine(position-side,position-tip,new Color(.4f,.8f,1),1.5f);
            return;
        }
        var length=e.Kind==WeaponKind.GrandCannon?7:e.Kind==WeaponKind.Missile?8:4;
        WeaponLine(position-direction*length*Zoom,position,new Color(.32f,.28f,.19f),3);
        WeaponLine(position-direction*(length-1)*Zoom,position,new Color(1,.91f,.58f),1.5f);
        if(e.Kind==WeaponKind.Missile)
            WeaponLine(position-direction*(length+4)*Zoom,position-direction*length*Zoom,new Color(1,.41f,.12f),2);
        if(e.Kind==WeaponKind.GrandCannon && weaponDetail>=2 && SpendWeaponDraw())
            DrawCircle(ProjectGround(cell),2.5f*Zoom,new Color(0,0,0,.22f));
    }
    void DrawTeslaBolt(in WeaponVisual e,Vector2 start,Vector2 finish,float age)
    {
        var segments=weaponDetail>=2?12:7;
        var perpendicular=new Vector2(-(finish-start).Y,(finish-start).X).Normalized();
        var previous=start;var slice=(int)(age/42);
        var alpha=Math.Clamp(1-age/290,.05f,1);
        for(var i=1;i<=segments;i++)
        {
            var t=i/(float)segments;
            var point=start.Lerp(finish,t);
            if(i<segments)point+=perpendicular*(WeaponNoise(e.Seed,i+slice*19)*2-1)*
                MathF.Sin(t*MathF.PI)*(weaponDetail>=2?10:7)*Zoom;
            if(weaponDetail>=2)WeaponLine(previous,point,new Color(.24f,.42f,.95f,alpha*.55f),4);
            WeaponLine(previous,point,new Color(.72f,.91f,1,alpha),1.3f);
            // One short branch only, rather than a branching tree of draw calls.
            if(weaponDetail>=2 && i==segments/2)
                WeaponLine(point,point+perpendicular*(5+WeaponNoise(e.Seed,slice)*6)*Zoom,new Color(.58f,.79f,1,alpha*.7f));
            previous=point;
        }
    }
    void DrawDogAttack(in WeaponVisual e,Vector2 source,float age)
    {
        if(weaponDetail<2)return;
        var phase=age/300;var direction=ProjectGround(e.Target)-source;
        var lunge=direction.LengthSquared()>.01f?direction.Normalized()*MathF.Sin(phase*MathF.PI)*7*Zoom:Vector2.Zero;
        // Original SHP pose plus shadow requires at most two existing cached draws.
        if(!SpendWeaponDraw(2))return;
        Controller.Graphics.DrawCombatDog(this,e.Country,e.Owner,e.Target-e.Source,source+lunge,Zoom,phase);
    }
    void DrawWeaponImpact(in WeaponVisual e,Vector2 point,float age)
    {
        if(e.Destroyed && !e.TargetInfantry)
        {
            if(age<700)WeaponSprite(EffectSprite.Explosion,point-new Vector2(0,3)*Zoom,1.2f+Math.Clamp(e.Damage,0,250)/500f,age/700);
            var count=weaponDetail>=2?3:1;
            for(var i=0;i<count;i++)
            {
                var smokeAge=age-100-i*100;if(smokeAge<0 || smokeAge>1250)continue;
                var smokeCell=e.Target+new Vector2((i-1)*.08f,(i-1)*.06f);
                if(!WeaponVisible(smokeCell))continue;
                var drift=new Vector2((WeaponNoise(e.Seed,i)-.5f)*12,-smokeAge*.014f)*Zoom;
                WeaponSprite(EffectSprite.Smoke,point+drift,1.1f+i*.15f,smokeAge/1250,.8f);
            }
            if(age<620)DrawWeaponDebris(e,point,age);
            return;
        }
        if(e.Destroyed && e.TargetInfantry)
        {
            if(age<600)WeaponSprite(EffectSprite.Dust,point+new Vector2(0,7)*Zoom,.7f,age/600);
            if(age<220)WeaponSprite(e.Kind==WeaponKind.Dog?EffectSprite.Bite:EffectSprite.RifleImpact,point,.6f,age/220);
            return;
        }
        switch(e.Kind)
        {
            case WeaponKind.Tesla:
            case WeaponKind.PrismPulse:
                if(age<320)WeaponSprite(EffectSprite.ElectricImpact,point,e.Kind==WeaponKind.Tesla?.7f:.5f,age/320);
                break;
            case WeaponKind.Cannon:
            case WeaponKind.GrandCannon:
            case WeaponKind.Missile:
                if(age<400)WeaponSprite(EffectSprite.Explosion,point,e.Kind==WeaponKind.GrandCannon?.85f:.5f,age/400);
                if(weaponDetail>=2 && age>100 && age<520)WeaponSprite(EffectSprite.Smoke,point,.45f,(age-100)/420,.55f);
                break;
            case WeaponKind.Dog:
                if(age<330)WeaponSprite(EffectSprite.Bite,point,.65f,age/330);
                break;
            case WeaponKind.Flak:
                if(age<350)WeaponSprite(EffectSprite.Dust,point,.7f,age/350);
                if(age<200)WeaponSprite(EffectSprite.RifleImpact,point,.65f,age/200);
                break;
            default:
                if(age<240)WeaponSprite(EffectSprite.RifleImpact,point,e.Kind==WeaponKind.Sniper?.55f:.42f,age/240);
                break;
        }
    }
    void DrawWeaponDebris(in WeaponVisual e,Vector2 point,float age)
    {
        var t=age/620;var count=weaponDetail>=2?5:2;
        for(var i=0;i<count;i++)
        {
            var angle=WeaponNoise(e.Seed,i+40)*MathF.Tau;
            var distance=(14+WeaponNoise(e.Seed,i+50)*25)*t;
            var cell=e.Target+new Vector2(MathF.Cos(angle),MathF.Sin(angle))*t*.35f;
            if(!WeaponVisible(cell))continue;
            var offset=new Vector2(MathF.Cos(angle)*distance,MathF.Sin(angle)*distance*.45f-32*MathF.Sin(t*MathF.PI))*Zoom;
            var p=point+offset;
            var color=t<.25f?new Color(1,.69f,.27f,1-t):new Color(.32f,.30f,.24f,1-t);
            WeaponLine(p,p+new Vector2(2+(i%2),-1)*Zoom,color,2);
        }
    }
}
