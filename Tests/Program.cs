using System.Diagnostics;
using System.Text.Json;
using RedAlert.Simulation;

if(args.Contains("--classic-map-objects"))return RedAlert.Tests.ClassicMapObjectChecks.Run(args);
if(args.Contains("--classic-mobility"))return RedAlert.Tests.ClassicMobilityChecks.Run(args);
if(args.Contains("--classic-combat"))return RedAlert.Tests.ClassicCombatChecks.Run(args);
if(args.Contains("--classic-economy"))return RedAlert.Tests.ClassicEconomyChecks.Run(args);
if(args.Contains("--classic-replay"))return RedAlert.Tests.ClassicReplayChecks.Run(args);
if(args.Contains("--building-footprints"))return RedAlert.Tests.BuildingFootprintChecks.Run(args);
if(args.Contains("--terrain-surface"))return RedAlert.Tests.TerrainSurfaceChecks.Run(args);
if(args.Contains("--landscape"))return RedAlert.Tests.LandscapeChecks.Run(args);
if(args.Contains("--map-boundary"))return RedAlert.Tests.MapBoundaryChecks.Run(args);
if(args.Contains("--experience"))return RedAlert.Tests.ExperienceSimulationChecks.Run(args);
if(args.Contains("--battle-chat"))return RedAlert.Tests.BattleChatChecks.Run(args);
if(args.Contains("--external-ai"))return RedAlert.Tests.ExternalAiChecks.Run(args);

var results=new List<object>();var benchmarks=new List<object>();var failed=0;
void Check(bool value,string message){if(!value)throw new Exception(message);}
void Test(string name,Action action)
{
    if(args.Contains("--benchmark-only") && name!="large army fixed-step performance")return;
    var watch=Stopwatch.StartNew();
    try{action();results.Add(new{name,passed=true,milliseconds=watch.Elapsed.TotalMilliseconds});Console.WriteLine($"PASS {name} ({watch.ElapsedMilliseconds} ms)");}
    catch(Exception e){failed++;results.Add(new{name,passed=false,error=e.Message});Console.Error.WriteLine($"FAIL {name}: {e}");}
}
World New(int count=2)=>new(MatchSettings.Practice(count));
GameCommand Cmd(World w,int player,CommandType type,int[]? actors=null,int x=0,int y=0,UnitType unit=UnitType.Gi,BuildingType building=BuildingType.PowerPlant,int target=0)=>new(){Tick=w.Tick,Player=player,Sequence=w.Tick*100+player,Type=type,Actors=actors??[],X=x,Y=y,Unit=unit,Building=building,Target=target};
void Run(World w,int ticks){for(var i=0;i<ticks;i++)w.Step();}

