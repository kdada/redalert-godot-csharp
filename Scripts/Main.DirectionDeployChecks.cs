using Godot;
using RedAlert.Simulation;
using System.Diagnostics;

namespace RedAlert;

public partial class Main
{
    bool directionDeployChecksRunning;
    Godot.Collections.Dictionary directionDeployReport=new();
    public Godot.Collections.Dictionary GetDirectionDeployAudit()=>directionDeployReport;

    // Focused native operator. Uses real Viewport input/GPU frames, existing
    // classic-start MCVs and normal Move/Deploy commands; never spawns entities.
    public async void RunDirectionDeployChecks()
    {
        if(directionDeployChecksRunning)return;
        directionDeployChecksRunning=true;
        var checks=new Godot.Collections.Array<string>();var captures=new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var diagnostics=new Godot.Collections.Dictionary();var folder="";var error="";
        var savedEdges=clientPreferences.EdgeScrolling;var savedFast=fast;var clock=Stopwatch.StartNew();
        World? world=null;
        directionDeployReport=new(){["running"]=true,["completed"]=false,["passed"]=false,["phase"]="starting",
            ["checks"]=checks,["captures"]=captures,["diagnostics"]=diagnostics,["oldLongSuitesRun"]=false,["directEntitySpawns"]=0};
        void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        async Task Wait(Func<bool> condition,string phase)
        {
            directionDeployReport["phase"]=phase;var start=world?.Tick??0;var timer=Stopwatch.StartNew();
            while(!condition())
            {
                if(timer.Elapsed.TotalSeconds>15 || clock.Elapsed.TotalSeconds>120 || world is not null && world.Tick-start>400)
                    throw new TimeoutException(phase+" tick="+world?.Tick);
                await PointerParityFrame();
            }
        }
        void Point(Vector2 target)
        {
            var point=ClampBattlePointer(target);softwarePointer=point;forwardingBattlePointer=true;
            try{using var motion=new InputEventMouseMotion{Position=point,GlobalPosition=point,WindowId=GetWindow().GetWindowId()};GetViewport().PushInput(motion,true);}
            finally{forwardingBattlePointer=false;}
        }
        void Click(Vector2 target)
        {
            Point(target);var point=BattlePointerPosition();forwardingBattlePointer=true;
            try
            {
                using var down=new InputEventMouseButton{Position=point,GlobalPosition=point,WindowId=GetWindow().GetWindowId(),ButtonIndex=MouseButton.Left,Pressed=true};
                using var up=new InputEventMouseButton{Position=point,GlobalPosition=point,WindowId=GetWindow().GetWindowId(),ButtonIndex=MouseButton.Left};
                GetViewport().PushInput(down,true);GetViewport().PushInput(up,true);
            }
            finally{forwardingBattlePointer=false;}
        }
        void DeployKey()
        {
            battlefield!.GrabFocus();using var down=new InputEventKey{Pressed=true,Keycode=Key.D,PhysicalKeycode=Key.D};
            using var up=new InputEventKey{Keycode=Key.D,PhysicalKeycode=Key.D};GetViewport().PushInput(down,true);GetViewport().PushInput(up,true);
        }
        async Task Capture(string label,string key,int first,int last,Vector2 hotspot)
        {
            directionDeployReport["phase"]=label;var path=Path.Combine(folder,label);Directory.CreateDirectory(path);
            var shot=await PointerParityCapture(path,key,first,last,hotspot);shot["label"]=label;captures.Add(shot);
        }
        try
        {
            Require(!headless && NeedsSoftwareBattlePointer,"Direction/deploy checks require the native original software pointer path");
            Require(!pointerParityChecksRunning,"Original pointer check is already running");
            folder=ProjectSettings.GlobalizePath("res://Reports/direction-deploy-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Time.GetTicksMsec());
            Directory.CreateDirectory(folder);directionDeployReport["folder"]=folder;
            ReturnToDeployment();await PointerParityFrame();
            if(!GetWindow().HasFocus()){GetWindow().GrabFocus();await PointerParityFrame();await PointerParityFrame();}
            Require(GetWindow().HasFocus(),"Native window must have focus");
            clientPreferences.EdgeScrolling=true;fast=false;
            var settings=new MatchSettings(20261018,[new("方向部署检查",Country.America,0),new("对手",Country.Russia,1)],StartingUnits:0,Crates:false);
            StartPractice(Country.America,settings);await Wait(()=>localWorld is not null && battlefield is not null && preparationOverlay is null,"classic-start");
            world=localWorld!;var view=battlefield!;terrainInspection=true;terrainInspectionMotion=false;
            await PointerParityFrame();UpdateWindowInput();
            Require(softwareBattlePointer && OriginalGamePointerVisible,"Original battle overlay did not activate");
            var map=world.Map;var u=(map.PlayableMinU+map.PlayableMaxU)*.5f;var v=(map.PlayableMinV+map.PlayableMaxV)*.5f;
            var cameraCenter=new Vector2((u+v)*.5f,(v-u)*.5f);var safe=BattlePointerRect();var center=safe.GetCenter();
            var edgeCases=new[]{
                (Key:"n",Direction:new Vector2(0,-1),Point:new Vector2(center.X,safe.Position.Y+.1f),Frame:2,Hotspot:new Vector2(27,0)),
                (Key:"ne",Direction:new Vector2(1,-1),Point:new Vector2(safe.End.X-.1f,safe.Position.Y+.1f),Frame:3,Hotspot:new Vector2(54,0)),
                (Key:"e",Direction:new Vector2(1,0),Point:new Vector2(safe.End.X-.1f,center.Y),Frame:4,Hotspot:new Vector2(54,21)),
                (Key:"se",Direction:new Vector2(1,1),Point:new Vector2(safe.End.X-.1f,safe.End.Y-.1f),Frame:5,Hotspot:new Vector2(54,42)),
                (Key:"s",Direction:new Vector2(0,1),Point:new Vector2(center.X,safe.End.Y-.1f),Frame:6,Hotspot:new Vector2(27,42)),
                (Key:"sw",Direction:new Vector2(-1,1),Point:new Vector2(safe.Position.X+.1f,safe.End.Y-.1f),Frame:7,Hotspot:new Vector2(0,42)),
                (Key:"w",Direction:new Vector2(-1,0),Point:new Vector2(safe.Position.X+.1f,center.Y),Frame:8,Hotspot:new Vector2(0,21)),
                (Key:"nw",Direction:new Vector2(-1,-1),Point:new Vector2(safe.Position.X+.1f,safe.Position.Y+.1f),Frame:9,Hotspot:new Vector2(0,0))};
            foreach(var edge in edgeCases)
            {
                view.SetCameraCell(cameraCenter);Point(edge.Point);var before=view.CameraCell;
                Require(view.CanScrollScreen(edge.Direction) && view.CameraCell==before,"Scroll feasibility query moved or blocked center camera: "+edge.Key);
                Require(BattleEdgeDirection(BattlePointerPosition())==edge.Direction,"Wrong outer-client direction: "+edge.Key);
                await Capture("edge-"+edge.Key,"scroll_"+edge.Key,edge.Frame,edge.Frame,edge.Hotspot);
                checks.Add("rendered direction "+edge.Key+": original frame/hotspot and real overlay PNG");
            }
            view.SetCameraCell(cameraCenter);var seam=new Vector2(view.GetGlobalRect().End.X-2,center.Y);Point(seam);
            Require(BattleEdgeDirection(BattlePointerPosition())==Vector2.Zero && OriginalGamePointerKey=="arrow","Interior sidebar seam became a scrolling edge");
            await Capture("sidebar-seam","arrow",0,0,Vector2.Zero);checks.Add("sidebar seam stays arrow; far-right direction was rendered over sidebar");
            var blocked=new Godot.Collections.Array<Godot.Collections.Dictionary>();
            foreach(var edge in edgeCases)
            {
                view.SetCameraCell(cameraCenter+new Vector2(edge.Direction.X+edge.Direction.Y,edge.Direction.Y-edge.Direction.X)*10000);Point(edge.Point);
                var key="scroll_"+edge.Key+"_blocked";Require(!view.CanScrollScreen(edge.Direction) && OriginalGamePointerKey==key,"Missing blocked direction "+edge.Key);
                blocked.Add(new(){["key"]=key,["camera"]=view.CameraCell,["expectedFrame"]=edge.Frame+8});
            }
            diagnostics["blockedQueries"]=blocked;
            view.SetCameraCell(cameraCenter+new Vector2(10000,-10000));Point(edgeCases[2].Point);
            await Capture("right-limit","scroll_e_blocked",12,12,new(54,21));checks.Add("eight blocked camera queries and rendered NoMoveE frame12");
            Point(edgeCases[1].Point);Require(view.CanScrollScreen(new(1,-1)) && OriginalGamePointerKey=="scroll_ne","Partial diagonal clamp incorrectly blocks remaining north motion");
            checks.Add("partial diagonal clamp keeps the feasible direction");

            var mcv=world.Units.Single(unit=>unit.Owner==LocalPlayer && unit.Type==UnitType.Mcv);
            void FocusMcv(){view.SetCameraCell(new(mcv.X/(float)Rules.Scale,mcv.Y/(float)Rules.Scale));Point(view.DeploymentPointerPosition(mcv));}
            FocusMcv();view.Selection.Clear();Require(OriginalGamePointerKey=="arrow","Unselected MCV offers deploy");
            var beforeSelect=world.StateHash();Click(view.DeploymentPointerPosition(mcv));
            Require(view.Selection.Contains(mcv.Id) && localPending.All(command=>command.Type!=CommandType.Deploy) && world.StateHash()==beforeSelect,"First click deployed instead of selecting");
            checks.Add("first ordinary left click only selects the classic-start MCV");
            Require(view.CanDeployMcv(mcv),"Classic-start fixture MCV must have a legal clear foundation");
            var beforeQuery=world.StateHash();var cameraBefore=view.CameraCell;
            for(var i=0;i<20;i++){_ = view.CanDeployMcv(mcv);_ = OriginalGamePointerKeyAt(BattlePointerPosition());}
            Require(world.StateHash()==beforeQuery && view.CameraCell==cameraBefore,"Cursor query mutated simulation or camera");
            diagnostics["validDeploy"]=view.GetDeploymentCursorAudit(BattlePointerPosition());
            await Capture("mcv-deploy","deploy",110,118,new(27,21));checks.Add("selected MCV deploy sequence and repeated read-only legality queries");
            view.SetMaintenanceMode(CommandType.Sell);Require(OriginalGamePointerKey=="sell_invalid","Maintenance did not supersede deploy hover");
            ShowMatchMenu();Require(OriginalGamePointerKey=="arrow" && !softwareBattlePointer,"Menu retained battle/deploy pointer");
            HideMatchMenu();view.SetMaintenanceMode(null);FocusMcv();checks.Add("maintenance and menu pointer priority preserved");

            var home=new Cell(mcv.CellX,mcv.CellY);
            var ore=Enumerable.Range(0,map.Ore.Length).Where(index=>map.Ore[index]>0 && map.Navigable(index%map.Width,index/map.Width,MovementDomain.Land))
                .OrderBy(index=>Math.Abs(index%map.Width-home.X)+Math.Abs(index/map.Width-home.Y)).First();
            var resource=new Cell(ore%map.Width,ore/map.Width);
            terrainInspection=false;Issue(new(){Type=CommandType.Move,Actors=[mcv.Id],X=resource.X,Y=resource.Y});
            await Wait(()=>mcv.Alive && mcv.CellX==resource.X && mcv.CellY==resource.Y && mcv.Order==UnitOrder.Idle,"move-MCV-to-existing-ore");
            terrainInspection=true;FocusMcv();Require(!view.CanDeployMcv(mcv),"Existing ore did not invalidate foundation");
            await Capture("mcv-no-deploy","deploy_invalid",119,119,new(27,21));
            var pending=localPending.Count;Click(view.DeploymentPointerPosition(mcv));
            Require(localPending.Count==pending && view.Selection.Contains(mcv.Id) && mcv.Alive,"Invalid deploy click issued a command or lost selection");
            diagnostics["invalidDeploy"]=view.GetDeploymentCursorAudit(BattlePointerPosition());checks.Add("real Move reaches existing ore; NoDeploy renders and invalid click preserves selection");
            terrainInspection=false;Issue(new(){Type=CommandType.Move,Actors=[mcv.Id],X=home.X,Y=home.Y});
            await Wait(()=>mcv.Alive && mcv.CellX==home.X && mcv.CellY==home.Y && mcv.Order==UnitOrder.Idle,"move-MCV-back-to-clear-ground");
            terrainInspection=true;FocusMcv();Require(view.CanDeployMcv(mcv),"Returned MCV cannot deploy");
            var accepted=world.AcceptedCommands;Click(view.DeploymentPointerPosition(mcv));
            Require(localPending.Count(command=>command.Type==CommandType.Deploy && command.Actors.SequenceEqual(new[]{mcv.Id}))==1,"Second left click did not enqueue exactly one normal Deploy");
            terrainInspection=false;await Wait(()=>world.HasBuilding(LocalPlayer,BuildingType.ConstructionYard),"left-click-MCV-deployment");terrainInspection=true;
            Require(!mcv.Alive && world.AcceptedCommands>accepted,"Left-click deployment did not reach authoritative simulation");
            var yard=world.Buildings.Single(building=>building.Owner==LocalPlayer && building.Type==BuildingType.ConstructionYard && building.Complete);
            view.SetCameraCell(new(yard.CellX,yard.CellY));Point(view.GetGlobalRect().GetCenter());
            await Capture("left-click-yard","arrow",0,0,Vector2.Zero);checks.Add("normal left click produces a completed construction yard through Main.Issue and real stepping");
            view.Selection.Clear();view.Selection.Add(yard.Id);terrainInspection=false;DeployKey();
            await Wait(()=>world.Units.Any(unit=>unit.Owner==LocalPlayer && unit.Type==UnitType.Mcv && unit.Alive),"D-repack-yard");
            var repacked=world.Units.Single(unit=>unit.Owner==LocalPlayer && unit.Type==UnitType.Mcv && unit.Alive);
            view.Selection.Clear();view.Selection.Add(repacked.Id);DeployKey();
            await Wait(()=>world.HasBuilding(LocalPlayer,BuildingType.ConstructionYard),"D-deploy-repacked-MCV");terrainInspection=true;
            checks.Add("existing D shortcut still repacks and deploys via normal commands");
            diagnostics["finalWorld"]=new Godot.Collections.Dictionary{["tick"]=world.Tick,["accepted"]=world.AcceptedCommands,["rejected"]=world.RejectedCommands,["hash"]=world.StateHash().ToString("x16")};
        }
        catch(Exception exception){error=exception.ToString();GD.PushError("Direction/deploy checks: "+error);}
        finally
        {
            try
            {
                clientPreferences.EdgeScrolling=savedEdges;fast=savedFast;ReturnToDeployment();await PointerParityFrame();await PointerParityFrame();
                Require(Battle is null && !terrainInspection && !terrainInspectionMotion && !softwareBattlePointer,"Cursor check left a frozen battle or capture active");
                directionDeployReport["returnedToDeployment"]=true;
            }
            catch(Exception cleanup){error+=(error.Length==0?"":"\n")+"Cleanup: "+cleanup;directionDeployReport["returnedToDeployment"]=false;}
            directionDeployReport["running"]=false;directionDeployReport["completed"]=true;directionDeployReport["phase"]="complete";
            directionDeployReport["error"]=error;directionDeployReport["checkCount"]=checks.Count;directionDeployReport["captureCount"]=captures.Count;
            directionDeployReport["passed"]=error.Length==0 && checks.Count==17 && captures.Count==13;
            try{if(folder.Length>0)File.WriteAllText(Path.Combine(folder,"acceptance.json"),Godot.Json.Stringify(directionDeployReport,"\t"));}
            catch(Exception writeError){directionDeployReport["passed"]=false;directionDeployReport["reportWriteError"]=writeError.ToString();}
            directionDeployChecksRunning=false;
        }
    }
}
