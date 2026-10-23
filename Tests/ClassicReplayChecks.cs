using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using RedAlert.Simulation;

namespace RedAlert.Tests;

// Pure simulation checks. Lead connects --classic-replay in Program; no Godot.
public static class ClassicReplayChecks
{
    const int ReplayTicks=30,StepCap=100;
    static int steps;
    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    static void Changed(World world,Action change,Action restore,string name)
    {
        var before=world.StateHash();
        try{change();Require(world.StateHash()!=before,"hash omitted "+name);}
        finally{restore();}
        Require(world.StateHash()==before,"mutation did not restore "+name);
    }
    static object? Alternate(Type type,object? value)
    {
        if(type==typeof(int))return unchecked((int)value!+1);
        if(type==typeof(uint))return unchecked((uint)value!+1);
        if(type==typeof(bool))return !(bool)value!;
        if(type==typeof(string))return (string?)value+"#changed";
        if(type.IsEnum)return Enum.GetValues(type).Cast<object>().First(other=>!Equals(other,value));
        if(Nullable.GetUnderlyingType(type) is {IsEnum:true} underlying)return value is null?Enum.GetValues(underlying).GetValue(0):null;
        throw new InvalidOperationException("unsupported check member "+type);
    }
    static bool Scalar(Type type)=>type==typeof(int) || type==typeof(uint) || type==typeof(bool) || type==typeof(string) || type.IsEnum || Nullable.GetUnderlyingType(type)?.IsEnum==true;
    static int CheckScalars(World world,object target)
    {
        var count=0;
        foreach(var field in target.GetType().GetFields(BindingFlags.Public|BindingFlags.Instance).Where(field=>!field.IsInitOnly && Scalar(field.FieldType)))
        {
            var original=field.GetValue(target);var changed=Alternate(field.FieldType,original);
            Changed(world,()=>field.SetValue(target,changed),()=>field.SetValue(target,original),target.GetType().Name+"."+field.Name);count++;
        }
        // Record init properties are also checked: replacing a queued immutable
        // record with a changed copy must not leave the checkpoint unchanged.
        foreach(var property in target.GetType().GetProperties(BindingFlags.Public|BindingFlags.Instance)
            .Where(property=>property.GetIndexParameters().Length==0 && property.SetMethod is {IsPublic:true} && Scalar(property.PropertyType)))
        {
            var original=property.GetValue(target);var changed=Alternate(property.PropertyType,original);
            Changed(world,()=>property.SetValue(target,changed),()=>property.SetValue(target,original),target.GetType().Name+"."+property.Name);count++;
        }
        return count;
    }
    static void Ordered<T>(World world,List<T> list,T first,T second,string name)
    {
        var saved=list.ToArray();list.Clear();
        try
        {
            Changed(world,()=>list.Add(first),()=>list.Clear(),name+" count");
            list.Add(first);list.Add(second);
            Changed(world,()=>list.Reverse(),()=>list.Reverse(),name+" order");
        }
        finally{list.Clear();list.AddRange(saved);}
    }
    static ClassicMapDefinition OriginalMap(string source,bool block=false)
    {
        var map=new ClassicMapDefinition
        {
            Schema=1,Id="classic-replay-hash-check",Name="hash fixture",SourcePath="test-original.map",SourceSha256=source,Theater="TEMPERATE",Width=32,Height=32,
            SourceSize=new(){Width=32,Height=32},LocalSize=new(){Width=32,Height=32},PlayableBounds=new(){MinU=-32,MaxU=32,MinV=0,MaxV=64},
            ShipyardsAllowed=true,Spawns=[new(){Index=0,X=7,Y=7,SourceX=7,SourceY=7},new(){Index=1,X=24,Y=24,SourceX=24,SourceY=24}],
            RuntimeCells=Enumerable.Range(0,32*32).Select(index=>new ClassicMapCell
            {
                X=index%32,Y=index/32,SourceX=index%32,SourceY=index/32,Playable=true,Ground=Terrain.Grass,UnderlyingGround=Terrain.Grass,
                LandPassable=!(block && index==16*32+16),Buildable=!(block && index==16*32+16)
            }).ToArray()
        };
        return ClassicMapDefinition.Parse(JsonSerializer.Serialize(map));
    }
    public static int Run(string[] args)
    {
        var watch=Stopwatch.StartNew();steps=0;var total=0;var failed=0;
        void Test(string name,Action action)
        {total++;try{action();Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.Error.WriteLine("FAIL "+name+": "+error.Message);}}
        Test("complete rules fingerprint is cached and nine country variants remain available",()=>
        {
            var first=World.ContentFingerprint();var second=World.ContentFingerprint();
            Require(ReferenceEquals(first,second) && first.Length==64 && first.All(Uri.IsHexDigit),"rules fingerprint was not cached SHA256");
            Require(Enum.GetValues<Country>().Length==9,"nine-country rules contract changed");
            foreach(var country in Enum.GetValues<Country>())
            {
                Require(Enum.GetValues<UnitType>().All(type=>Rules.Unit(type,country).Type==type),"country unit catalog incomplete");
                Require(Enum.GetValues<BuildingType>().All(type=>Rules.Building(type,country).Type==type),"country building catalog incomplete");
            }
        });
        Test("all appended settings survive JSON and affect state identity",()=>
        {
            var settings=MatchSettings.Practice(2) with{StartingCredits=8300,StartingUnits=0,ShortGame=false,MCVRepacks=false,Crates=false,SuperWeapons=false,BuildNearAlly=false};
            var json=JsonSerializer.Serialize(settings,ReplayFile.JsonOptions);
            var copy=JsonSerializer.Deserialize<MatchSettings>(json,ReplayFile.JsonOptions)!;
            Require(copy.StartingCredits==8300 && copy.StartingUnits==0 && copy.ClassicStart && copy.MapId=="" && copy.GameSpeed==0
                && !copy.ShortGame && !copy.MCVRepacks && !copy.Crates && !copy.SuperWeapons && !copy.BuildNearAlly,"new settings reverted to defaults during JSON roundtrip");
            var world=new World(copy);
            Require(CheckScalars(world,copy)>=13,"settings scalar coverage unexpectedly shrank");
            Changed(world,()=>Array.Reverse(copy.Players),()=>Array.Reverse(copy.Players),"Settings.Players order");
            Require(world.StateHash()==new World(settings).StateHash(),"settings roundtrip changed initial state");
        });
        Test("new unit building player projectile and world scalar fields affect checkpoints",()=>
        {
            var world=new World(MatchSettings.LegacyFixture(2));
            Require(CheckScalars(world,world.Units[0])>=50,"unit coverage unexpectedly shrank");
            Require(CheckScalars(world,world.Buildings[0])>=20,"building coverage unexpectedly shrank");
            Require(CheckScalars(world,world.Players[0])>=20,"player coverage unexpectedly shrank");
            Require(CheckScalars(world,world)>=3,"crate scheduler coverage unexpectedly shrank");
            var projectile=new ClassicProjectile{Id=9000,Owner=0,Health=50,Kind="V3ROCKET",Warhead="V3WH"};world.ClassicProjectiles.Add(projectile);
            Require(CheckScalars(world,projectile)>=15,"projectile coverage unexpectedly shrank");
            var effect=new ClassicAreaEffect(1,0,100,200,1024,0,30,25,"Radiation"){NextTick=5};world.ClassicAreaEffects.Add(effect);
            Require(CheckScalars(world,effect)>=10,"area effect coverage unexpectedly shrank");
            var crate=new ClassicCrate(1,10,20,ClassicCrateKind.Credits,0,100,1000);world.ClassicCrates.Add(crate);
            Require(CheckScalars(world,crate)>=7,"crate coverage unexpectedly shrank");
            var allocator=typeof(World).GetField("nextId",BindingFlags.Instance|BindingFlags.NonPublic)!;var original=(int)allocator.GetValue(world)!;
            Changed(world,()=>allocator.SetValue(world,original+1),()=>allocator.SetValue(world,original),"nextId");
        });
        Test("ordered queues effects crates and passenger lists preserve count and order",()=>
        {
            var world=new World(MatchSettings.LegacyFixture(2));var unit=world.Units[0];var building=world.Buildings[0];
            Ordered(world,unit.Path,3,7,"unit path");Ordered(world,unit.Passengers,101,202,"passengers");
            Ordered(world,building.Queue,UnitType.Gi,UnitType.Conscript,"production queue");
            Ordered(world,building.PendingClones,new(UnitType.Gi,Country.America,0),new(UnitType.Conscript,Country.Russia,1),"clone queue");
            Ordered(world,world.ClassicProjectiles,new(){Id=9001,Kind="HORNET"},new(){Id=9002,Kind="V3ROCKET"},"projectiles");
            Ordered(world,world.ClassicAreaEffects,new(1,0,1,2,3,4,5,6,"Radiation"),new(2,1,2,3,4,5,6,7,"Weather"),"area effects");
            Ordered(world,world.ClassicCrates,new(1,1,2,ClassicCrateKind.Credits,0,100,500),new(2,2,3,ClassicCrateKind.Repair,1,101),"crates");
            Changed(world,()=>world.Units.Reverse(),()=>world.Units.Reverse(),"world unit order");
            Changed(world,()=>world.Buildings.Reverse(),()=>world.Buildings.Reverse(),"world building order");
            var visible=world.Players[0].Visibility[0];
            Changed(world,()=>world.Players[0].Visibility[0]=(byte)(visible^1),()=>world.Players[0].Visibility[0]=visible,"visibility bytes");
            var before=world.StateHash();world.ClassicCratePickups.Add(new(0,1,0,unit.Id,ClassicCrateKind.Credits,10));
            Require(world.StateHash()==before,"transient pickup notification entered gameplay state");
        });
        Test("immutable original-map identity includes source and derived passability",()=>
        {
            var settings=MatchSettings.Practice(2) with{MapId="classic-replay-hash-check",Width=32,Height=32,StartingUnits=0};
            ClassicMapRepository.Register(OriginalMap(new string('a',64)));var first=new World(settings);var initial=first.StateHash();
            ClassicMapRepository.Register(OriginalMap(new string('b',64)));var second=new World(settings);
            Require(initial!=second.StateHash(),"source map SHA256 omitted");
            ClassicMapRepository.Register(OriginalMap(new string('b',64),block:true));var blocked=new World(settings);
            Require(second.Map.Ground.SequenceEqual(blocked.Map.Ground) && second.StateHash()!=blocked.StateHash(),"original passability absent from immutable map fingerprint");
            Require(first.StateHash()==initial,"repository registration altered an existing world's immutable map identity");
        });
        Test("thirty-tick deploy repack deploy stream survives JSON replay",()=>
        {
            var settings=MatchSettings.Practice(2) with{StartingUnits=0};var a=new World(settings);var b=new World(settings);var commands=new List<GameCommand>();
            var originalMcv=a.Units.Single(unit=>unit.Owner==0 && unit.Type==UnitType.Mcv);var originalCell=new Cell(originalMcv.CellX,originalMcv.CellY);
            for(var tick=0;tick<ReplayTicks;tick++)
            {
                var batch=new List<GameCommand>();
                if(tick is 0 or 24)
                {
                    var mcv=a.Units.Single(unit=>unit.Alive && unit.Owner==0 && unit.Type==UnitType.Mcv);
                    batch.Add(new(){Tick=tick,Player=0,Sequence=tick+1,Type=CommandType.Deploy,Actors=[mcv.Id]});
                }
                if(tick==22)
                {
                    var yard=a.Buildings.Single(building=>building.Alive && building.Owner==0 && building.Type==BuildingType.ConstructionYard && building.Complete);
                    batch.Add(new(){Tick=tick,Player=0,Sequence=tick+1,Type=CommandType.Deploy,Actors=[yard.Id]});
                }
                // Rejected orders also belong to the canonical replay stream.
                if(tick==12)batch.Add(new(){Tick=tick,Player=0,Sequence=tick+1,Type=CommandType.Move,Actors=null!});
                Require((steps+=2)<=StepCap,"replay check exceeded its 100-step cap");a.Step(batch);b.Step(batch);commands.AddRange(batch);
                Require(a.StateHash()==b.StateHash(),"paired deployment worlds diverged at "+tick);
                if(tick==22){var repacked=a.Units.Single(unit=>unit.Owner==0 && unit.Type==UnitType.Mcv);Require(repacked.CellX==originalCell.X && repacked.CellY==originalCell.Y,"deploy/repack shifted the MCV's original cell");}
            }
            Require(a.AcceptedCommands==3 && a.RejectedCommands==1 && a.Buildings.Any(building=>building.Owner==0 && building.Alive && !building.Complete),"deployment/repack stream did not actually execute");
            var replay=new ReplayFile{Settings=settings,Commands=commands,FinalTick=a.Tick,FinalHash=a.StateHash().ToString("x16")};
            var copy=JsonSerializer.Deserialize<ReplayFile>(JsonSerializer.Serialize(replay,ReplayFile.JsonOptions),ReplayFile.JsonOptions)!;
            Require((steps+=copy.FinalTick)<=StepCap,"replay playback exceeded its 100-step cap");
            Require(copy.Play().StateHash()==a.StateHash(),"serialized replay changed final state");
        });
        Console.WriteLine($"Classic replay: {total-failed}/{total} passed; {steps}/{StepCap} capped world steps; {watch.ElapsedMilliseconds} ms.");
        return failed==0?0:1;
    }
}