Test("2–8 player starts / faction rules / non-overlapping footprints",()=>{
    for(var n=2;n<=8;n++)
    {
        var w=New(n);Check(w.Players.Length==n,"player count");Check(w.Units.Count==n*6,"starting units");Check(w.Buildings.Count==n*4,"starting structures");
        foreach(var p in w.Players){Check(p.Powered,"initial power");Check(w.HasBuilding(p.Id,BuildingType.ConstructionYard),"yard");Check(w.Units.Any(u=>u.Owner==p.Id && u.Type==Rules.Miner(p.Setup.Country)),"country miner");}
        Check(w.Map.Buildings.Count(id=>id!=0)==n*26,"building cells");
    }
});
Test("classic terrain restores transitions without changing deterministic simulation",()=>PresentationAudit.Run(Check));
Test("fixed-point square root matches exact integer bounds",()=>{
    foreach(var n in new long[]{0,1,2,3,4,1024,2097152,1000000000000}){var root=World.IntegerSqrt(n);Check((long)root*root<=n && (long)(root+1)*(root+1)>n,"sqrt "+n);}
});
Test("seed changes map/hash and identical settings reproduce state",()=>{
    var a=New();var b=New();Check(a.StateHash()==b.StateHash(),"initial equality");var c=new World(MatchSettings.Practice(2,7));Check(a.StateHash()!=c.StateHash(),"seed separation");
});
Test("ownership, mixed actor selection, invalid coordinates and enum rejected atomically",()=>{
    var w=New();var mine=w.Units.First(u=>u.Owner==0 && !u.Definition.Miner);var enemy=w.Units.First(u=>u.Owner==1);
    w.Step([Cmd(w,0,CommandType.Move,[mine.Id,enemy.Id],30,30)]);Check(mine.Order==UnitOrder.Idle,"mixed ownership changed my unit");
    w.Step([Cmd(w,0,CommandType.Move,[mine.Id],-1,10000),Cmd(w,0,(CommandType)999)]);
    Check(w.RejectedCommands==3 && w.AcceptedCommands==0,"rejection counts");
});
Test("movement and formation reach distinct destinations without wall traversal",()=>{
    var w=New();var army=w.Units.Where(u=>u.Owner==0 && !u.Definition.Miner).ToArray();
    w.Step([Cmd(w,0,CommandType.Move,army.Select(u=>u.Id).ToArray(),27,22)]);Run(w,350);
    Check(army.Select(u=>(u.CellX,u.CellY)).Distinct().Count()==army.Length,"formation overlap");
    Check(army.All(u=>Math.Abs(u.CellX-27)+Math.Abs(u.CellY-22)<8),"did not reach formation");Check(army.All(u=>w.Map.Walkable(u.CellX,u.CellY)),"occupied obstacle");
});
Test("A* forbids diagonal corner cuts and routes around structures",()=>{
    var w=New();w.Map.Ground[w.Map.Index(30,30)]=Terrain.Grass;w.Map.Ground[w.Map.Index(31,31)]=Terrain.Grass;w.Map.Ground[w.Map.Index(31,30)]=Terrain.Rock;w.Map.Ground[w.Map.Index(30,31)]=Terrain.Rock;
    var path=new GridPathfinder(w.Map).Find(30,30,31,31);Check(path.Count>1,"cut blocked corner");
    var b=w.Buildings[0];var p=new GridPathfinder(w.Map).Find(b.CellLeft-1,b.CellTop,b.CellLeft+4,b.CellTop);Check(p.Count>0 && p.All(i=>w.Map.Buildings[i]==0),"walked through building");
});
Test("construction charges once, blocks overlap and completes",()=>{
    var w=New();var initial=w.Players[0].Credits;
    w.Step([Cmd(w,0,CommandType.Build,x:17,y:9,building:BuildingType.WarFactory)]);
    var b=w.Buildings.Single(b=>b.Owner==0 && b.Type==BuildingType.WarFactory);Check(!b.Complete,"instant construction");Check(w.Players[0].Credits==initial-2000,"cost");
    w.Step([Cmd(w,0,CommandType.Build,x:17,y:9,building:BuildingType.WarFactory)]);Check(w.RejectedCommands==1,"overlap allowed");Run(w,220);Check(b.Complete,"not completed");Check(b.Health==b.MaxHealth,"undamaged building completed below full health");
});
Test("production / cancellation refunds / opposing country units rejected",()=>{
    var w=New();var barracks=w.Buildings.First(b=>b.Owner==0 && b.Type==BuildingType.Barracks);var credits=w.Players[0].Credits;
    w.Step([Cmd(w,0,CommandType.Train,[barracks.Id],unit:UnitType.Gi)]);Check(barracks.Queue.Count==1,"not queued");
    w.Step([Cmd(w,0,CommandType.CancelQueue,[barracks.Id])]);Check(w.Players[0].Credits==credits && barracks.Queue.Count==0,"refund");
    w.Step([Cmd(w,0,CommandType.Train,[barracks.Id],unit:UnitType.Conscript),Cmd(w,0,CommandType.Train,[barracks.Id],unit:UnitType.Sniper)]);Check(w.RejectedCommands==2,"foreign unit allowed");
    var before=w.Units.Count;w.Step([Cmd(w,0,CommandType.Train,[barracks.Id],unit:UnitType.Gi)]);Run(w,65);Check(w.Units.Count==before+1,"production incomplete");
});
Test("mining conserves ore, cargo and credits including chrono returns",()=>{
    var w=New(8);var ore=w.Map.Ore.Sum();var credits=w.Players.Sum(p=>p.Credits);Run(w,1000);
    var mined=ore-w.Map.Ore.Sum();var earned=w.Players.Sum(p=>p.Credits)-credits;
    Check(earned>0,"no ore delivered");Check(mined==earned+w.Units.Sum(u=>u.Cargo),"mineral duplication/loss");Check(w.Map.Ore.All(o=>o>=0),"negative ore");
});
Test("combat kills visible enemy and refuses friendly fire",()=>{
    var w=New();var tank=w.SpawnUnit(0,UnitType.Grizzly,30,20);var target=w.SpawnUnit(1,UnitType.Conscript,32,20);Run(w,7);
    var accepted=w.AcceptedCommands;w.Step([Cmd(w,0,CommandType.Attack,[tank.Id],target.CellX,target.CellY,target:target.Id)]);Check(w.AcceptedCommands==accepted+1,"attack rejected");Run(w,160);Check(w.Find(target.Id)==null,"enemy survived");
    var friend=w.SpawnUnit(0,UnitType.Gi,31,20);Run(w,5);var rejected=w.RejectedCommands;w.Step([Cmd(w,0,CommandType.Attack,[tank.Id],friend.CellX,friend.CellY,target:friend.Id)]);Check(w.RejectedCommands==rejected+1,"friendly fire accepted");
});
Test("engineer captures structure and is consumed",()=>{
    var w=New();var e=w.SpawnUnit(0,UnitType.Engineer,74,80);Run(w,7);var target=w.Buildings.First(b=>b.Owner==1 && b.Type==BuildingType.PowerPlant);
    w.Step([Cmd(w,0,CommandType.Attack,[e.Id],target.CellX,target.CellY,target:target.Id)]);Run(w,150);Check(target.Owner==0 && w.Find(e.Id)==null,"capture failed");
});
Test("American airborne delivers six GI with cooldown and country restriction",()=>{
    var w=New(4);Run(w,200);var count=w.Units.Count(u=>u.Owner==0 && u.Type==UnitType.Gi);
    w.Step([Cmd(w,0,CommandType.Support,x:17,y:12)]);Check(w.Units.Count(u=>u.Owner==0 && u.Type==UnitType.Gi)==count+6,"airborne count");
    var rejected=w.RejectedCommands;w.Step([Cmd(w,0,CommandType.Support,x:17,y:12),Cmd(w,1,CommandType.Support,x:80,y:77)]);Check(w.RejectedCommands==rejected+2,"cooldown/country restriction");
});
Test("country specific sniper, Tesla tank and grand cannon unlocks",()=>{
    var w=New(4);Check(Rules.Allowed(Rules.Unit(UnitType.Sniper),Country.Britain) && !Rules.Allowed(Rules.Unit(UnitType.Sniper),Country.America),"sniper rules");
    var b=w.Buildings.First(b=>b.Owner==3 && b.Type==BuildingType.Barracks);w.Step([Cmd(w,3,CommandType.Train,[b.Id],unit:UnitType.Sniper)]);Check(b.Queue.Count==1,"sniper queue");
    var s=w.Map.Spawns[1];var factory=w.SpawnBuilding(1,BuildingType.WarFactory,s.X+4,s.Y-4);w.Step([Cmd(w,1,CommandType.Train,[factory.Id],unit:UnitType.TeslaTank)]);Check(factory.Queue.Count==1,"Tesla queue");
    s=w.Map.Spawns[2];w.SpawnBuilding(2,BuildingType.WarFactory,s.X+4,s.Y-4);w.Step([Cmd(w,2,CommandType.Build,x:s.X+4,y:s.Y-1,building:BuildingType.GrandCannon)]);Check(w.Buildings.Any(b=>b.Owner==2 && b.Type==BuildingType.GrandCannon),"grand cannon");
});
Test("low power slows production and repair consumes credits",()=>{
    var w=New();w.Buildings.First(b=>b.Owner==0 && b.Type==BuildingType.PowerPlant).Health=0;Run(w,2);Check(!w.Players[0].Powered,"power shortage");
    var b=w.Buildings.First(b=>b.Owner==0 && b.Type==BuildingType.Barracks);var count=w.Units.Count;w.Step([Cmd(w,0,CommandType.Train,[b.Id],unit:UnitType.Gi)]);Run(w,65);Check(w.Units.Count==count,"power shortage did not slow production");Run(w,65);Check(w.Units.Count==count+1,"low-power production stuck");
    b.Health-=100;var health=b.Health;w.Step([Cmd(w,0,CommandType.Repair,[b.Id])]);Run(w,15);Check(b.Health>health,"repair failed");
});
Test("fog shares allied vision but keeps opponents hidden",()=>{
    var settings=new MatchSettings(10,[new("A",Country.America,0),new("B",Country.Britain,0),new("C",Country.Russia,1)]);var w=new World(settings);
    var s=w.Map.Spawns[1];Check(w.Visible(0,s.X,s.Y),"allied vision missing");s=w.Map.Spawns[2];Check(!w.Visible(0,s.X,s.Y),"enemy base visible");Check(!w.Explored(0,47,47),"unexplored center");
});
Test("surrender produces victory and removes defeated actors",()=>{
    var w=New();Run(w,22);w.Step([Cmd(w,1,CommandType.Surrender)]);Check(w.Finished && w.WinningTeam==0,"victory failed");Check(!w.Units.Any(u=>u.Owner==1) && !w.Buildings.Any(b=>b.Owner==1),"defeated actors remain");
});
Test("in-flight group order ignores dead actors while commanding survivors",()=>{
    var w=New();var tanks=w.Units.Where(u=>u.Owner==0 && u.Type==UnitType.Grizzly).ToArray();tanks[1].Health=0;
    w.Step([Cmd(w,0,CommandType.Move,[tanks[0].Id,tanks[1].Id],27,22)]);
    Check(w.AcceptedCommands==1 && tanks[0].Order==UnitOrder.Move,"surviving squad lost its order");
});
Test("dogs cannot manually attack vehicles but can attack hostile infantry",()=>{
    var w=New();var dog=w.SpawnUnit(0,UnitType.Dog,30,20);var tank=w.SpawnUnit(1,UnitType.Rhino,32,20);tank.Reload=100000;Run(w,6);
    w.Step([Cmd(w,0,CommandType.Attack,[dog.Id],tank.CellX,tank.CellY,target:tank.Id)]);Check(w.RejectedCommands==1,"dog attacked armor");
    var infantry=w.SpawnUnit(1,UnitType.Conscript,32,21);infantry.Reload=100000;Run(w,5);
    w.Step([Cmd(w,0,CommandType.Attack,[dog.Id],infantry.CellX,infantry.CellY,target:infantry.Id)]);Run(w,100);
    Check(w.Find(infantry.Id)==null && tank.Health==tank.MaxHealth,"infantry target or armor immunity wrong");
});
Test("capturing a factory refunds its old queue to the previous owner",()=>{
    var w=New();var barracks=w.SpawnBuilding(1,BuildingType.Barracks,70,80);
    w.Step([Cmd(w,1,CommandType.Train,[barracks.Id],unit:UnitType.Conscript)]);
    var engineer=w.SpawnUnit(0,UnitType.Engineer,69,80);Run(w,5);var oldCredits=w.Players[1].Credits;var newCredits=w.Players[0].Credits;
    w.Step([Cmd(w,0,CommandType.Attack,[engineer.Id],barracks.CellX,barracks.CellY,target:barracks.Id)]);
    Check(barracks.Owner==0 && barracks.Queue.Count==0 && w.Players[0].Credits==newCredits && w.Players[1].Credits==oldCredits+Rules.Unit(UnitType.Conscript).Cost,"capture transferred queue money to wrong owner");
});
Test("losing the last structure removes defeated units in the victory frame",()=>{
    var w=New();Run(w,25);foreach(var b in w.Buildings.Where(b=>b.Owner==1))b.Health=0;w.Step();
    Check(w.Finished && w.WinningTeam==0 && !w.Units.Any(u=>u.Owner==1) && !w.Buildings.Any(b=>b.Owner==1),"victory left defeated actors");
});
Test("maximum eight player boundary rejects nine-player settings",()=>{
    var rejected=false;try{_ =new World(new MatchSettings(1,Enumerable.Range(0,9).Select(i=>new PlayerSetup("P"+i,(Country)(i%4),i%2)).ToArray()));}catch(ArgumentException){rejected=true;}
    Check(rejected,"nine players were accepted");
});
Test("construction reaches full health while preserving combat damage",()=>{
    var w=New();w.Step([Cmd(w,0,CommandType.Build,x:17,y:9,building:BuildingType.PowerPlant)]);var building=w.Buildings.Last();Run(w,20);building.Health-=100;Run(w,150);
    Check(building.Complete && building.Health==building.MaxHealth-100,"construction erased damage or lost rounding remainder");
});
Test("unreachable early units cannot starve a later reachable unit",()=>{
    var w=New();
    for(var y=24;y<=31;y++)for(var x=24;x<=35;x++)w.Map.Ground[w.Map.Index(x,y)]=x==24 || x==35 || y==24 || y==31?Terrain.Rock:Terrain.Grass;
    var blocked=new List<Unit>();for(var y=25;y<=30;y++)for(var x=25;x<=34;x++)blocked.Add(w.SpawnUnit(0,UnitType.Gi,x,y));
    for(var y=19;y<=21;y++)for(var x=38;x<=44;x++)w.Map.Ground[w.Map.Index(x,y)]=Terrain.Grass;
    var free=w.SpawnUnit(0,UnitType.Grizzly,38,20);var orders=blocked.Select((u,i)=>new GameCommand{Tick=w.Tick,Player=0,Sequence=i+1,Type=CommandType.Move,Actors=[u.Id],X=43,Y=32}).ToList();orders.Add(Cmd(w,0,CommandType.Move,[free.Id],43,20));
    w.Step(orders);Run(w,60);
    Check(free.CellX==43 && free.CellY==20,"failed searches monopolized global path budget");Check(blocked.All(u=>u.CellX>=25 && u.CellX<=34 && u.CellY>=25 && u.CellY<=30),"sealed units crossed blockers");
});
Test("eight-player deterministic command stream and serialized replay",()=>{
    var settings=MatchSettings.Practice(8);var a=new World(settings);var b=new World(settings);var stream=new List<GameCommand>();
    for(var tick=0;tick<2400;tick++)
    {
        var commands=Enumerable.Range(0,8).SelectMany(p=>PracticeCommander.Orders(a,p)).ToArray();stream.AddRange(commands);
        a.Step(commands);b.Step(commands.Reverse());if(tick%40==0)Check(a.StateHash()==b.StateHash(),"desync tick "+tick);
    }
    var replay=new ReplayFile{Settings=settings,Commands=stream,FinalTick=a.Tick,FinalHash=a.StateHash().ToString("x16")};
    var json=JsonSerializer.Serialize(replay,ReplayFile.JsonOptions);var loaded=JsonSerializer.Deserialize<ReplayFile>(json,ReplayFile.JsonOptions)!;
    var played=loaded.Play();Check(played.StateHash()==a.StateHash(),"replay mismatch");Check(a.AcceptedCommands>100,"scenario exercised too few commands");
});

