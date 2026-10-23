using Godot;
using RedAlert.Simulation;
using System.Diagnostics;

namespace RedAlert;

public partial class Main
{
    bool terrainDestructionChecksRunning;
    Godot.Collections.Dictionary terrainDestructionReport=new();
    public Godot.Collections.Dictionary GetTerrainDestructionAudit()=>terrainDestructionReport;
    sealed record TerrainDestructionSite(ClassicMapInfo Info,int TargetId,int X,int Y,Cell? TreeControl=null);
    static MatchSettings TerrainDestructionSettings(ClassicMapInfo info)=>new(20261022,
        [new PlayerSetup("Terrain inspection",Country.America,0),new PlayerSetup("Fixture survivor",Country.Russia,1)],
        Width:info.Width,Height:info.Height,MapId:info.Id,ClassicStart:true,StartingUnits:0,ShortGame:false,Crates:false);
    static void TerrainDestructionRequire(bool value,string message)
    {if(!value)throw new InvalidOperationException(message);}
    static List<Cell> TerrainDestructionFiringCells(BattleMap map,int x,int y)
    {
        var cells=new List<Cell>();var range=Rules.Unit(UnitType.V3Launcher,Country.Russia).PrimaryWeapon?.RangeFixed??0;
        for(var dy=-10;dy<=10;dy++)for(var dx=-10;dx<=10;dx++)
        {
            var distance=(long)(dx*dx+dy*dy)*Rules.Scale*Rules.Scale;
            if(distance<36L*Rules.Scale*Rules.Scale || distance>(long)(range-Rules.Scale)*(range-Rules.Scale))continue;
            var cx=x+dx;var cy=y+dy;
            if(map.Navigable(cx,cy,MovementDomain.Land) && !map.HasBridge(cx,cy)
                && !map.BuildingBlocksMovement(cx*Rules.Scale+Rules.Scale/2,cy*Rules.Scale+Rules.Scale/2,
                    cx*Rules.Scale+Rules.Scale/2,cy*Rules.Scale+Rules.Scale/2,Rules.Scale/3))cells.Add(new(cx,cy));
        }
        return cells.OrderBy(cell=>(cell.X-x)*(cell.X-x)+(cell.Y-y)*(cell.Y-y)).ThenBy(cell=>cell.Y).ThenBy(cell=>cell.X).ToList();
    }
    static Cell? TerrainDestructionTreeControl(World world,TerrainTarget target)
    {
        var map=world.Map;var item=map.OriginalObjects[target.ObjectIndex];
        if(!map.IsPlayable(target.CellX,target.CellY) || map.HasBridge(target.CellX,target.CellY)
            || map.Ground[map.Index(target.CellX,target.CellY)] is not (Terrain.Grass or Terrain.Dirt))return null;
        // Keep the control footprint and its immediate approach clear of any
        // other original blocker; never remove a second object to make a route.
        for(var i=0;i<map.OriginalObjects.Count;i++)
        {
            if(i==target.ObjectIndex)continue;var other=map.OriginalObjects[i];
            if(other.Definition.BlocksLandMovement && other.X<item.X+item.Width+1 && other.X+other.Width>item.X-1
                && other.Y<item.Y+item.Height+1 && other.Y+other.Height>item.Y-1)return null;
        }
        var cleared=new Cell(target.CellX,target.CellY);
        foreach(var cell in new[]{new Cell(cleared.X-1,cleared.Y),new Cell(cleared.X+1,cleared.Y),new Cell(cleared.X,cleared.Y-1),new Cell(cleared.X,cleared.Y+1)})
            if(map.Walkable(cell.X,cell.Y) && !map.HasBridge(cell.X,cell.Y) && map.CanTraverseTerrain(cell,cleared)
                && world.NearestFree(cell.X,cell.Y,true)==cell
                && !map.BuildingBlocksMovement(cell.X*Rules.Scale+Rules.Scale/2,cell.Y*Rules.Scale+Rules.Scale/2,
                    cell.X*Rules.Scale+Rules.Scale/2,cell.Y*Rules.Scale+Rules.Scale/2,Rules.Scale/3))return cell;
        return null;
    }
    static bool TerrainDestructionBridgeRoute(BattleMap map,TerrainTarget target,out List<int> path)
    {
        var span=map.BridgeSpans.First(candidate=>candidate.Id==target.BridgeId);path=[];
        if(!map.Walkable(span.BankA.X,span.BankA.Y) || !map.Walkable(span.BankB.X,span.BankB.Y)
            || map.HasBridge(span.BankA) || map.HasBridge(span.BankB)
            || map.Ground[map.Index(span.BankA.X,span.BankA.Y)]==Terrain.Water
            || map.Ground[map.Index(span.BankB.X,span.BankB.Y)]==Terrain.Water)return false;
        path=new GridPathfinder(map).Find(span.BankA.X,span.BankA.Y,span.BankB.X,span.BankB.Y);
        return path.Any(index=>map.BridgeIds[index]==target.BridgeId
            && map.Navigable(index%map.Width,index/map.Width,MovementDomain.Water));
    }
    World TerrainDestructionOpen(ClassicMapInfo info)
    {
        CancelLocalPreparation();StopExternalAi();network?.Dispose();network=null;
        var world=new World(TerrainDestructionSettings(info));
        TerrainDestructionRequire(world.Map.OriginalDefinition?.Id==info.Id && world.Map.OriginalDefinition.SourceSha256==info.SourceSha256,
            "Fixture is not the registered official source map: "+info.Id);
        localWorld=world;terrainInspection=true;terrainInspectionMotion=false;accumulator=0;ended=false;failure="";
        localPending.Clear();localReplay.Clear();ShowBattle();RevealTerrainInspection();RefreshHud();minimap?.InvalidateTerrain();return world;
    }
    Godot.Collections.Dictionary TerrainDestructionState(World world,TerrainTarget target,int x,int y)=>new()
    {
        ["mapId"]=world.Settings.MapId,["sourceSha256"]=world.Map.OriginalDefinition!.SourceSha256,["tick"]=world.Tick,
        ["id"]=target.Id,["kind"]=target.Kind.ToString(),["objectIndex"]=target.ObjectIndex,["bridgeId"]=target.BridgeId,
        ["health"]=target.Health,["maxHealth"]=target.MaxHealth,["armor"]=target.Armor,["immune"]=target.Immune,["alive"]=target.Alive,
        ["x"]=x,["y"]=y,["navigationVersion"]=world.Map.NavigationVersion,["walkable"]=world.Map.Walkable(x,y),
        ["landNavigable"]=world.Map.Navigable(x,y,MovementDomain.Land),["waterNavigable"]=world.Map.Navigable(x,y,MovementDomain.Water),
        ["supportHeightFixed"]=world.Map.SurfaceHeightFixed(x*Rules.Scale+Rules.Scale/2,y*Rules.Scale+Rules.Scale/2),
        ["bridgeIntact"]=world.Map.IsBridgeCellIntact(x,y),["render"]=battlefield!.GetTerrainVisualAudit(),
        ["originalObjectQueued"]=target.ObjectIndex>=0 && battlefield.OriginalObjectQueued(target.ObjectIndex),
        ["originalBridgePiecesDrawn"]=battlefield.OriginalBridgePiecesDrawn,["bridgeForegroundDraws"]=battlefield.OriginalBridgeForegroundDraws,
        ["bridgeVisualQueued"]=target.BridgeId>0 && battlefield.BridgeVisualQueued(target.BridgeId),["camera"]=battlefield.CameraCell
    };
    async Task<Godot.Collections.Dictionary> TerrainDestructionCapture(string folder,string tag,World world,TerrainTarget target,int x,int y)
    {
        var view=battlefield??throw new InvalidOperationException("Actual Battlefield is missing");var before=view.DrawCalls;
        TerrainDestructionRequire(ReferenceEquals(Battle,world) && ReferenceEquals(view.Controller,this),"Capture lost the actual Main world/renderer");
        view.QueueRedraw();await PointerParityFrame();await PointerParityAfterDraw();
        TerrainDestructionRequire(view.DrawCalls>before,"Native Battlefield did not draw the destruction fixture");
        using var image=GetViewport().GetTexture().GetImage();
        TerrainDestructionRequire(image.GetWidth()>0 && image.GetHeight()>0,"Native viewport image is empty");
        var png=Path.Combine(folder,tag+".png");TerrainDestructionRequire(image.SavePng(png)==Error.Ok,"Native viewport PNG save failed");
        var capture=new Godot.Collections.Dictionary
        {
            ["tag"]=tag,["png"]=png,["nativeViewport"]=true,["frame"]=Engine.GetFramesDrawn(),
            ["width"]=image.GetWidth(),["height"]=image.GetHeight(),["state"]=TerrainDestructionState(world,target,x,y)
        };
        File.WriteAllText(Path.Combine(folder,tag+".json"),Godot.Json.Stringify(capture,"\t"));return capture;
    }

