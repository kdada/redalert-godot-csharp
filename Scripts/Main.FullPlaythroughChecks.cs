using Godot;
using RedAlert.Simulation;
using System.Diagnostics;

namespace RedAlert;

public partial class Main
{
    // Developer operator: all products spend real credits and wait for normal
    // production. The opposite player receives ordinary commands, never spawned
    // units or shortened jobs. Rendering and stepping remain Main._Process.
    bool fullPlaythroughControlsEnemy,fullPlaythroughPaused;
    Godot.Collections.Dictionary fullPlaythroughReport=new();
    readonly List<CombatEvent> fullPlaythroughDamage=[];
    void ObserveFullPlaythroughTick(World world)
    {
        if(!fullPlaythroughReport.TryGetValue("running",out var active) || !active.AsBool())return;
        fullPlaythroughDamage.AddRange(world.Events.Where(e=>e.Damage>0));
    }
    public Godot.Collections.Dictionary GetFullPlaythroughAudit()
    {
        fullPlaythroughReport["rulesRevision"]=Rules.Revision;
        fullPlaythroughReport["damageEvents"]=fullPlaythroughDamage.Count;
        var hits=new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach(var e in fullPlaythroughDamage)hits.Add(new(){{"tick",e.Tick},{"source",e.Source},{"target",e.Target},{"damage",e.Damage}});
        fullPlaythroughReport["hits"]=hits;
        if(localWorld is {} world)
        {
            var units=new Godot.Collections.Array<Godot.Collections.Dictionary>();
            foreach(var u in world.Units)units.Add(new(){{"id",u.Id},{"owner",u.Owner},{"type",u.Type.ToString()},{"cell",new Vector2(u.CellX,u.CellY)},{"goal",new Vector2(u.GoalX,u.GoalY)},{"order",u.Order.ToString()},{"health",u.Health},{"ammo",u.Ammo},{"altitude",u.Altitude},{"pathCount",u.Path.Count-u.PathIndex},{"cargo",u.Cargo},{"returning",u.Returning},{"lastFired",u.LastFiredTick},{"target",u.TargetId},{"blockedTicks",u.BlockedTicks},{"nextPathTick",u.NextPathTick},{"visibleTo0",world.ClassicUnitVisibleTo(0,u)},{"visibleTo1",world.ClassicUnitVisibleTo(1,u)},{"landNavigable",world.Map.Navigable(u.CellX,u.CellY,MovementDomain.Land,u.Definition.Amphibious)},{"waterNavigable",world.Map.Navigable(u.CellX,u.CellY,MovementDomain.Water)},{"surfaceHeightFixed",world.Map.SurfaceHeightFixed(u.X,u.Y)},{"bridge",world.Map.HasBridge(u.CellX,u.CellY)}});
            fullPlaythroughReport["units"]=units;
            var buildings=new Godot.Collections.Array<Godot.Collections.Dictionary>();
            foreach(var b in world.Buildings)buildings.Add(new(){{"id",b.Id},{"owner",b.Owner},{"type",b.Type.ToString()},{"cell",new Vector2(b.CellLeft,b.CellTop)},{"health",b.Health},{"complete",b.Complete},{"remaining",b.RemainingBuildTicks},{"queue",string.Join(",",b.Queue)},{"progress",b.ProductionProgress},{"paid",b.ProductionPaid},{"paused",b.ProductionPaused}});
            fullPlaythroughReport["buildings"]=buildings;fullPlaythroughReport["rejectedCommands"]=world.RejectedCommands;
            fullPlaythroughReport["tick"]=world.Tick;fullPlaythroughReport["credits"]=world.Players[0].Credits;
        }
        return fullPlaythroughReport;
    }
    public async void RunFullPlaythrough(string scenario="land-air",int nation=0)
    {
        if(fullPlaythroughReport.TryGetValue("running",out var active) && active.AsBool())return;
        var folder=ProjectSettings.GlobalizePath("res://Reports/full-playthrough-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+scenario);
        Directory.CreateDirectory(folder);fullPlaythroughDamage.Clear();var stages=new Godot.Collections.Array<Godot.Collections.Dictionary>();
        fullPlaythroughReport=new(){{"running",true},{"passed",false},{"scenario",scenario},{"folder",folder},{"stages",stages},{"normalProduction",true},{"directEntitySpawns",0}};
        var savedSpeed=clientPreferences.GameSpeed;var savedScale=Engine.TimeScale;var savedFast=fast;var clock=Stopwatch.StartNew();
        World? world=null;var preferredNavalSites=new Dictionary<int,Cell>();var navalWater=new HashSet<Cell>();
        void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        async Task Wait(Func<bool> condition,string phase,int ticks=5000)
        {
            fullPlaythroughReport["phase"]=phase;var start=world?.Tick??0;var real=Stopwatch.StartNew();
            while(!condition())
            {
                if(real.Elapsed.TotalSeconds>90 || world is not null && world.Tick-start>ticks || clock.Elapsed.TotalSeconds>600)throw new TimeoutException(phase+" at tick "+world?.Tick);
                if(world is not null)
                {
                    fullPlaythroughReport["tick"]=world.Tick;fullPlaythroughReport["credits"]=world.Players[0].Credits;
                }
                await PointerParityFrame();
            }
        }
        void MovePointer(Vector2 point)
        {
            if(softwareBattlePointer)softwarePointer=ClampBattlePointer(point);
            forwardingBattlePointer=true;
            try{using var e=new InputEventMouseMotion{Position=point,GlobalPosition=point};GetViewport().PushInput(e,true);}
            finally{forwardingBattlePointer=false;}
        }
        void Click(Vector2 point)
        {
            MovePointer(point);forwardingBattlePointer=true;
            try
            {
                using var down=new InputEventMouseButton{Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left,Pressed=true};
                using var up=new InputEventMouseButton{Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left};GetViewport().PushInput(down,true);GetViewport().PushInput(up,true);
            }
            finally{forwardingBattlePointer=false;}
        }
        void Send(int player,GameCommand command)
        {if(player==0)Issue(command);else localPending.Add(command with{Player=player,Tick=world!.Tick,Sequence=++sequence});}
        void Focus(Vector2 cell)
        {var rise=LandscapeFocusRise(cell);battlefield!.SetCameraCell(cell-Vector2.One*rise);}
        float LandscapeFocusRise(Vector2 cell)=>world!.Map.OriginalDefinition is null?0:(float)world.Map.SurfaceHeightAt(cell.X,cell.Y)*.5f;
        async Task Capture(string label,Vector2? cell=null)
        {
            fullPlaythroughReport["phase"]=label;if(cell is {} focus)Focus(focus);MovePointer(battlefield!.GetGlobalRect().GetCenter());
            await PointerParityFrame();await PointerParityAfterDraw();var stage=new Godot.Collections.Dictionary{{"name",label},{"tick",world!.Tick},{"credits",world.Players[0].Credits},{"powerGenerated",world.Players[0].GeneratedPower},{"powerUsed",world.Players[0].UsedPower},{"accepted",world.AcceptedCommands},{"rejected",world.RejectedCommands}};
            using var image=GetViewport().GetTexture().GetImage();var file=Path.Combine(folder,$"{stages.Count:00}-{label}.png");Require(image.SavePng(file)==Error.Ok,"Capture failed");stage["png"]=file;stages.Add(stage);
            File.WriteAllText(Path.Combine(folder,"report.json"),Godot.Json.Stringify(fullPlaythroughReport,"\t"));
        }
        async Task Product(UnitType? unit,BuildingType? building)
        {
            productTab=unit is {} u?(Rules.Production(u)==ProductionCategory.Infantry?"infantry":"vehicle"):"build";lastProductKey="";RefreshClassicProducts();
            var index=productButtons.FindIndex(p=>p.Unit==unit && p.Building==building);Require(index>=0,"Missing sidebar product: "+unit+building);
            productPage=index/Math.Max(1,productPageCapacity);RefreshProductPage();await PointerParityFrame();
            var button=productButtons[index].Button;Require(button.Visible && !button.Disabled,"Unavailable sidebar product: "+unit+building);Click(button.GetGlobalRect().GetCenter());
        }
        Cell Site(int player,BuildingType type)
        {
            if(type==BuildingType.NavalYard && preferredNavalSites.TryGetValue(player,out var preferred) && world!.CanPlaceBuilding(player,type,preferred.X,preferred.Y))return preferred;
            var anchors=world!.Buildings.Where(b=>b.Alive && b.Complete && b.Owner==player).ToArray();
            var definition=Rules.Building(type,world.Players[player].Setup.Country);
            bool LeavesProductionDoors(Cell cell)
            {
                var footprint=new Rect2I(cell.X,cell.Y,definition.Width,definition.Height);
                foreach(var producer in anchors.Where(b=>b.Definition.Produces is ProductionCategory.Infantry or ProductionCategory.Vehicle or ProductionCategory.Naval || b.Type==BuildingType.Refinery))
                    if(world.ClassicProductionExitPortals(producer).Any(p=>footprint.HasPoint(new(p.X,p.Y))))return false;
                if(definition.Produces is not (ProductionCategory.Infantry or ProductionCategory.Vehicle or ProductionCategory.Naval) && type!=BuildingType.Refinery)return true;
                var planned=new Building{Type=type,FoundationCountry=world.Players[player].Setup.Country,Owner=player,CellLeft=cell.X,CellTop=cell.Y,X=cell.X*Rules.Scale+definition.Width*Rules.Scale/2,Y=cell.Y*Rules.Scale+definition.Height*Rules.Scale/2};
                return world.ClassicProductionExitPortals(planned).Any(p=>world.Map.Navigable(p.X,p.Y,definition.WaterBound?MovementDomain.Water:MovementDomain.Land));
            }
            return anchors.SelectMany(b=>Enumerable.Range(-10,21).SelectMany(dy=>Enumerable.Range(-10,21).Select(dx=>new Cell(b.CellX+dx,b.CellY+dy))))
                .Distinct().Where(c=>world.CanPlaceBuilding(player,type,c.X,c.Y) && (type!=BuildingType.NavalYard || navalWater.Contains(c)) && LeavesProductionDoors(c))
                .OrderBy(c=>anchors.Min(b=>Math.Abs(b.CellX-c.X)+Math.Abs(b.CellY-c.Y))).ThenBy(c=>c.Y).ThenBy(c=>c.X).Cast<Cell?>().FirstOrDefault()??throw new InvalidOperationException("No legal explored building site "+player+" "+type);
        }
        async Task<Building> Build(int player,BuildingType type)
        {
            await Wait(()=>world!.CanQueueBuilding(player,type),$"{player}-afford-{type}");var before=world!.Buildings.Select(b=>b.Id).DefaultIfEmpty().Max();
            if(player==0)await Product(null,type);else Send(player,new(){Type=CommandType.QueueBuilding,Building=type});
            await Wait(()=>world.Players[player].BuildingProduction==type,$"{player}-queue-{type}");await Wait(()=>world.Players[player].BuildingReady,$"{player}-prepare-{type}");
            var site=Site(player,type);
            if(player==0)
            {
                await Product(null,type);Require(battlefield!.Placement==type,"Ready building did not enter placement");
                // A moving miner can occupy a site between selection and the real
                // click. Re-evaluate and retry ordinary viewport placement briefly.
                for(var attempt=0;attempt<8 && !world.Buildings.Any(b=>b.Id>before && b.Owner==player && b.Type==type);attempt++)
                {
                    site=Site(player,type);Focus(new(site.X+.25f,site.Y+.25f));await PointerParityFrame();
                    var projected=battlefield.ProjectGround(new Vector2(site.X+.25f,site.Y+.25f));
                    var resolved=battlefield.UnprojectGround(projected);
                    fullPlaythroughReport["lastPlacement"]=new Godot.Collections.Dictionary{{"type",type.ToString()},{"site",new Vector2(site.X,site.Y)},{"resolved",resolved},{"attempt",attempt+1},{"legal",world.CanPlaceBuilding(player,type,site.X,site.Y)}};
                    if(world.CanPlaceBuilding(player,type,site.X,site.Y))
                        Click(battlefield.GlobalPosition+projected);
                    await PointerParityFrame();await PointerParityFrame();
                    if(attempt>0)fullPlaythroughReport["placementRetries"]=fullPlaythroughReport.TryGetValue("placementRetries",out var retries)?retries.AsInt32()+1:1;
                }
                Require(world.Buildings.Any(b=>b.Id>before && b.Owner==player && b.Type==type),"Viewport building placement did not complete after eight attempts");
            }
            else Send(player,new(){Type=CommandType.Build,Building=type,X=site.X,Y=site.Y});
            await Wait(()=>world.Buildings.Any(b=>b.Id>before && b.Owner==player && b.Type==type && b.Complete),$"{player}-place-{type}");
            return world.Buildings.First(b=>b.Id>before && b.Owner==player && b.Type==type && b.Complete);
        }
        async Task<Unit> Train(int player,UnitType type)
        {
            var producer=Rules.ProducerForUnit(type,world!.Players[player].Setup.Country);
            await Wait(()=>world.Buildings.Any(b=>b.Owner==player && b.Type==producer && world.CanTrainUnit(player,type,b)),$"{player}-afford-{type}");
            var before=world.Units.Select(u=>u.Id).DefaultIfEmpty().Max();
            if(player==0)await Product(type,null);else
            {var factory=world.Buildings.First(b=>b.Owner==player && b.Type==producer && world.CanTrainUnit(player,type,b));Send(player,new(){Type=CommandType.Train,Unit=type,Actors=[factory.Id]});}
            await Wait(()=>world.Units.Any(u=>u.Id>before && u.Owner==player && u.Type==type),$"{player}-produce-{type}");
            return world.Units.First(u=>u.Id>before && u.Owner==player && u.Type==type);
        }
        async Task Deploy(int player,bool coastal)
        {
            var mcv=world!.Units.First(u=>u.Owner==player && u.Type==UnitType.Mcv);
            if(coastal)
            {
                var def=Rules.Building(BuildingType.ConstructionYard,mcv.OriginCountry);var yard=Rules.Building(BuildingType.NavalYard,mcv.OriginCountry);var map=world.Map;
                var reachable=new HashSet<Cell>();var queue=new Queue<Cell>();queue.Enqueue(new(mcv.CellX,mcv.CellY));reachable.Add(queue.Peek());
                while(queue.Count>0){var c=queue.Dequeue();foreach(var n in new[]{new Cell(c.X+1,c.Y),new Cell(c.X-1,c.Y),new Cell(c.X,c.Y+1),new Cell(c.X,c.Y-1)})if(!reachable.Contains(n) && map.CanTraverseMovement(c,n,MovementDomain.Land)){reachable.Add(n);queue.Enqueue(n);}}
                if(navalWater.Count==0)
                {
                    var seen=new HashSet<Cell>();
                    for(var y=0;y<map.Height;y++)for(var x=0;x<map.Width;x++)
                    {
                        var start=new Cell(x,y);if(seen.Contains(start) || !map.Navigable(x,y,MovementDomain.Water))continue;
                        var component=new HashSet<Cell>();queue.Clear();queue.Enqueue(start);component.Add(start);seen.Add(start);
                        while(queue.Count>0){var c=queue.Dequeue();foreach(var n in new[]{new Cell(c.X+1,c.Y),new Cell(c.X-1,c.Y),new Cell(c.X,c.Y+1),new Cell(c.X,c.Y-1)})if(!seen.Contains(n) && map.CanTraverseMovement(c,n,MovementDomain.Water)){seen.Add(n);component.Add(n);queue.Enqueue(n);}}
                        if(component.Count>navalWater.Count)navalWater=component;
                    }
                }
                bool InMainSea(int x,int y)
                {for(var dy=0;dy<yard.Height;dy++)for(var dx=0;dx<yard.Width;dx++)if(!navalWater.Contains(new(x+dx,y+dy)))return false;return true;}
                var waterSites=new List<Cell>();for(var y=1;y<map.Height-yard.Height;y++)for(var x=1;x<map.Width-yard.Width;x++)if(InMainSea(x,y) && map.FootprintFree(yard,x,y))waterSites.Add(new(x,y));
                bool InYardSight(Cell bank,Cell site)
                {
                    var cx=bank.X-(def.Width-1)/2+def.Width/2;var cy=bank.Y-(def.Height-1)/2+def.Height/2;
                    for(var dy=0;dy<yard.Height;dy++)for(var dx=0;dx<yard.Width;dx++)if((site.X+dx-cx)*(site.X+dx-cx)+(site.Y+dy-cy)*(site.Y+dy-cy)>def.Sight*def.Sight)return false;
                    return true;
                }
                var bank=reachable.Where(c=>map.FootprintFree(def,c.X-(def.Width-1)/2,c.Y-(def.Height-1)/2) && waterSites.Any(w=>Math.Abs(w.X-c.X)+Math.Abs(w.Y-c.Y)<=7 && InYardSight(c,w)))
                    .OrderBy(c=>Math.Abs(c.X-mcv.CellX)+Math.Abs(c.Y-mcv.CellY)).ThenBy(c=>c.Y).ThenBy(c=>c.X).Cast<Cell?>().FirstOrDefault()??throw new InvalidOperationException("No connected coastal MCV site");
                preferredNavalSites[player]=waterSites.Where(w=>Math.Abs(w.X-bank.X)+Math.Abs(w.Y-bank.Y)<=7 && InYardSight(bank,w)).OrderBy(w=>Math.Abs(w.X-bank.X)+Math.Abs(w.Y-bank.Y)).ThenBy(w=>w.Y).ThenBy(w=>w.X).First();
                Send(player,new(){Type=CommandType.Move,Actors=[mcv.Id],X=bank.X,Y=bank.Y});await Wait(()=>mcv.CellX==bank.X && mcv.CellY==bank.Y,$"{player}-coastal-move",10000);
                Send(player,new(){Type=CommandType.Stop,Actors=[mcv.Id]});await PointerParityFrame();
            }
            if(player==0)
            {
                battlefield!.Selection.Clear();battlefield.Selection.Add(mcv.Id);Focus(new(mcv.CellX+.5f,mcv.CellY+.5f));await PointerParityFrame();
                using var down=new InputEventKey{Pressed=true,Keycode=Key.D,PhysicalKeycode=Key.D};using var up=new InputEventKey{Keycode=Key.D,PhysicalKeycode=Key.D};GetViewport().PushInput(down,true);GetViewport().PushInput(up,true);
            }
            else Send(player,new(){Type=CommandType.Deploy,Actors=[mcv.Id]});
            await Wait(()=>world.HasBuilding(player,BuildingType.ConstructionYard),$"{player}-deploy");
        }
        try
        {
            Require(!headless,"Full playthrough needs native rendering");ReturnToDeployment();await PointerParityFrame();
            var country=(Country)Math.Clamp(nation,0,8);var naval=scenario=="naval";var ordinaryAi=scenario=="ai";var priceProbe=scenario=="price";var mapId=naval?"mp01t4":"mp08t2";var info=ClassicMapCatalog.Parse(Godot.FileAccess.GetFileAsString(ClassicMapCatalog.ResourcePath)).Find(mapId)!;
            var enemy=Rules.Side(country)==Faction.Allied?Country.Russia:Country.America;
            var startingCredits=priceProbe?Rules.Building(BuildingType.PowerPlant,country).Cost:10000;
            var settings=new MatchSettings(20261018,[new("原生流程验证",country,0),new("对战指挥官",enemy,1)],startingCredits,info.Width,info.Height){MapId=mapId,ClassicStart=true,StartingUnits=0,Crates=false};
            practiceDifficulties=[1,1];fullPlaythroughControlsEnemy=!ordinaryAi;fast=false;clientPreferences.GameSpeed=6;Engine.TimeScale=3;StartPractice(country,settings);
            fullPlaythroughReport["ordinaryPracticeAi"]=ordinaryAi;
            await Wait(()=>localWorld is not null,"prepare-match");world=localWorld!;terrainInspection=false;fullPlaythroughReport["country"]=country.ToString();fullPlaythroughReport["enemyCountry"]=enemy.ToString();fullPlaythroughReport["mapId"]=mapId;
            await Deploy(0,naval);
            if(ordinaryAi)await Wait(()=>world.HasBuilding(1,BuildingType.ConstructionYard),"ai-mcv-deploy");else await Deploy(1,naval);
            var home=world.Buildings.First(b=>b.Owner==0 && b.Type==BuildingType.ConstructionYard);await Capture("mcv-deployed",new(home.CellX,home.CellY));
            var pp=await Build(0,BuildingType.PowerPlant);
            if(priceProbe)
            {
                Require(world.Players[0].Credits==0,"Exact country price was not charged");await Capture("exact-country-price",new(pp.CellX,pp.CellY));
                fullPlaythroughReport["passed"]=true;fullPlaythroughReport["charged"]=startingCredits;fullPlaythroughReport["finalTick"]=world.Tick;fullPlaythroughReport["rejectedCommands"]=world.RejectedCommands;return;
            }
            if(!ordinaryAi)await Build(1,BuildingType.PowerPlant);
            var refinery=await Build(0,BuildingType.Refinery);if(!ordinaryAi)await Build(1,BuildingType.Refinery);var money=world.Players[0].Credits;
            await Wait(()=>world.Players[0].Credits>money,"real-miner-delivery",8000);await Capture("ore-delivered",new(refinery.CellX,refinery.CellY));
            await Build(0,BuildingType.Barracks);if(!ordinaryAi)await Build(1,BuildingType.Barracks);await Build(0,BuildingType.WarFactory);if(!ordinaryAi)await Build(1,BuildingType.WarFactory);
            var allied=Rules.Side(country)==Faction.Allied;var infantry=await Train(0,allied?UnitType.Gi:UnitType.Conscript);var tank=await Train(0,allied?UnitType.Grizzly:UnitType.Rhino);
            await Capture("ground-products",new(tank.CellX,tank.CellY));
            if(ordinaryAi)
            {
                await Wait(()=>world.HasBuilding(1,BuildingType.WarFactory) && world.Units.Any(u=>u.Alive && u.Owner==1 && !u.Definition.Miner && u.Type!=UnitType.Mcv),"ai-production");
                var began=world.Tick;var actors=new[]{tank,infantry};var enemyHome=world.Map.Spawns[1];
                Send(0,new(){Type=CommandType.AttackMove,Actors=actors.Select(u=>u.Id).ToArray(),X=enemyHome.X,Y=enemyHome.Y});
                await Wait(()=>actors.Any(u=>u.LastFiredTick>=began) && (actors.Any(u=>u.Health<u.MaxHealth) || world.Units.Any(u=>u.Owner==1 && u.Health<u.MaxHealth)),"ordinary-ai-combat",12000);
                await Capture("ordinary-ai-skirmish",new(tank.CellX,tank.CellY));
                fullPlaythroughReport["passed"]=true;fullPlaythroughReport["finalTick"]=world.Tick;fullPlaythroughReport["acceptedCommands"]=world.AcceptedCommands;fullPlaythroughReport["rejectedCommands"]=world.RejectedCommands;fullPlaythroughReport["audio"]=GetAudioAudit();return;
            }
            var enemyTank=await Train(1,allied?UnitType.Rhino:UnitType.Grizzly);
            Unit combat=tank,opponent=enemyTank;
            if(naval)
            {
                var yard=await Build(0,BuildingType.NavalYard);await Build(1,BuildingType.NavalYard);combat=await Train(0,allied?UnitType.Destroyer:UnitType.AttackSubmarine);opponent=await Train(1,allied?UnitType.AttackSubmarine:UnitType.Destroyer);
                await Capture("naval-products",new(yard.CellX,yard.CellY));
            }
            else
            {
                await Build(0,BuildingType.Radar);
                if(!allied){await Build(0,BuildingType.PowerPlant);await Build(0,BuildingType.BattleLab);}
                var aircraft=await Train(0,allied?(country==Country.Korea?UnitType.BlackEagle:UnitType.Harrier):UnitType.Kirov);await Capture("aircraft-produced",new(aircraft.CellX,aircraft.CellY));
                var airStart=world.Tick;var health=enemyTank.Health;Send(0,new(){Type=CommandType.Move,Actors=[aircraft.Id],X=enemyTank.CellX,Y=enemyTank.CellY});
                await Wait(()=>world.Visible(0,enemyTank.CellX,enemyTank.CellY) && aircraft.Altitude>0,"aircraft-scout",8000);Send(0,new(){Type=CommandType.Attack,Actors=[aircraft.Id],Target=enemyTank.Id});
                await Wait(()=>fullPlaythroughDamage.Any(e=>e.Tick>=airStart && e.Source==aircraft.Id && e.Target==enemyTank.Id),"air-combat",5000);await Capture("air-combat",new(aircraft.CellX,aircraft.CellY));
                if(aircraft.Definition.AirportBound)
                {
                    var ammo=aircraft.Ammo;Require(ammo<aircraft.Definition.Ammo,"Aircraft did not consume ammo");Send(0,new(){Type=CommandType.Stop,Actors=[aircraft.Id]});
                    await Wait(()=>aircraft.Alive && aircraft.Altitude==0 && aircraft.Ammo==aircraft.Definition.Ammo,"aircraft-return-rearm",10000);await Capture("aircraft-rearmed",new(aircraft.CellX,aircraft.CellY));
                }
                else
                {
                    Send(0,new(){Type=CommandType.Move,Actors=[aircraft.Id],X=home.CellX,Y=home.CellY});
                    await Wait(()=>Math.Abs(aircraft.CellX-home.CellX)+Math.Abs(aircraft.CellY-home.CellY)<3,"kirov-return",10000);
                }
                opponent=await Train(1,allied?UnitType.Rhino:UnitType.Grizzly);
            }
            // Use an intersection of terrain-connected components. Navigable alone
            // cannot establish that two ships can leave their respective bays.
            HashSet<Cell> Reachable(Unit unit)
            {
                var found=new HashSet<Cell>();var queue=new Queue<Cell>();var start=new Cell(unit.CellX,unit.CellY);found.Add(start);queue.Enqueue(start);
                while(queue.Count>0)
                {
                    var c=queue.Dequeue();foreach(var n in new[]{new Cell(c.X+1,c.Y),new Cell(c.X-1,c.Y),new Cell(c.X,c.Y+1),new Cell(c.X,c.Y-1)})
                        if(!found.Contains(n) && world.Map.CanTraverseMovement(c,n,unit.Definition.Domain,unit.Definition.Amphibious)){found.Add(n);queue.Enqueue(n);}
                }
                return found;
            }
            var midpoint=new Cell((combat.CellX+opponent.CellX)/2,(combat.CellY+opponent.CellY)/2);var ours=Reachable(combat);var theirs=Reachable(opponent);
            var rally=ours.Where(theirs.Contains).OrderBy(c=>Math.Abs(c.X-midpoint.X)+Math.Abs(c.Y-midpoint.Y)).ThenBy(c=>c.Y).ThenBy(c=>c.X).Cast<Cell?>().FirstOrDefault();
            Require(rally is not null,"Produced opponents have no common reachable combat area");var dest=rally!.Value;var battleStart=world.Tick;var ourHealth=combat.Health;var enemyHealth=opponent.Health;
            if(naval)
            {
                var adjacent=new[]{new Cell(dest.X+1,dest.Y),new Cell(dest.X,dest.Y+1),new Cell(dest.X-1,dest.Y),new Cell(dest.X,dest.Y-1)}.First(c=>ours.Contains(c) && theirs.Contains(c) && world.Map.CanTraverseMovement(dest,c,MovementDomain.Water));
                Send(0,new(){Type=CommandType.Move,Actors=[combat.Id],X=dest.X,Y=dest.Y});Send(1,new(){Type=CommandType.Move,Actors=[opponent.Id],X=adjacent.X,Y=adjacent.Y});
                await Wait(()=>World.DistanceSquared(combat,opponent)<=9L*Rules.Scale*Rules.Scale && world.Visible(0,opponent.CellX,opponent.CellY) && world.Visible(1,combat.CellX,combat.CellY),"sea-rendezvous",12000);
                await Capture("sea-rendezvous",new(combat.CellX,combat.CellY));
                Send(0,new(){Type=CommandType.Attack,Actors=[combat.Id],Target=opponent.Id});Send(1,new(){Type=CommandType.Attack,Actors=[opponent.Id],Target=combat.Id});
            }
            else {Send(0,new(){Type=CommandType.AttackMove,Actors=[combat.Id],X=dest.X,Y=dest.Y});Send(1,new(){Type=CommandType.AttackMove,Actors=[opponent.Id],X=dest.X,Y=dest.Y});}
            await Wait(()=>fullPlaythroughDamage.Any(e=>e.Tick>=battleStart && (e.Source==combat.Id && e.Target==opponent.Id || e.Source==opponent.Id && e.Target==combat.Id)),"produced-units-exchange-fire",12000);
            Require(combat.Alive && opponent.Alive,"Combat pair died before exchanging fire");
            Send(0,new(){Type=CommandType.Attack,Actors=[combat.Id],Target=opponent.Id});Send(1,new(){Type=CommandType.Attack,Actors=[opponent.Id],Target=combat.Id});
            await Wait(()=>fullPlaythroughDamage.Any(e=>e.Tick>=battleStart && e.Source==combat.Id && e.Target==opponent.Id) && fullPlaythroughDamage.Any(e=>e.Tick>=battleStart && e.Source==opponent.Id && e.Target==combat.Id),"both-combatants-hit",3000);
            await Capture(naval?"sea-combat":"land-combat",new(combat.CellX,combat.CellY));
            Require(world.Tick>0 && world.Players.All(p=>!p.Defeated),"Match ended before validation");
            fullPlaythroughReport["passed"]=true;fullPlaythroughReport["finalTick"]=world.Tick;fullPlaythroughReport["acceptedCommands"]=world.AcceptedCommands;fullPlaythroughReport["rejectedCommands"]=world.RejectedCommands;fullPlaythroughReport["audio"]=GetAudioAudit();
        }
        catch(Exception error)
        {
            fullPlaythroughReport["error"]=error.ToString();GD.PushError("Full playthrough: "+error.Message);
            try{if(world is not null && battlefield is not null)await Capture("failure");}catch(Exception captureError){fullPlaythroughReport["captureError"]=captureError.Message;}
        }
        finally
        {
            Engine.TimeScale=savedScale;clientPreferences.GameSpeed=savedSpeed;fast=savedFast;fullPlaythroughControlsEnemy=false;fullPlaythroughPaused=true;terrainInspection=terrainInspectionMotion=false;
            fullPlaythroughReport["running"]=false;fullPlaythroughReport["completed"]=true;fullPlaythroughReport["elapsedSeconds"]=clock.Elapsed.TotalSeconds;
            GetFullPlaythroughAudit();File.WriteAllText(Path.Combine(folder,"report.json"),Godot.Json.Stringify(fullPlaythroughReport,"\t"));
        }
    }
}
