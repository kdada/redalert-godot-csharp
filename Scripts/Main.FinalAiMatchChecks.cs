using Godot;
using RedAlert.Simulation;
using System.Diagnostics;

namespace RedAlert;

public partial class Main
{
    Godot.Collections.Dictionary finalAiMatchReport=new();
    sealed class FinalAiNaturalEnd : Exception { }
    public Godot.Collections.Dictionary GetFinalAiMatchAudit()=>finalAiMatchReport;

    // A human-side operator, not a second simulation/AI implementation. Every
    // purchase uses the real sidebar and every army order goes through Issue.
    // Main._Process alone steps the world and issues the opponent's PracticeOrders.
    public async void RunFinalAiMatch(int nation=0)
    {
        if(fullPlaythroughReport.TryGetValue("running",out var running) && running.AsBool())return;
        const int tickCap=60000;const int secondCap=900;
        var folder=ProjectSettings.GlobalizePath("res://Reports/final-ai-match-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(folder);
        var stages=new Godot.Collections.Array<Godot.Collections.Dictionary>();
        finalAiMatchReport=new(){{"running",true},{"passed",false},{"completed",false},{"phase","preparing"},
            {"folder",folder},{"stages",stages},{"tickCap",tickCap},{"realSecondCap",secondCap},{"rulesRevision",Rules.Revision},
            {"directEntitySpawns",0},{"directEnemyOrders",0},{"simulationStateWrites",0},{"ordinaryPracticeAi",true},{"aiDifficulty","normal"}};
        // Reuse the existing per-tick event hook, including frames that run several ticks.
        fullPlaythroughReport=finalAiMatchReport;fullPlaythroughDamage.Clear();
        var savedSpeed=clientPreferences.GameSpeed;var savedScale=Engine.TimeScale;var savedFast=fast;
        var savedEdges=clientPreferences.EdgeScrolling;var savedDifficulties=practiceDifficulties.ToArray();
        var clock=Stopwatch.StartNew();World? world=null;var error="";var replayIndex=0;var damageIndex=0;
        var owners=new Dictionary<int,int>();var liveLastFrame=new HashSet<int>();
        var priorCargo=new Dictionary<int,int>();var seenCombat=new[]{new HashSet<int>(),new HashSet<int>()};
        var seenBuildings=new[]{new HashSet<int>(),new HashSet<int>()};var commandCounts=new[]{new Dictionary<string,int>(),new Dictionary<string,int>()};
        var unloads=new int[2];var unloadedMinimum=new long[2];var damage=new long[2];var hits=new int[2];var kills=new int[2];
        var removedUnits=new int[2];var removedBuildings=new int[2];var peakArmy=new int[2];var entityIsBuilding=new Dictionary<int,bool>();
        var entityIsMcv=new HashSet<int>();var nextDecision=0;var nextCapture=4000;var gotBattleCapture=false;var gotDevelopmentCapture=false;var gotEnemyBaseCapture=false;var placementRetries=0;
        var country=(Country)Math.Clamp(nation,0,8);var allied=Rules.Side(country)==Faction.Allied;
        var enemy=allied?Country.Russia:Country.America;Cell home=default;Cell enemyStart=default;
        void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        void Observe()
        {
            if(world is null)return;
            Require(ReferenceEquals(world,Battle),"The ordinary match was replaced while the operator was running");
            var live=new HashSet<int>();
            foreach(var building in world.Buildings.Where(b=>b.Alive))
            {
                live.Add(building.Id);owners[building.Id]=building.Owner;entityIsBuilding[building.Id]=true;
                if((uint)building.Owner<2)seenBuildings[building.Owner].Add(building.Id);
            }
            foreach(var unit in world.Units.Where(u=>u.Alive))
            {
                live.Add(unit.Id);owners[unit.Id]=unit.Owner;entityIsBuilding[unit.Id]=false;
                if(unit.Type==UnitType.Mcv)entityIsMcv.Add(unit.Id);
                if((uint)unit.Owner>=2)continue;
                if(!unit.Definition.Miner && unit.Type!=UnitType.Mcv)seenCombat[unit.Owner].Add(unit.Id);
                if(unit.Definition.Miner)
                {
                    if(priorCargo.TryGetValue(unit.Id,out var cargo) && cargo>0 && unit.Cargo==0 && unit.Order==UnitOrder.Harvest)
                    {unloads[unit.Owner]++;unloadedMinimum[unit.Owner]+=cargo;}
                    priorCargo[unit.Id]=unit.Cargo;
                }
            }
            foreach(var id in liveLastFrame.Where(id=>!live.Contains(id) && !entityIsMcv.Contains(id)))
                if(owners.TryGetValue(id,out var owner) && (uint)owner<2)
                {if(entityIsBuilding.GetValueOrDefault(id))removedBuildings[owner]++;else removedUnits[owner]++;}
            liveLastFrame=live;
            while(replayIndex<localReplay.Count)
            {
                var command=localReplay[replayIndex++];if((uint)command.Player>=2)continue;
                var counts=commandCounts[command.Player];var key=command.Type.ToString();counts[key]=counts.GetValueOrDefault(key)+1;
            }
            while(damageIndex<fullPlaythroughDamage.Count)
            {
                var hit=fullPlaythroughDamage[damageIndex++];var owner=hit.SourceOwner;
                if(owner<0)owner=owners.GetValueOrDefault(hit.Source,-1);
                if((uint)owner>=2 || !owners.TryGetValue(hit.Target,out var victim) || victim==owner)continue;
                hits[owner]++;damage[owner]+=hit.Damage;if(hit.Explosion)kills[owner]++;
            }
            for(var p=0;p<2;p++)peakArmy[p]=Math.Max(peakArmy[p],world.Units.Count(u=>u.Alive && u.Owner==p && !u.Definition.Miner && u.Type!=UnitType.Mcv));
            finalAiMatchReport["tick"]=world.Tick;finalAiMatchReport["elapsedSeconds"]=clock.Elapsed.TotalSeconds;
            finalAiMatchReport["acceptedCommands"]=world.AcceptedCommands;finalAiMatchReport["rejectedCommands"]=world.RejectedCommands;
        }
        void Guard(bool naturalEnd=true)
        {
            if(failure.Length>0)throw new InvalidOperationException("Main failure: "+failure);
            if(clock.Elapsed.TotalSeconds>secondCap || world?.Tick>=tickCap)throw new TimeoutException("Final AI match reached its bounded budget at tick "+world?.Tick);
            if(naturalEnd && world?.Finished==true)throw new FinalAiNaturalEnd();
        }
        async Task Frame(){await PointerParityFrame();Observe();Guard();}
        async Task Wait(Func<bool> condition,string phase,int ticks=8000)
        {
            finalAiMatchReport["phase"]=phase;var start=world?.Tick??0;var timer=Stopwatch.StartNew();
            while(!condition())
            {
                Guard();if(timer.Elapsed.TotalSeconds>150 || world is not null && world.Tick-start>ticks)throw new TimeoutException(phase+" at tick "+world?.Tick);
                await Frame();
            }
        }
        void Focus(Cell cell)
        {
            if(battlefield is null || world is null)return;
            var position=new Vector2(cell.X+.5f,cell.Y+.5f);var rise=(float)world.Map.SurfaceHeightAt(position.X,position.Y)*.5f;
            battlefield.SetCameraCell(position-Vector2.One*rise);
        }
        void Pointer(Vector2 point)
        {
            if(softwareBattlePointer)softwarePointer=ClampBattlePointer(point);
            forwardingBattlePointer=true;
            try{using var motion=new InputEventMouseMotion{Position=point,GlobalPosition=point};GetViewport().PushInput(motion,true);}
            finally{forwardingBattlePointer=false;}
        }
        void Click(Vector2 point)
        {
            Pointer(point);forwardingBattlePointer=true;
            try
            {
                using var down=new InputEventMouseButton{Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left,Pressed=true};
                using var up=new InputEventMouseButton{Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left};
                GetViewport().PushInput(down,true);GetViewport().PushInput(up,true);
            }
            finally{forwardingBattlePointer=false;}
        }
        void Snapshot()
        {
            if(world is null || !ReferenceEquals(world,Battle))return;Observe();
            var players=new Godot.Collections.Array<Godot.Collections.Dictionary>();
            for(var p=0;p<2;p++)
            {
                var commands=new Godot.Collections.Dictionary();foreach(var entry in commandCounts[p])commands[entry.Key]=entry.Value;
                players.Add(new(){{"player",p},{"country",world.Players[p].Setup.Country.ToString()},{"credits",world.Players[p].Credits},
                    {"defeated",world.Players[p].Defeated},{"powerGenerated",world.Players[p].GeneratedPower},{"powerUsed",world.Players[p].UsedPower},
                    {"observedMinerUnloads",unloads[p]},{"observedUnloadedCargoMinimum",unloadedMinimum[p]},
                    {"combatUnitsSeen",seenCombat[p].Count},{"buildingsSeen",seenBuildings[p].Count},{"peakArmy",peakArmy[p]},
                    {"hitsOnOpponent",hits[p]},{"damageToOpponent",damage[p]},{"combatKills",kills[p]},
                    {"removedUnits",removedUnits[p]},{"removedBuildings",removedBuildings[p]},{"submittedCommandTypes",commands}});
            }
            finalAiMatchReport["players"]=players;finalAiMatchReport["winnerTeam"]=world.WinningTeam;
            finalAiMatchReport["finished"]=world.Finished;finalAiMatchReport["worldHash"]=world.StateHash().ToString("x16");
            var blocked=new Godot.Collections.Array<Godot.Collections.Dictionary>();
            foreach(var factory in world.Buildings.Where(b=>b.Alive && b.Queue.Count>0))
            {
                var definition=Rules.Unit(factory.Queue[0],factory.FoundationCountry);if(definition.Domain==MovementDomain.Air)continue;
                var exit=world.ClassicProductionExit(factory,definition);
                if(exit.Cell is null)blocked.Add(new(){{"id",factory.Id},{"owner",factory.Owner},{"type",factory.Type.ToString()},
                    {"head",definition.Type.ToString()},{"progress",factory.ProductionProgress},{"paid",factory.ProductionPaid},{"block",exit.Block.ToString()}});
            }
            finalAiMatchReport["blockedProducers"]=blocked;
        }
        void Save(){Snapshot();File.WriteAllText(Path.Combine(folder,"report.json"),Godot.Json.Stringify(finalAiMatchReport,"\t"));}
        async Task Capture(string label,Cell? cell=null)
        {
            finalAiMatchReport["phase"]=label;if(cell is {} focus)Focus(focus);
            if(battlefield is not null)Pointer(battlefield.GetGlobalRect().GetCenter());
            await PointerParityFrame();await PointerParityAfterDraw();Observe();
            using var image=GetViewport().GetTexture().GetImage();var path=Path.Combine(folder,$"{stages.Count:00}-{label}.png");
            Require(image.SavePng(path)==Error.Ok,"GPU capture failed");
            stages.Add(new(){{"name",label},{"tick",world?.Tick??0},{"png",path},{"finished",world?.Finished??false}});Save();
        }
        async Task<bool> Product(UnitType? unit,BuildingType? building)
        {
            Guard();productTab=unit is {} type?(Rules.Production(type)==ProductionCategory.Infantry?"infantry":"vehicle"):"build";
            lastProductKey="";RefreshClassicProducts();var index=productButtons.FindIndex(p=>p.Unit==unit && p.Building==building);
            if(index<0)return false;
            productPage=index/Math.Max(1,productPageCapacity);RefreshProductPage();await Frame();
            var button=productButtons[index].Button;if(!button.Visible || button.Disabled)return false;
            Click(button.GetGlobalRect().GetCenter());return true;
        }
        Cell? Site(BuildingType type)
        {
            var anchors=world!.Buildings.Where(b=>b.Alive && b.Complete && b.Owner==0).ToArray();var definition=Rules.Building(type,country);
            bool Clear(Cell cell)
            {
                var footprint=new Rect2I(cell.X,cell.Y,definition.Width,definition.Height);
                foreach(var producer in anchors.Where(b=>b.Definition.Produces is ProductionCategory.Infantry or ProductionCategory.Vehicle or ProductionCategory.Naval || b.Type==BuildingType.Refinery))
                {
                    if(world.ClassicProductionExitPortals(producer).Any(p=>footprint.HasPoint(new(p.X,p.Y))))return false;
                    if(producer.Type==BuildingType.Refinery && footprint.HasPoint(new(producer.CellLeft+producer.Definition.Width+1,producer.CellTop+producer.Definition.Height/2)))return false;
                }
                if(definition.Produces is not (ProductionCategory.Infantry or ProductionCategory.Vehicle or ProductionCategory.Naval) && type!=BuildingType.Refinery)return true;
                var planned=new Building{Type=type,FoundationCountry=country,Owner=0,CellLeft=cell.X,CellTop=cell.Y,
                    X=cell.X*Rules.Scale+definition.Width*Rules.Scale/2,Y=cell.Y*Rules.Scale+definition.Height*Rules.Scale/2};
                return world.ClassicProductionExitPortals(planned).Any(p=>world.Map.Navigable(p.X,p.Y,definition.WaterBound?MovementDomain.Water:MovementDomain.Land));
            }
            return anchors.SelectMany(anchor=>Enumerable.Range(-10,21).SelectMany(dy=>Enumerable.Range(-10,21).Select(dx=>new Cell(anchor.CellX+dx,anchor.CellY+dy))))
                .Distinct().Where(cell=>world.CanPlaceBuilding(0,type,cell.X,cell.Y) && Clear(cell))
                .OrderBy(cell=>anchors.Min(anchor=>Math.Abs(anchor.CellX-cell.X)+Math.Abs(anchor.CellY-cell.Y))).ThenBy(cell=>cell.Y).ThenBy(cell=>cell.X).Cast<Cell?>().FirstOrDefault();
        }
        async Task Construct()
        {
            var player=world!.Players[0];
            if(player.BuildingProduction is {} active)
            {
                if(!player.BuildingReady)return;
                // Miners and troops can enter a valid footprint between the UI
                // frames. Retry only through the real viewport, with fresh sites.
                for(var attempt=0;attempt<4 && player.BuildingProduction==active && player.BuildingReady;attempt++)
                {
                    if(Site(active) is not {} site)return;
                    if(battlefield!.Placement!=active && !await Product(null,active))return;
                    Require(battlefield.Placement==active,"Ready sidebar building did not enter ordinary placement");
                    Focus(site);await Frame();
                    if(!world.CanPlaceBuilding(0,active,site.X,site.Y)){placementRetries++;continue;}
                    Click(battlefield.GlobalPosition+battlefield.ProjectGround(new Vector2(site.X+.25f,site.Y+.25f)));
                    for(var frame=0;frame<3 && player.BuildingProduction==active && player.BuildingReady;frame++)await Frame();
                    if(player.BuildingProduction==active && player.BuildingReady)placementRetries++;
                }
                finalAiMatchReport["placementRetries"]=placementRetries;return;
            }
            int Count(BuildingType type)=>world.Buildings.Count(b=>b.Alive && b.Owner==0 && b.Type==type);
            BuildingType? desired=Count(BuildingType.PowerPlant)==0?BuildingType.PowerPlant
                :Count(BuildingType.Refinery)==0?BuildingType.Refinery
                :Count(BuildingType.Barracks)==0?BuildingType.Barracks
                :Count(BuildingType.WarFactory)==0?BuildingType.WarFactory
                :Count(BuildingType.Refinery)<2?BuildingType.Refinery
                :Count(BuildingType.Radar)==0?BuildingType.Radar
                :Count(BuildingType.BattleLab)==0?BuildingType.BattleLab
                :Count(BuildingType.WarFactory)<2?BuildingType.WarFactory
                :Count(BuildingType.Refinery)<3 && unloads[0]>=4?BuildingType.Refinery
                :allied && Count(BuildingType.OrePurifier)==0?BuildingType.OrePurifier:null;
            var future=desired is {} next?Math.Max(0,-Rules.Building(next,country).Power):40;
            if(player.GeneratedPower<player.UsedPower+future+15)
                desired=!allied && Count(BuildingType.BattleLab)>0 && world.CanProduceBuilding(0,BuildingType.NuclearReactor)?BuildingType.NuclearReactor:BuildingType.PowerPlant;
            if(desired is {} building && world.CanQueueBuilding(0,building))await Product(null,building);
        }
        async Task Reinforce()
        {
            var player=world!.Players[0];var owned=world.Units.Where(u=>u.Alive && u.Owner==0).ToArray();
            var factories=world.Buildings.Where(b=>b.Alive && b.Complete && b.Owner==0).ToArray();
            var reserve=player.BuildingProduction is {} active?Math.Min(1600,Math.Max(0,Rules.Building(active,country).Cost-player.BuildingProductionPaid)+200):400;
            if(player.Credits<reserve)return;
            var miner=Rules.Miner(country);var miners=owned.Count(u=>u.Definition.Miner)+factories.Sum(b=>b.Queue.Count(t=>t==miner));
            var desiredMiners=Math.Min(6,Math.Max(2,factories.Count(b=>b.Type==BuildingType.Refinery)*2));
            var vehicle=factories.FirstOrDefault(b=>b.Type==BuildingType.WarFactory && b.Queue.Count<2);
            if(vehicle is not null)
            {
                var aa=allied?UnitType.Ifv:UnitType.FlakTrack;var basic=Rules.BasicTank(country);
                var siege=allied?UnitType.PrismTank:UnitType.V3Launcher;var count=owned.Count(u=>!u.Definition.Miner && u.Type!=UnitType.Mcv);
                var type=miners<desiredMiners?miner:owned.Count(u=>u.Type==aa)<3?aa
                    :world.CanTrainUnit(0,siege,vehicle) && (owned.Count(u=>u.Type==siege)<8 || count%3==0)?siege:basic;
                if((type==miner || count<48) && world.CanTrainUnit(0,type,vehicle))await Product(type,null);
            }
            var barracks=factories.FirstOrDefault(b=>b.Type==BuildingType.Barracks && b.Queue.Count<2);
            if(barracks is not null && player.Credits>=reserve+200 && owned.Count(u=>u.Definition.Infantry)<12)
            {
                var infantry=Rules.BasicInfantry(country);if(world.CanTrainUnit(0,infantry,barracks))await Product(infantry,null);
            }
        }
        Cell? Advance(Unit unit,Cell destination)
        {
            var start=new Cell(unit.CellX,unit.CellY);if(!world!.Map.Navigable(start.X,start.Y,unit.Definition.Domain,unit.Definition.Amphibious))return null;
            var queue=new Queue<Cell>();var visited=new HashSet<Cell>{start};queue.Enqueue(start);var best=start;
            long Distance(Cell cell)=>(long)(cell.X-destination.X)*(cell.X-destination.X)+(long)(cell.Y-destination.Y)*(cell.Y-destination.Y);
            var score=Distance(start);
            while(queue.Count>0 && visited.Count<=4096)
            {
                var cell=queue.Dequeue();var distance=Distance(cell);if(distance<score){best=cell;score=distance;if(score==0)break;}
                foreach(var next in new[]{new Cell(cell.X+1,cell.Y),new Cell(cell.X,cell.Y+1),new Cell(cell.X-1,cell.Y),new Cell(cell.X,cell.Y-1)})
                    if(!visited.Contains(next) && world.Map.CanTraverseMovement(cell,next,unit.Definition.Domain,unit.Definition.Amphibious)){visited.Add(next);queue.Enqueue(next);}
            }
            return best==start?null:best;
        }
        void Fight()
        {
            var army=world!.Units.Where(u=>u.Alive && u.Owner==0 && !u.Definition.Miner && u.Type is not (UnitType.Mcv or UnitType.Engineer)
                && u.TransportId==0 && u.ParasiteHostId==0 && u.Definition.PrimaryWeapon is not null).ToArray();
            if(army.Length==0)return;
            var enemies=world.Units.Where(u=>u.Alive && u.Owner==1 && world.ClassicUnitVisibleTo(0,u)).Cast<Entity>()
                .Concat(world.Buildings.Where(b=>b.Alive && b.Owner==1 && world.Visible(0,b.CellX,b.CellY))).ToArray();
            var target=enemies.Where(e=>army.Any(u=>world.CanTarget(u,e)))
                .OrderBy(e=>e is Unit && Math.Abs(e.CellX-home.X)+Math.Abs(e.CellY-home.Y)<22?0:e is Unit?1:2)
                .ThenBy(e=>army.Min(u=>World.DistanceSquared(u,e))).ThenBy(e=>e.Id).FirstOrDefault();
            var attackers=target is null?Array.Empty<Unit>():army.Where(u=>!u.Deployed && world.CanTarget(u,target) && (u.TargetId!=target.Id || u.Order!=UnitOrder.Attack)).Take(40).ToArray();
            if(target is not null && attackers.Length>0)
            {
                battlefield!.Selection.Clear();foreach(var unit in attackers)battlefield.Selection.Add(unit.Id);
                Issue(new(){Type=CommandType.Attack,Actors=attackers.Select(u=>u.Id).ToArray(),Target=target.Id});
            }
            var mobile=army.Where(u=>!u.Deployed && (u.Order==UnitOrder.Idle || u.BlockedTicks>=80)).OrderBy(u=>u.Id).ToArray();
            if(mobile.Length==0 || army.Length<6 && world.Tick<3000)return;
            var destination=enemyStart;
            if(world.Tick>12000)
            {
                (int X,int Y)[] patrol=[(0,0),(12,0),(0,12),(-12,0),(0,-12),(20,12),(-20,-12)];
                var offset=patrol[(world.Tick/1600)%patrol.Length];destination=new(enemyStart.X+offset.X,enemyStart.Y+offset.Y);
            }
            if(Advance(mobile[0],destination) is not {} goal)return;
            battlefield!.Selection.Clear();foreach(var unit in mobile.Take(40))battlefield.Selection.Add(unit.Id);
            Issue(new(){Type=CommandType.AttackMove,Actors=mobile.Take(40).Select(u=>u.Id).ToArray(),X=goal.X,Y=goal.Y});
        }
        try
        {
            Require(!headless,"Final AI match requires a native viewport");ReturnToDeployment();await PointerParityFrame();
            fast=false;clientPreferences.EdgeScrolling=false;clientPreferences.GameSpeed=6;Engine.TimeScale=3;
            practiceDifficulties=[1,1];fullPlaythroughControlsEnemy=false;fullPlaythroughPaused=true;
            const string mapId="mp08t2";var info=ClassicMapCatalog.Parse(Godot.FileAccess.GetFileAsString(ClassicMapCatalog.ResourcePath)).Find(mapId)
                ??throw new InvalidOperationException("Missing official map "+mapId);
            var settings=new MatchSettings(20261018,[new("最终AI对战",country,0),new("自主对手",enemy,1)],10000,info.Width,info.Height)
                {MapId=mapId,ClassicStart=true,StartingUnits=0,Crates=false,ShortGame=true};
            finalAiMatchReport["mapId"]=mapId;finalAiMatchReport["startingCredits"]=10000;finalAiMatchReport["startingUnits"]=0;
            finalAiMatchReport["country"]=country.ToString();finalAiMatchReport["enemyCountry"]=enemy.ToString();
            StartPractice(country,settings);await Wait(()=>localWorld is not null,"prepare-ordinary-match");world=localWorld!;
            terrainInspection=terrainInspectionMotion=false;home=world.Map.Spawns[0];enemyStart=world.Map.Spawns[1];Observe();
            Require(world.Tick==0 && world.Buildings.Count==0 && world.Units.Count==2 && world.Units.All(u=>u.Type==UnitType.Mcv)
                && world.Players.All(p=>p.Credits==10000),"Starting position was not the ordinary 10000-credit MCV-only match");
            await Capture("normal-start",home);fullPlaythroughPaused=false;
            var mcv=world.Units.Single(u=>u.Owner==0 && u.Type==UnitType.Mcv);battlefield!.Selection.Clear();battlefield.Selection.Add(mcv.Id);Focus(home);await Frame();
            using(var down=new InputEventKey{Pressed=true,Keycode=Key.D,PhysicalKeycode=Key.D})GetViewport().PushInput(down,true);
            using(var up=new InputEventKey{Keycode=Key.D,PhysicalKeycode=Key.D})GetViewport().PushInput(up,true);
            await Wait(()=>world.HasBuilding(0,BuildingType.ConstructionYard) && world.HasBuilding(1,BuildingType.ConstructionYard),"both-normal-mcv-deploy");
            home=new(world.Buildings.First(b=>b.Owner==0 && b.Type==BuildingType.ConstructionYard).CellX,world.Buildings.First(b=>b.Owner==0 && b.Type==BuildingType.ConstructionYard).CellY);
            while(!world.Finished)
            {
                Guard();
                if(world.Tick>=nextDecision)
                {
                    await Construct();await Reinforce();Fight();nextDecision=world.Tick+64;
                }
                if(!gotDevelopmentCapture && unloads.All(count=>count>0) && seenCombat.All(units=>units.Count>0))
                {gotDevelopmentCapture=true;await Capture("both-developed-and-mining",home);}
                if(!gotEnemyBaseCapture && world.Buildings.FirstOrDefault(b=>b.Alive && b.Owner==1 && b.Type!=BuildingType.ConstructionYard && world.Visible(0,b.CellX,b.CellY)) is {} scoutedBase)
                {gotEnemyBaseCapture=true;await Capture("enemy-developed-visible",new(scoutedBase.CellX,scoutedBase.CellY));}
                if(!gotBattleCapture && hits.All(count=>count>0))
                {
                    gotBattleCapture=true;var front=world.Units.FirstOrDefault(u=>u.Alive && u.Owner==0 && !u.Definition.Miner && u.LastFiredTick>0);
                    await Capture("mutual-live-combat",front is null?home:new(front.CellX,front.CellY));
                }
                if(world.Tick>=nextCapture)
                {
                    var front=world.Units.Where(u=>u.Alive && u.Owner==0 && !u.Definition.Miner).OrderByDescending(u=>u.LastFiredTick).FirstOrDefault();
                    await Capture("midgame-"+world.Tick,front is null?home:new(front.CellX,front.CellY));nextCapture=world.Tick+4000;
                }
                await Frame();
            }
        }
        catch(FinalAiNaturalEnd){ }
        catch(Exception exception){error=exception.ToString();GD.PushError("Final AI match: "+exception.Message);}
        finally
        {
            if(world?.Finished!=true)fullPlaythroughPaused=true;
            try
            {
                if(world is not null)
                {
                    Observe();await PointerParityFrame();await PointerParityFrame();
                    var natural=world.Finished && world.WinningTeam>=0 && world.Players.Count(p=>!p.Defeated)==1;
                    var ui=natural && MatchMenuOpen && matchMenuTitle is not null && matchMenuTitle.IsVisibleInTree()
                        && matchMenuTitle.Text==(world.WinningTeam==world.Players[0].Setup.Team?"作战胜利":"作战结束");
                    finalAiMatchReport["resultUiVisible"]=ui;finalAiMatchReport["resultUiTitle"]=matchMenuTitle?.Text??"";
                    finalAiMatchReport["resultNotice"]=notice;finalAiMatchReport["localDefeated"]=world.Players[0].Defeated;
                    finalAiMatchReport["naturalResult"]=natural;finalAiMatchReport["localPlayerWon"]=natural && world.WinningTeam==world.Players[0].Setup.Team;
                    var evidence=unloads.All(count=>count>0) && seenCombat.All(units=>units.Count>0) && hits.All(count=>count>0)
                        && commandCounts.All(counts=>counts.GetValueOrDefault(CommandType.Train.ToString())>0 && counts.GetValueOrDefault(CommandType.Build.ToString())>0);
                    finalAiMatchReport["bothSidesDevelopedMinedProducedAndHit"]=evidence;
                    if(error.Length==0 && !natural)error="No natural winner before the operator stopped";
                    if(error.Length==0 && !ui)error="Natural winner was not displayed by the ordinary Main result UI";
                    if(error.Length==0 && !evidence)error="Natural result lacks both sides' mining, production and mutual combat evidence";
                    finalAiMatchReport["passed"]=error.Length==0 && natural && ui && evidence;
                    finalAiMatchReport["audio"]=GetAudioAudit();
                    await Capture(natural?"natural-result":"bounded-failure",natural?enemyStart:home);
                    var replay=new ReplayFile{Settings=world.Settings,Commands=localReplay.ToList(),FinalTick=world.Tick,FinalHash=world.StateHash().ToString("x16")};
                    var replayPath=Path.Combine(folder,"match.replay.json");replay.Save(replayPath);finalAiMatchReport["replay"]=replayPath;
                }
            }
            catch(Exception finalError){if(error.Length==0)error=finalError.ToString();finalAiMatchReport["passed"]=false;}
            Engine.TimeScale=savedScale;clientPreferences.GameSpeed=savedSpeed;clientPreferences.EdgeScrolling=savedEdges;
            fast=savedFast;practiceDifficulties=savedDifficulties;fullPlaythroughControlsEnemy=false;fullPlaythroughPaused=true;
            terrainInspection=terrainInspectionMotion=false;forwardingBattlePointer=false;
            finalAiMatchReport["running"]=false;finalAiMatchReport["completed"]=true;finalAiMatchReport["elapsedSeconds"]=clock.Elapsed.TotalSeconds;
            finalAiMatchReport["error"]=error;finalAiMatchReport["pausedAfterOperator"]=true;Save();
        }
    }
}