    // Source-only Native operator. Root explicitly invokes this on the existing
    // Main after releasing the build hold. Setup may spawn/move fixture actors;
    // target HP, damage, projectiles and support changes only come from World.Step.
    public async void RunTerrainDestructionChecks()
    {
        if(terrainDestructionChecksRunning)return;terrainDestructionChecksRunning=true;
        const int maximumSteps=1600;var totalSteps=0;var error="";var folder="";var clock=Stopwatch.StartNew();
        var checks=new Godot.Collections.Array<string>();var missing=new Godot.Collections.Array<string>();
        var notes=new Godot.Collections.Array<string>();var captures=new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var scans=new Godot.Collections.Array<Godot.Collections.Dictionary>();var cases=new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var savedEdges=clientPreferences.EdgeScrolling;var savedFast=fast;
        terrainDestructionReport=new()
        {
            ["running"]=true,["completed"]=false,["passed"]=false,["phase"]="starting",["checks"]=checks,["missingConditions"]=missing,
            ["coverageNotes"]=notes,["captures"]=captures,["mapSearch"]=scans,["cases"]=cases,["maximumWorldSteps"]=maximumSteps,
            ["maximumSeconds"]=90,["oldLongSuitesRun"]=false,["directTargetHealthWrites"]=0,["directTerrainDamageCalls"]=0,
            ["captureSource"]="actual Main viewport after Battlefield draw and RenderingServer.FramePostDraw"
        };
        void Step(World world)
        {
            if(totalSteps>=maximumSteps || clock.Elapsed.TotalSeconds>90)throw new TimeoutException("Bounded terrain destruction inspection expired");
            RevealTerrainInspection();var commands=localPending.ToArray();localPending.Clear();world.Step(commands);totalSteps++;
            battlefield!.CaptureEvents();RevealTerrainInspection();
        }
        void Click(Vector2 local,MouseButton button,bool control=false)
        {
            var view=battlefield!;var point=view.GetGlobalTransformWithCanvas()*local;
            softwarePointer=point;forwardingBattlePointer=true;view.GrabFocus();
            try
            {
                using var down=new InputEventMouseButton{Position=point,GlobalPosition=point,WindowId=GetWindow().GetWindowId(),ButtonIndex=button,Pressed=true,CtrlPressed=control};
                using var up=new InputEventMouseButton{Position=point,GlobalPosition=point,WindowId=GetWindow().GetWindowId(),ButtonIndex=button,CtrlPressed=control};
                GetViewport().PushInput(down,true);GetViewport().PushInput(up,true);
            }
            finally{forwardingBattlePointer=false;}
        }
        try
        {
            TerrainDestructionRequire(!headless && DisplayServer.GetName()!="headless","Terrain destruction inspection needs the actual native Main root");
            TerrainDestructionRequire(!pointerParityChecksRunning && !vehicleSlopeChecksRunning && !directionDeployChecksRunning && !battlePolishChecksRunning,
                "Another Native inspection is running");
            ReturnToDeployment();await PointerParityFrame();await PointerParityFrame();
            clientPreferences.EdgeScrolling=false;fast=false;
            if(!GetWindow().HasFocus()){GetWindow().GrabFocus();await PointerParityFrame();await PointerParityFrame();}
            TerrainDestructionRequire(GetWindow().HasFocus(),"Native game window must have focus");
            folder=ProjectSettings.GlobalizePath("res://Reports/terrain-destruction/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Time.GetTicksMsec());
            Directory.CreateDirectory(folder);terrainDestructionReport["folder"]=folder;
            var catalog=ClassicMapCatalog.Parse(Godot.FileAccess.GetFileAsString(ClassicMapCatalog.ResourcePath));
            var treeSites=new List<TerrainDestructionSite>();var bridgeSites=new List<TerrainDestructionSite>();
            terrainDestructionReport["phase"]="searching-official-maps";
            foreach(var info in catalog.Maps)
            {
                var definition=ClassicMapRepository.Get(info.Id);var scanWorld=new World(TerrainDestructionSettings(info));var map=scanWorld.Map;
                TerrainDestructionRequire(definition.SourceSha256==info.SourceSha256,"Registered map hash differs from catalog: "+info.Id);
                var trees=map.TerrainTargets.Where(target=>target.Kind==TerrainTargetKind.Tree && target.Alive && !target.Immune && map.IsPlayable(target.CellX,target.CellY)).ToArray();
                var bridges=map.TerrainTargets.Where(target=>target.Kind==TerrainTargetKind.Bridge && target.Alive && !target.Immune).ToArray();
                foreach(var target in trees)
                    if(TerrainDestructionTreeControl(scanWorld,target) is {} control && TerrainDestructionFiringCells(map,target.CellX,target.CellY).Count>=4)
                    {treeSites.Add(new(info,target.Id,target.CellX,target.CellY,control));break;}
                foreach(var target in bridges)
                {
                    if(!TerrainDestructionBridgeRoute(map,target,out _))continue;
                    var cell=definition.RuntimeCells.Where(cell=>cell.Bridge && cell.HighBridge && cell.Playable
                            && map.TerrainTargetAt(cell.X,cell.Y)?.Id==target.Id && map.Walkable(cell.X,cell.Y)
                            && map.Navigable(cell.X,cell.Y,MovementDomain.Water))
                        .OrderBy(cell=>cell.X+cell.Y).FirstOrDefault(cell=>TerrainDestructionFiringCells(map,cell.X,cell.Y).Count>=4);
                    if(cell is not null)bridgeSites.Add(new(info,target.Id,cell.X,cell.Y));
                }
                scans.Add(new(){["mapId"]=info.Id,["sourceSha256"]=definition.SourceSha256,["trees"]=trees.Length,["bridges"]=bridges.Length,
                    ["usableTree"]=treeSites.Any(site=>site.Info.Id==info.Id),["usableBridges"]=bridgeSites.Count(site=>site.Info.Id==info.Id)});
                await PointerParityFrame();
            }
            checks.Add("Searched actual registered official maps and verified source hashes; no synthetic terrain or target HP setup");
            var treeSite=treeSites.FirstOrDefault();
            var bridgeSite=bridgeSites.OrderByDescending(site=>site.Info.Id=="mp02t2").FirstOrDefault();
            if(treeSite is null)missing.Add("No independent official tree with a free traversable adjacent movement control and four legal firing positions");
            if(bridgeSite is null)missing.Add("No official high bridge with an actual navigable water underpass and a dry bank-to-bank shortest route crossing that deck");
            foreach(var site in new[]{treeSite,bridgeSite})
            {
                if(site is null)continue;
                var world=TerrainDestructionOpen(site.Info);var map=world.Map;var target=map.TerrainTargetById(site.TargetId)!;
                var view=battlefield!;var tag=target.Kind.ToString().ToLowerInvariant();terrainDestructionReport["phase"]=tag+"-setup";
                var original=map.OriginalCellAt(site.X,site.Y)!;var originalTile=original.TileIndex;var originalOverlay=original.OverlayData;
                var navigationBefore=map.NavigationVersion;var walkableBefore=map.Walkable(site.X,site.Y);
                var firingCells=TerrainDestructionFiringCells(map,site.X,site.Y).Take(4).ToArray();var attackers=new List<Unit>();
                foreach(var cell in firingCells)
                {
                    var unit=world.SpawnUnit(0,UnitType.V3Launcher,cell.X,cell.Y,Country.Russia);
                    TerrainDestructionRequire(world.CanOccupyUnitPosition(unit,unit.X,unit.Y),"Fixture attacker has no original-map support");attackers.Add(unit);
                }
                Unit? deckTank=null;Unit? underShip=null;var shipHealth=0;BridgeSpan? span=null;var pathBefore=new List<int>();
                Cell? survivorCell=null;
                if(target.Kind==TerrainTargetKind.Bridge)
                {
                    span=map.BridgeSpans.First(candidate=>candidate.Id==target.BridgeId);
                    TerrainDestructionRequire(TerrainDestructionBridgeRoute(map,target,out pathBefore),"Selected official banks lost their intact shortest route through the water-spanning deck");
                    var deck=map.OriginalDefinition!.RuntimeCells
                        .Where(cell=>cell.Bridge && cell.Playable && map.BridgeIds[map.Index(cell.X,cell.Y)]==target.BridgeId && map.Walkable(cell.X,cell.Y))
                        .OrderByDescending(cell=>cell.HighBridge && map.Navigable(cell.X,cell.Y,MovementDomain.Water))
                        .ThenByDescending(cell=>(cell.X-site.X)*(cell.X-site.X)+(cell.Y-site.Y)*(cell.Y-site.Y)).FirstOrDefault();
                    if(deck is not null)
                    {
                        survivorCell=new(deck.X,deck.Y);deckTank=world.SpawnUnit(0,UnitType.Grizzly,deck.X,deck.Y,Country.America);
                        SetInspectionUnit(deckTank,new(deck.X+.5f,deck.Y+.5f),new(1,0));
                        TerrainDestructionRequire(world.CanOccupyUnitPosition(deckTank,deckTank.X,deckTank.Y),"Deck tank is not physically supported");
                        if(deck.HighBridge && map.Navigable(deck.X,deck.Y,MovementDomain.Water))
                        {
                            underShip=world.SpawnUnit(0,UnitType.Destroyer,deck.X,deck.Y,Country.America);
                            SetInspectionUnit(underShip,new(deck.X+.5f,deck.Y+.5f),new(1,0));shipHealth=underShip.Health;
                            TerrainDestructionRequire(world.CanOccupyUnitPosition(underShip,underShip.X,underShip.Y),"Underpass ship has no physical water support");
                        }
                    }
                    if(deckTank is null)missing.Add("No actual supported tank position on chosen official bridge");
                    if(underShip is null)missing.Add("No navigable high-bridge water underpass for the ship-below control");
                }
                view.Selection.Clear();foreach(var unit in attackers)view.Selection.Add(unit.Id);inspectionUnit=attackers[0].Id;
                var level=(float)map.SurfaceHeightAt(site.X+.5,site.Y+.5);view.SetCameraCell(new Vector2(site.X+.5f,site.Y+.5f)-Vector2.One*(level*.5f));
                RevealTerrainInspection();RefreshHud();await PointerParityFrame();
                captures.Add(await TerrainDestructionCapture(folder,tag+"-before",world,target,site.X,site.Y));
                if(survivorCell is {} beforeControl)
                {
                    var camera=view.CameraCell;var support=(float)map.SurfaceHeightAt(beforeControl.X+.5,beforeControl.Y+.5);
                    view.SetCameraCell(new Vector2(beforeControl.X+.5f,beforeControl.Y+.5f)-Vector2.One*(support*.5f));
                    var control=await TerrainDestructionCapture(folder,"bridge-underpass-before",world,target,site.X,site.Y);
                    control["deckTank"]=deckTank!.Id;control["deckTankHealth"]=deckTank.Health;control["underShip"]=underShip?.Id??0;
                    control["underShipHealth"]=underShip?.Health??0;control["waterSurfaceLevel"]=map.WaterSurfaceLevel(beforeControl.X,beforeControl.Y);
                    control["deckSurfaceLevel"]=support;captures.Add(control);view.SetCameraCell(camera);await PointerParityFrame();await PointerParityAfterDraw();
                }
                var point=view.ProjectGround(new(site.X+.5f,site.Y+.5f));
                if(target.Kind==TerrainTargetKind.Tree)
                {
                    var item=map.OriginalObjects[target.ObjectIndex];
                    point=Graphics.OriginalMapObjectBounds(item.Source,view.ProjectGround(new(item.X+.5f,item.Y+.5f)),view.Zoom).GetCenter();
                    TerrainDestructionRequire(view.OriginalObjectQueued(target.ObjectIndex),"Source tree was not queued for the before frame");
                }
                TerrainDestructionRequire(view.TryGetTerrainHoverTarget(point,target.Kind==TerrainTargetKind.Bridge,out var picked,out _,out _) && picked==target.Id,"Visible source-art/deck pick did not resolve the real terrain ID");
                var previousAttackMode=view.AttackMode;view.AttackMode=true;softwarePointer=view.GetGlobalTransformWithCanvas()*point;
                TerrainDestructionRequire(OriginalGamePointerKeyAt(softwarePointer)=="attack","In-range tree/bridge target did not show the original attack cursor");
                captures.Add(await TerrainDestructionCapture(folder,tag+"-target-cursor",world,target,site.X,site.Y));
                view.AttackMode=previousAttackMode;checks.Add(tag+"-native-attack-cursor");
                var stateHash=world.StateHash();
                for(var repeat=0;repeat<8;repeat++)_ = view.TryGetTerrainHoverTarget(point,false,out _,out _,out _);
                TerrainDestructionRequire(stateHash==world.StateHash(),"Terrain hover query mutated the world");
                var visibility=(byte[])world.Players[0].Visibility.Clone();Array.Clear(world.Players[0].Visibility);
                try{TerrainDestructionRequire(!view.TryGetTerrainHoverTarget(point,true,out _,out _,out _),"Terrain hover leaked an unexplored/fogged target");}
                finally{Array.Copy(visibility,world.Players[0].Visibility,visibility.Length);}
                TerrainDestructionRequire(attackers.All(unit=>world.CanAttackTerrain(unit,target.Id)),"Fixture weapon cannot attack this terrain target");
                var acceptedBefore=world.AcceptedCommands;var rejectedBefore=world.RejectedCommands;
                if(target.Kind==TerrainTargetKind.Tree)Click(point,MouseButton.Right);
                view.AttackMode=true;Click(point,MouseButton.Left);
                var issued=localPending.Where(command=>command.Type==CommandType.AttackGround && command.Target==target.Id).ToArray();
                TerrainDestructionRequire(issued.Length==(target.Kind==TerrainTargetKind.Tree?2:1),"Actual battlefield clicks did not enqueue terrain attacks through Main.Issue");
                var damageEvents=0;var resolvedDamage=0;var weaponEvents=0;var projectileIds=new HashSet<int>();var projectileCompleted=false;
                var beforeHealth=target.Health;var steps=0;var flightCaptured=false;var tankAliveAtLastSample=deckTank?.Alive??false;var tankLostWithCollapse=false;
                for(;steps<640 && target.Alive;steps++)
                {
                    var wasAlive=target.Alive;Step(world);
                    foreach(var shot in world.ClassicProjectiles.Where(projectile=>attackers.Any(unit=>unit.Id==projectile.SourceId)))projectileIds.Add(shot.Id);
                    if(projectileIds.Count>0 && projectileIds.Any(id=>world.ClassicProjectiles.All(projectile=>projectile.Id!=id || !projectile.Alive)))projectileCompleted=true;
                    foreach(var hit in world.Events.Where(hit=>hit.Target==target.Id && hit.Damage>0)){damageEvents++;resolvedDamage+=hit.Damage;}
                    weaponEvents+=world.ClassicWeaponEvents.Count(weapon=>weapon.Tick==world.Tick-1 && attackers.Any(unit=>unit.Id==weapon.Source));
                    if(wasAlive && !target.Alive && deckTank is not null)tankLostWithCollapse=tankAliveAtLastSample && !deckTank.Alive;
                    tankAliveAtLastSample=deckTank?.Alive??false;
                    if(!flightCaptured && projectileIds.Count>0)
                    {captures.Add(await TerrainDestructionCapture(folder,tag+"-projectile",world,target,site.X,site.Y));flightCaptured=true;}
                    if(steps%8==0)await PointerParityFrame();
                }
                TerrainDestructionRequire(!target.Alive && target.Health<beforeHealth && damageEvents>0 && resolvedDamage>0,"Real projectile cycles did not destroy "+tag+" within the bounded steps");
                TerrainDestructionRequire(projectileIds.Count>0 && projectileCompleted && weaponEvents>0,"No completed actual weapon/projectile cycle observed for "+tag);
                TerrainDestructionRequire(world.AcceptedCommands>=acceptedBefore+issued.Length && world.RejectedCommands==rejectedBefore,"Issued terrain command was rejected");
                TerrainDestructionRequire(map.NavigationVersion>navigationBefore,"Terrain destruction did not invalidate authoritative navigation");
                TerrainDestructionRequire(ReferenceEquals(original,map.OriginalCellAt(site.X,site.Y)) && original.TileIndex==originalTile && original.OverlayData==originalOverlay,
                    "Terrain destruction rewrote immutable source cell data");
                Issue(new GameCommand{Type=CommandType.Stop,Actors=attackers.Select(unit=>unit.Id).ToArray()});Step(world);
                // A first impact can finish a tree before the rest of the volley.
                // Clear only already-launched ordnance before spawning a move probe.
                var drain=0;
                for(;drain<64 && world.ClassicProjectiles.Any(projectile=>projectile.Alive && attackers.Any(unit=>unit.Id==projectile.SourceId));drain++)
                {Step(world);if(drain%8==0)await PointerParityFrame();}
                TerrainDestructionRequire(!world.ClassicProjectiles.Any(projectile=>projectile.Alive && attackers.Any(unit=>unit.Id==projectile.SourceId)),"Fixture left in-flight ordnance before movement controls");
                captures.Add(await TerrainDestructionCapture(folder,tag+"-after",world,target,site.X,site.Y));
                var result=new Godot.Collections.Dictionary
                {
                    ["kind"]=tag,["mapId"]=site.Info.Id,["targetId"]=target.Id,["beforeHealth"]=beforeHealth,["afterHealth"]=target.Health,
                    ["steps"]=steps,["issuedCommands"]=issued.Length,["acceptedCommands"]=world.AcceptedCommands-acceptedBefore,["damageEvents"]=damageEvents,
                    ["resolvedDamage"]=resolvedDamage,["weaponEvents"]=weaponEvents,["projectileIds"]=new Godot.Collections.Array<int>(projectileIds.Order()),
                    ["projectileCycleCompleted"]=projectileCompleted,["navigationBefore"]=navigationBefore,["navigationAfter"]=map.NavigationVersion,
                    ["walkableBefore"]=walkableBefore,["walkableAfter"]=map.Walkable(site.X,site.Y),["sourceCellPreserved"]=true,["visual"]=view.GetTerrainVisualAudit()
                };cases.Add(result);
                if(target.Kind==TerrainTargetKind.Tree)
                {
                    TerrainDestructionRequire(map.IsOriginalObjectDestroyed(target.ObjectIndex) && !view.OriginalObjectQueued(target.ObjectIndex),"Destroyed tree remained in source-object depth/foreground queue");
                    var cleared=new Cell(site.X,site.Y);
                    var probeCells=(site.TreeControl is {} control?new[]{control}:Array.Empty<Cell>())
                        .Where(cell=>map.CanTraverseTerrain(cell,cleared) && world.NearestFree(cell.X,cell.Y,true)==cell).ToArray();
                    if(map.Walkable(site.X,site.Y) && probeCells.Length>0)
                    {
                        var probe=world.SpawnUnit(0,UnitType.Grizzly,probeCells[0].X,probeCells[0].Y,Country.America);var sx=probe.X;var sy=probe.Y;var searches=world.PathSearches;
                        Issue(new GameCommand{Type=CommandType.Move,Actors=[probe.Id],X=site.X,Y=site.Y});
                        for(var move=0;move<160 && (probe.CellX!=site.X || probe.CellY!=site.Y);move++)
                        {Step(world);if(move%8==0)await PointerParityFrame();}
                        var reached=probe.CellX==site.X && probe.CellY==site.Y;
                        result["movement"]=new Godot.Collections.Dictionary{["reachedClearedTreeCell"]=reached,["moved"]=sx!=probe.X || sy!=probe.Y,["pathSearches"]=world.PathSearches-searches};
                        TerrainDestructionRequire(reached && (sx!=probe.X || sy!=probe.Y) && world.PathSearches>searches && world.CanOccupyUnitPosition(probe,probe.X,probe.Y),"Real Move did not reach the cleared tree footprint");
                        captures.Add(await TerrainDestructionCapture(folder,tag+"-movement",world,target,site.X,site.Y));
                    }
                    else missing.Add("Cleared tree footprint has no free traversable adjacent control (or another original object still blocks it); actual movement control unavailable");
                }
                else
                {
                    TerrainDestructionRequire(map.IsBridgeDestroyed(target.BridgeId) && !map.IsBridgeCellIntact(site.X,site.Y),"Bridge target death left an authoritative intact deck");
                    TerrainDestructionRequire(!view.BridgeVisualQueued(target.BridgeId),"Destroyed bridge retained intact TMP/overlay selection");
                    TerrainDestructionRequire(view.GetTerrainVisualAudit()["missingUnderlyingSourceTiles"].AsInt32()==0,"A destroyed bridge cell has no actual underlying source TMP");
                    if(deckTank is not null)TerrainDestructionRequire(!deckTank.Alive && tankLostWithCollapse,"Deck tank did not die with the actual collapse (or died before collapse)");
                    if(underShip is not null)TerrainDestructionRequire(underShip.Alive && underShip.Health==shipHealth && world.CanOccupyUnitPosition(underShip,underShip.X,underShip.Y),"Ship below was not spared on its real water support");
                    var s=span!.Value;var pathAfter=new GridPathfinder(map).Find(s.BankA.X,s.BankA.Y,s.BankB.X,s.BankB.Y);
                    var beforeUsesDeck=pathBefore.Any(index=>map.BridgeIds[index]==target.BridgeId);
                    var afterUsesDeck=pathAfter.Any(index=>map.BridgeIds[index]==target.BridgeId);
                    TerrainDestructionRequire(!afterUsesDeck,"Post-collapse land route still traverses destroyed bridge cells");
                    if(!beforeUsesDeck)missing.Add("Official bank-to-bank shortest route did not cross the selected intact deck; nav invalidation still verified");
                    result["bridgeNavigation"]=new Godot.Collections.Dictionary{["beforePathCells"]=pathBefore.Count,["afterPathCells"]=pathAfter.Count,["beforeUsesDeck"]=beforeUsesDeck,["afterUsesDeck"]=afterUsesDeck};
                    result["deckTank"]=deckTank?.Id??0;result["deckTankKilledWithCollapse"]=tankLostWithCollapse;
                    result["underShip"]=underShip?.Id??0;result["underShipSpared"]=underShip is {Alive:true} && underShip.Health==shipHealth;
                    result["underpassCell"]=survivorCell is {} cell?new Vector2(cell.X,cell.Y):Vector2.Zero;
                    if(survivorCell is {} afterControl)
                    {
                        var camera=view.CameraCell;var support=(float)map.SurfaceHeightAt(afterControl.X+.5,afterControl.Y+.5);
                        view.SetCameraCell(new Vector2(afterControl.X+.5f,afterControl.Y+.5f)-Vector2.One*(support*.5f));
                        var control=await TerrainDestructionCapture(folder,"bridge-underpass-after",world,target,site.X,site.Y);
                        control["deckTank"]=deckTank!.Id;control["deckTankAlive"]=deckTank.Alive;control["underShip"]=underShip?.Id??0;
                        control["underShipHealth"]=underShip?.Health??0;control["waterSurfaceLevel"]=map.WaterSurfaceLevel(afterControl.X,afterControl.Y);
                        control["collapsedSurfaceLevel"]=support;captures.Add(control);view.SetCameraCell(camera);await PointerParityFrame();await PointerParityAfterDraw();
                    }
                    notes.Add("Exact collapsed bridge art is not proven by retained source state metadata; after capture audits identify every original underlying-terrain TMP used");
                }
                // Exercise the existing Ctrl+left input route on an actual visible
                // empty cell, with an authenticated zero-target command.
                view.Selection.Clear();view.Selection.Add(attackers[0].Id);
                var empty=new[]{new Cell(site.X,site.Y)}.Concat(firingCells).FirstOrDefault(cell=>map.TerrainTargetAt(cell.X,cell.Y) is null
                    && new Rect2(Vector2.Zero,view.Size).HasPoint(view.ProjectGround(new(cell.X+.5f,cell.Y+.5f))));
                var emptyPoint=view.ProjectGround(new(empty.X+.5f,empty.Y+.5f));
                if(new Rect2(Vector2.Zero,view.Size).HasPoint(emptyPoint) && view.TryGetTerrainHoverTarget(emptyPoint,true,out var emptyId,out _,out _) && emptyId==0)
                {
                    var accepted=world.AcceptedCommands;Click(emptyPoint,MouseButton.Left,true);
                    TerrainDestructionRequire(localPending.Any(command=>command.Type==CommandType.AttackGround && command.Target==0),"Ctrl+left did not issue the empty-cell force-fire command");
                    Step(world);TerrainDestructionRequire(world.AcceptedCommands==accepted+1,"Empty-cell force-fire command was rejected");
                    Issue(new GameCommand{Type=CommandType.Stop,Actors=[attackers[0].Id]});Step(world);result["ctrlEmptyCellAccepted"]=true;
                }
                else{result["ctrlEmptyCellAccepted"]=false;missing.Add(tag+": no visible empty-cell pointer control in the bounded fixture camera");}
                checks.Add(tag+": actual source-art/deck pick, read-only/fog guards, Main.Issue, weapon/projectile impact, map HP death, navigation invalidation, and before/flight/after native frames");
            }
        }
        catch(Exception exception){error=exception.ToString();GD.PushError("Terrain destruction checks: "+error);}
        finally
        {
            terrainDestructionReport["phase"]="returning-to-deployment";
            try
            {
                clientPreferences.EdgeScrolling=savedEdges;fast=savedFast;ReturnToDeployment();await PointerParityFrame();await PointerParityFrame();
                TerrainDestructionRequire(Battle is null && !terrainInspection && !terrainInspectionMotion,"Terrain destruction fixture left a frozen battle open");
                terrainDestructionReport["returnedToDeployment"]=true;
            }
            catch(Exception cleanup){error+=(error.Length==0?"":"\n")+"Cleanup: "+cleanup;terrainDestructionReport["returnedToDeployment"]=false;}
            terrainDestructionReport["running"]=false;terrainDestructionReport["completed"]=true;terrainDestructionReport["phase"]="complete";
            terrainDestructionReport["error"]=error;terrainDestructionReport["worldSteps"]=totalSteps;terrainDestructionReport["elapsedSeconds"]=clock.Elapsed.TotalSeconds;
            terrainDestructionReport["captureCount"]=captures.Count;terrainDestructionReport["passedAvailableChecks"]=error.Length==0;
            terrainDestructionReport["coverageComplete"]=missing.Count==0;terrainDestructionReport["exactBrokenArtAvailable"]=false;
            terrainDestructionReport["passed"]=error.Length==0 && missing.Count==0 && cases.Count==2;
            try{if(folder.Length>0)File.WriteAllText(Path.Combine(folder,"acceptance.json"),Godot.Json.Stringify(terrainDestructionReport,"\t"));}
            catch(Exception writeError){terrainDestructionReport["passed"]=false;terrainDestructionReport["reportWriteError"]=writeError.ToString();}
            terrainDestructionChecksRunning=false;
        }
    }
}