var stressArg=args.FirstOrDefault(a=>a.StartsWith("--stress="));
if(stressArg is not null)Test("large army fixed-step performance",()=>{
    var count=int.Parse(stressArg.Split('=')[1]);var w=New(8);
    for(var y=3;y<93 && w.Units.Count<count;y++)for(var x=3;x<93 && w.Units.Count<count;x++)
        if(w.Map.Walkable(x,y) && !w.Units.Any(u=>u.CellX==x && u.CellY==y))w.SpawnUnit((x+y)%8,Rules.BasicTank((Country)((x+y)%4)),x,y);
    foreach(var u in w.Units)u.Reload=1000000;
    var samples=new List<double>();var total=Stopwatch.StartNew();
    for(var tick=0;tick<600;tick++)
    {
        var commands=tick==0?Enumerable.Range(0,8).Select(p=>Cmd(w,p,CommandType.Move,w.Units.Where(u=>u.Owner==p).Select(u=>u.Id).ToArray(),45+(p%3)*5,45+(p/3)*5)).ToArray():null;
        var sw=Stopwatch.StartNew();w.Step(commands);samples.Add(sw.Elapsed.TotalMilliseconds);
    }
    samples.Sort();var metrics=new{units=w.Units.Count,ticks=w.Tick,p50Ms=samples[samples.Count/2],p95Ms=samples[(int)(samples.Count*.95)],budgetMs=1000.0/Rules.TicksPerSecond,p95WithinBudget=samples[(int)(samples.Count*.95)]<=1000.0/Rules.TicksPerSecond,maxMs=samples[^1],elapsedMs=total.Elapsed.TotalMilliseconds,pathSearches=w.PathSearches};
    benchmarks.Add(metrics);Console.WriteLine(JsonSerializer.Serialize(metrics));Check(w.Units.Count==count,"stress unit count");
});
var reportArg=args.FirstOrDefault(a=>a.StartsWith("--report="));
if(reportArg is not null){var path=Path.GetFullPath(reportArg[9..]);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,JsonSerializer.Serialize(new{passed=failed==0,totalTests=results.Count,passedTests=results.Count-failed,failed,content=World.ContentFingerprint(),tests=results,benchmarks},new JsonSerializerOptions{WriteIndented=true}));}
Console.WriteLine($"{results.Count-failed} passed; {failed} failed");return failed==0?0:1;
