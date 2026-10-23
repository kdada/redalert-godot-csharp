using System.Diagnostics;
using RedAlert.Simulation;

namespace RedAlert.Tests;

// Focused deterministic checks only. Lead connects --classic-combat in Program.
// A global hard cap prevents an accidental long-running gameplay regression.
public static class ClassicCombatChecks
{
    const int StepCap=900;
    static int steps;
    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    static World Fixture()
    {
        var world=new World(MatchSettings.LegacyFixture(2));
        foreach(var u in world.Units){u.DisabledUntil=int.MaxValue;u.Order=UnitOrder.Idle;u.TargetId=0;}
        for(var y=30;y<=65;y++)for(var x=30;x<=65;x++)
        {
            var i=world.Map.Index(x,y);world.Map.Ground[i]=Terrain.Grass;world.Map.Elevation[i]=0;world.Map.Bridges[i]=false;world.Map.Ramps[i]=RampDirection.None;
        }
        foreach(var p in world.Players)Array.Fill(p.Visibility,(byte)3);
        return world;
    }
    static Unit Put(World world,int owner,UnitType type,int x,int y,bool passive=false)
    {
        var u=world.SpawnUnit(owner,type,x,y);u.X=x*Rules.Scale+Rules.Scale/2;u.Y=y*Rules.Scale+Rules.Scale/2;u.GoalX=x;u.GoalY=y;
        u.DisabledUntil=passive?int.MaxValue:0;u.Order=UnitOrder.Idle;u.TargetId=0;u.Ammo=u.Definition.Ammo;
        if(u.Definition.Domain==MovementDomain.Air)u.Altitude=5*Rules.Scale;
        // Debug spawning does not call Reveal; provide the same sight the
        // scripted order would have after a normal visibility update.
        foreach(var player in world.Players)Array.Fill(player.Visibility,(byte)3);
        return u;
    }
    static void Advance(World world,int count=1)
    {
        for(var i=0;i<count;i++)
        {
            if(++steps>StepCap)throw new InvalidOperationException("classic combat check exceeded hard 900-step cap");
            world.Step();
        }
    }
    static void Attack(Unit source,Entity target){source.TargetId=target.Id;source.Order=UnitOrder.Attack;source.Reload=0;}
    static GameCommand Special(World world,Unit source,Entity? target=null)=>new(){Tick=world.Tick,Player=source.Owner,Type=CommandType.Special,Actors=[source.Id],Target=target?.Id??0};
    static void Water(World w,int left,int top,int right,int bottom)
    {for(var y=top;y<=bottom;y++)for(var x=left;x<=right;x++)w.Map.Ground[w.Map.Index(x,y)]=Terrain.Water;}
    public static int Run(string[] args)
    {
        var watch=Stopwatch.StartNew();steps=0;var total=0;var failed=0;
        void Test(string name,Action action)
        {total++;try{action();Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.Error.WriteLine("FAIL "+name+": "+error.Message);}}
        Test("original weapon planes and armor verses distinguish AA, tanks, surface and underwater",()=>
        {
            var w=Fixture();Water(w,35,35,45,45);
            var tank=Put(w,0,UnitType.Grizzly,48,40);var aircraft=Put(w,1,UnitType.Rocketeer,47,40,true);
            var aegis=Put(w,0,UnitType.AegisCruiser,41,40);var enemyTank=Put(w,1,UnitType.Rhino,46,40,true);
            var sub=Put(w,1,UnitType.AttackSubmarine,40,38,true);var destroyer=Put(w,0,UnitType.Destroyer,39,40);
            Require(!w.CanTarget(tank,aircraft),"Grizzly fires on an airborne Rocketeer");
            Require(w.CanTarget(aegis,aircraft) && !w.CanTarget(aegis,enemyTank),"Aegis planes are not AA-only");
            Require(!w.CanTarget(tank,sub) && w.CanTarget(destroyer,sub),"submarine detection or ASW selection failed");
            Require(!w.CanTarget(sub,tank),"torpedo attacks a land tank");
            var harrier=Put(w,1,UnitType.Harrier,47,43,true);harrier.Altitude=0;
            Require(w.CanTarget(tank,harrier) && !w.CanTarget(aegis,harrier),"docked aircraft did not change to the ground target plane");
            var infantry=Put(w,1,UnitType.Gi,49,40,true);
            Require(w.ClassicDamageAgainst(infantry,100,"AP")==25 && w.ClassicDamageAgainst(enemyTank,100,"AP")==100,"AP did not use the original 25% infantry / 100% heavy armor columns");
            Require(w.ClassicDamageAgainst(enemyTank,100,"HollowPoint")==1,"small arms armor damage is still the old infantry/3 heuristic");
            var miner=Put(w,1,UnitType.WarMiner,49,43,true);
            Require(w.ClassicDamageAgainst(miner,125,"RadBeamWarhead")==0,"radiation-immune miner takes radiation beam damage");
        });
        Test("GI deployment changes actual weapon reach and keeps the deployed soldier stationary",()=>
        {
            var w=Fixture();var gi=Put(w,0,UnitType.Gi,40,40);var target=Put(w,1,UnitType.Conscript,45,40,true);
            target.X-=Rules.Scale/2;var startX=gi.X;var startY=gi.Y;
            Require(w.DeployClassicUnit(gi) && gi.Deployed && !w.ClassicUnitCanMove(gi),"GI deployment did not lock movement");
            Attack(gi,target);var hp=target.Health;Advance(w,3);
            Require(target.Health<hp && gi.X==startX && gi.Y==startY,"Para did not fire beyond undeployed M60 range while stationary");
            Require(w.DeployClassicUnit(gi) && !gi.Deployed,"GI cannot undeploy");
        });
        Test("mind control changes ownership and controller death restores the original owner",()=>
        {
            var w=Fixture();var yuri=Put(w,0,UnitType.Yuri,40,40);var target=Put(w,1,UnitType.Grizzly,42,40,true);
            Attack(yuri,target);Advance(w);
            Require(target.Owner==0 && target.OriginalOwner==1 && target.ControllerId==yuri.Id,"Yuri did not establish authoritative control");
            yuri.Health=0;w.ApplyClassicDestroyed(yuri);
            Require(target.Owner==1 && target.ControllerId==0 && yuri.ControlledTargetId==0,"controller death failed to restore the victim");
            var immune=Put(w,1,UnitType.ChronoMiner,43,40,true);var other=Put(w,0,UnitType.Yuri,41,40);
            Require(!w.CanTarget(other,immune),"Yuri controls an immune miner");
        });
        Test("chrono erasure freezes its victim, cancels on attacker death and completes over time",()=>
        {
            var w=Fixture();var legion=Put(w,0,UnitType.ChronoLegionnaire,40,40);var target=Put(w,1,UnitType.Grizzly,42,40,true);
            Attack(legion,target);var hp=target.Health;Advance(w);
            Require(target.ErasedById==legion.Id && target.EraseProgress>0 && !w.ClassicUnitCanMove(target),"temporal beam did not freeze and advance erasure");
            legion.Health=0;w.ApplyClassicDestroyed(legion);
            Require(target.ErasedById==0 && target.EraseProgress==0 && target.Health==hp,"interrupted beam did not release the victim intact");
            var next=Put(w,0,UnitType.ChronoLegionnaire,40,41);Attack(next,target);
            for(var i=0;i<30 && target.Alive;i++)Advance(w);
            Require(!target.Alive,"sustained erasure never completed");
        });
        Test("Spy lab infiltration unlocks stolen technology; C4 and Ivan use distinct delayed bombs",()=>
        {
            var w=Fixture();var lab=w.SpawnBuilding(1,BuildingType.BattleLab,42,40);var spy=Put(w,0,UnitType.Spy,41,41);
            Require(w.OrderSpecial(Special(w,spy,lab),w.Players[0]),"Spy lab order rejected");Advance(w);
            Require(!spy.Alive && w.Players[0].StolenSovietTech && !w.Players[0].StolenAlliedTech && lab.Owner==1,"Spy did not steal the infiltrated lab's original side technology");
            var power=w.SpawnBuilding(1,BuildingType.PowerPlant,48,44);var tanya=Put(w,0,UnitType.Tanya,47,44);
            Require(w.OrderSpecial(Special(w,tanya,power),w.Players[0]),"Tanya C4 order rejected");Advance(w);
            Require(power.BombIsC4 && power.BombTick==w.Tick-1+9,"C4 did not use .03 minutes = nine fastest-speed ticks");
            Advance(w,10);Require(!power.Alive && tanya.Alive,"C4 did not destroy the building and preserve Tanya");
            var ivan=Put(w,0,UnitType.CrazyIvan,51,47);var tank=Put(w,1,UnitType.Rhino,52,47,true);
            Require(w.OrderSpecial(Special(w,ivan,tank),w.Players[0]),"Ivan bomb order rejected");Advance(w);
            var deadline=tank.BombTick;Require(deadline==w.Tick-1+150 && !tank.BombIsC4,"Ivan did not use the original 450-frame fuse");
            while(w.Tick<deadline)Advance(w);Require(tank.Alive,"Ivan bomb exploded before its fuse");Advance(w);
            Require(!tank.Alive || tank.Health<tank.MaxHealth,"Ivan bomb produced no explosion damage");
        });
        Test("drone parasites, deployed radiation and demolition suicide alter real combat state",()=>
        {
            var w=Fixture();var drone=Put(w,0,UnitType.TerrorDrone,40,40);var tank=Put(w,1,UnitType.Rhino,41,40,true);
            Attack(drone,tank);Advance(w);
            Require(drone.ParasiteHostId==tank.Id && tank.ParasiteId==drone.Id,"drone did not enter its host");
            var hp=tank.Health;Advance(w,22);Require(tank.Health<hp,"attached drone does not damage its host");
            w.CureClassicParasite(tank);Require(tank.ParasiteId==0 && !drone.Alive,"repair did not remove the parasite");
            var deso=Put(w,0,UnitType.Desolator,48,48);var victim=Put(w,1,UnitType.Conscript,50,48,true);var miner=Put(w,1,UnitType.WarMiner,48,50,true);var minerHp=miner.Health;
            Require(w.DeployClassicUnit(deso),"Desolator deployment rejected");Advance(w,14);
            Require(!victim.Alive && miner.Health==minerHp && w.ClassicAreaEffects.Any(e=>e.Kind=="Radiation"),"radiation did not damage infantry and spare immune miners");
            var truck=Put(w,0,UnitType.DemolitionTruck,55,45);var enemy=Put(w,1,UnitType.Rhino,56,45,true);hp=enemy.Health;
            Require(w.OrderSpecial(Special(w,truck),w.Players[0]) && !truck.Alive && enemy.Health<hp,"demolition truck did not perform a real area suicide explosion");
        });
        Test("Dreadnought launches two damage-bearing missiles that Aegis can intercept",()=>
        {
            var w=Fixture();Water(w,34,35,44,45);var dread=Put(w,1,UnitType.Dreadnought,36,40);var aegis=Put(w,0,UnitType.AegisCruiser,42,40);
            var target=w.SpawnBuilding(0,BuildingType.PowerPlant,51,40);var hp=target.Health;Attack(dread,target);Advance(w);
            var missiles=w.ClassicProjectiles.Select(p=>p.Id).ToHashSet();Require(missiles.Count==2 && w.ClassicProjectiles.All(p=>p.Damage==300 && p.Warhead=="DMISLWH"),"Dreadnought did not launch the original two 300-damage payloads");
            dread.DisabledUntil=int.MaxValue;var intercepted=false;
            for(var i=0;i<38;i++){Advance(w);intercepted|=w.Events.Any(e=>e.Source==aegis.Id && missiles.Contains(e.Target) && e.Damage>0);}
            Require(intercepted && w.ClassicProjectiles.Count==0 && target.Health==hp,"AA failed to intercept live missiles before their payloads hit");
        });
        Test("Iron Curtain and two-stage Chronosphere use authoritative unit state",()=>
        {
            var w=Fixture();var curtain=w.SpawnBuilding(0,BuildingType.IronCurtain,32,45);var tank=Put(w,0,UnitType.Grizzly,40,40);var infantry=Put(w,0,UnitType.Gi,41,40);
            w.Players[0].GeneratedPower=100000;w.Players[0].IronCurtainReadyTick=w.Tick;
            Require(w.OrderSuperWeapon(new(){Actors=[curtain.Id],X=40,Y=40},w.Players[0]),"charged Iron Curtain rejected");
            Require(tank.InvulnerableUntil==w.Tick+250 && !infantry.Alive && w.Players[0].IronCurtainReadyTick==w.Tick+1500,"Iron Curtain protection, organic kill or charge interval is wrong");
            var enemy=Put(w,1,UnitType.Rhino,44,40);Attack(enemy,tank);var hp=tank.Health;Advance(w,8);Require(tank.Health==hp,"Iron Curtain failed to stop real incoming weapons");
            w=Fixture();var chrono=w.SpawnBuilding(0,BuildingType.Chronosphere,32,45);tank=Put(w,0,UnitType.Grizzly,40,40);infantry=Put(w,0,UnitType.Gi,41,40);
            w.Players[0].GeneratedPower=100000;w.Players[0].ChronosphereReadyTick=w.Tick;var x=tank.X;var y=tank.Y;
            Require(w.OrderSuperWeapon(new(){Actors=[chrono.Id],X=40,Y=40},w.Players[0]) && w.Players[0].ChronosphereSelectingDestination,"Chronosphere source selection failed");
            Require(w.OrderSuperWeapon(new(){Actors=[chrono.Id],X=50,Y=50},w.Players[0]),"Chronosphere destination rejected");
            Require(tank.X==x+10*Rules.Scale && tank.Y==y+10*Rules.Scale && !infantry.Alive && tank.DisabledUntil==w.Tick+20 && !w.Players[0].ChronosphereSelectingDestination,"Chronosphere did not transfer vehicles, kill infantry and apply recovery delay");
        });
        Test("weather and nuclear superweapons create delayed damaging effects rather than instant support spawns",()=>
        {
            var w=Fixture();var weather=w.SpawnBuilding(0,BuildingType.WeatherControl,32,45);var target=w.SpawnBuilding(1,BuildingType.PowerPlant,50,50);
            w.Players[0].GeneratedPower=100000;w.Players[0].WeatherReadyTick=0;
            Require(w.OrderSuperWeapon(new(){Actors=[weather.Id],X=50,Y=50},w.Players[0]),"charged weather control rejected");
            var hp=target.Health;Advance(w,84);Require(target.Health==hp,"weather ignored its original deferment");
            Advance(w,16);Require(target.Health<hp && w.ClassicAreaEffects.Any(e=>e.Kind=="Weather"),"weather never produced damaging lightning");
            w=Fixture();var silo=w.SpawnBuilding(0,BuildingType.NuclearMissile,32,45);var tank=Put(w,1,UnitType.Rhino,50,50,true);
            w.Players[0].GeneratedPower=100000;w.Players[0].NukeReadyTick=0;
            Require(w.OrderSuperWeapon(new(){Actors=[silo.Id],X=50,Y=50},w.Players[0]),"charged nuclear missile rejected");
            Advance(w,30);Require(tank.Alive,"nuclear missile struck before its flight finished");Advance(w);
            Require(!tank.Alive && w.ClassicAreaEffects.Any(e=>e.Kind=="Radiation"),"nuclear missile did not detonate and leave radiation");
        });
        Console.WriteLine($"Classic combat: {total-failed}/{total} passed; {steps}/{StepCap} capped world steps; {watch.ElapsedMilliseconds} ms.");
        return failed==0?0:1;
    }
}
