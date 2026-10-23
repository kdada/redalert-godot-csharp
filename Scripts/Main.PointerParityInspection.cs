using Godot;
using RedAlert.Simulation;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace RedAlert;

public partial class Main
{
    bool pointerParityChecksRunning;
    Godot.Collections.Dictionary pointerParityReport=new();
    public Godot.Collections.Dictionary GetPointerParityAudit()=>pointerParityReport;

    static void PointerParityRequire(bool value,string message)
    {if(!value)throw new InvalidOperationException(message);}
    async Task PointerParityFrame(){await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
    async Task PointerParityAfterDraw()
    {
        var drawn=new TaskCompletionSource();
        var timer=GetTree().CreateTimer(8);
        void AfterDraw()=>drawn.TrySetResult();
        void Timeout()=>drawn.TrySetException(new TimeoutException("Pointer frame was not rendered within eight seconds"));
        RenderingServer.FramePostDraw+=AfterDraw;timer.Timeout+=Timeout;
        try{await drawn.Task;}
        finally{RenderingServer.FramePostDraw-=AfterDraw;timer.Timeout-=Timeout;}
    }
    async Task<Godot.Collections.Dictionary> PointerParityCapture(string folder,string key,int firstFrame,int lastFrame,Vector2 hotspot)
    {
        await PointerParityFrame();await PointerParityAfterDraw();
        var library=classicCursorLibrary??throw new InvalidOperationException("Original cursor library is missing");
        PointerParityRequire(OriginalGamePointerVisible && battlePointerLayer?.Visible==true,"Original cursor overlay is not visible: "+key);
        PointerParityRequire(OriginalGamePointerKey==key && library.Key==key,"Drawn cursor does not match the requested state: "+key);
        PointerParityRequire(library.Frame>=firstFrame && library.Frame<=lastFrame,"Cursor frame is outside the original sequence: "+key);
        PointerParityRequire(library.Hotspot.DistanceTo(hotspot)<.001f,"Cursor hotspot is not the original source hotspot: "+key);
        var source=library.Audit();PointerParityRequire(source["sourceFrames"].AsInt32()==450,"Cursor atlas must contain all 450 original frames");
        using var image=GetViewport().GetTexture().GetImage();
        PointerParityRequire(image.GetWidth()>0 && image.GetHeight()>0,"Native viewport capture is empty");
        var png=Path.Combine(folder,key+".png");var saved=image.SavePng(png);
        PointerParityRequire(saved==Error.Ok,"Pointer PNG save failed: "+saved);
        var result=new Godot.Collections.Dictionary
        {
            ["key"]=key,["png"]=png,["width"]=image.GetWidth(),["height"]=image.GetHeight(),
            ["renderedFrame"]=Engine.GetFramesDrawn(),["cursor"]=source,["pointer"]=BattlePointerPosition(),
            ["expectedFirstFrame"]=firstFrame,["expectedLastFrame"]=lastFrame,["expectedHotspot"]=hotspot,
            ["drawTopLeft"]=BattlePointerPosition()-library.Hotspot,["window"]=GetWindowInputAudit()
        };
        File.WriteAllText(Path.Combine(folder,key+".json"),Godot.Json.Stringify(result,"\t"));return result;
    }

    // Native-only, seven focused checks. This deliberately never invokes the old
    // long suites. Raw-count probes are synthetic; GPU captures are real frames.
    public async void RunPointerParityChecks()
    {
        if(pointerParityChecksRunning)return;
        pointerParityChecksRunning=true;
        var checks=new Godot.Collections.Array<string>();var captures=new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var diagnostics=new Godot.Collections.Dictionary();var error="";var folder="";
        Building? fixtureBuilding=null;var savedOwner=0;var savedHealth=0;var savedBuildTicks=0;var savedRepairing=false;
        var menuAccumulation=Input.UseAccumulatedInput;
        pointerParityReport=new Godot.Collections.Dictionary
        {
            ["running"]=true,["completed"]=false,["passed"]=false,["phase"]="starting",["checks"]=checks,["captures"]=captures,
            ["diagnostics"]=diagnostics,["oldLongSuitesRun"]=false,["inputSource"]="synthetic original-unit probes through real Viewport.PushInput"
        };
        try
        {
            PointerParityRequire(!headless && NeedsSoftwareBattlePointer,"Pointer parity needs the native unsupported-warp backend");
            folder=ProjectSettings.GlobalizePath("res://Reports/skirmish-parity/pointer-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Time.GetTicksMsec());
            Directory.CreateDirectory(folder);pointerParityReport["folder"]=folder;
            ReturnToDeployment();await PointerParityFrame();await PointerParityFrame();
            if(!GetWindow().HasFocus()){GetWindow().GrabFocus();await PointerParityFrame();await PointerParityFrame();}
            PointerParityRequire(GetWindow().HasFocus(),"Native game window must have focus");
            var profile=desktopPointerMotion??throw new InvalidOperationException("Desktop mouse profile is missing");
            menuAccumulation=Input.UseAccumulatedInput;

            pointerParityReport["phase"]="menu-desktop-absolute";
            // Exercise the real client-entry handler; no unsupported pointer warp.
            WindowPointerEntered();
            PointerParityRequire(!softwareBattlePointer && softwareMenuPointer && OriginalGamePointerVisible,"Menu must use its original software arrow");
            PointerParityRequire(Input.MouseMode==Input.MouseModeEnum.Hidden,"Menu must hide the free desktop cursor without capture or confinement");
            var nativeMenuPoint=GetViewport().GetMousePosition();var menuReports=profile.GetAudit()["reports"].AsInt64();
            GetViewport().PushInput(new InputEventMouseMotion
            {
                Position=nativeMenuPoint,GlobalPosition=nativeMenuPoint,Relative=new Vector2(83,-37),ScreenRelative=new Vector2(25,9),WindowId=GetWindow().GetWindowId()
            },true);
            PointerParityRequire(BattlePointerPosition().DistanceTo(GetViewport().GetMousePosition())<.001f,"Menu pointer must follow native absolute coordinates");
            PointerParityRequire(profile.GetAudit()["reports"].AsInt64()==menuReports,"Menu motion incorrectly entered the raw-count acceleration profile");
            PointerParityRequire(Input.UseAccumulatedInput==menuAccumulation,"Menu changed the caller's input accumulation setting");
            diagnostics["menu"]=new Godot.Collections.Dictionary{["window"]=GetWindowInputAudit(),["clientEntry"]="real handler invoked; no physical OS pointer warp"};
            checks.Add("menu: Hidden free desktop absolute pointer, no profile gain");

            pointerParityReport["phase"]="captured-native-units";
            OpenBuildingPolishInspection("front");
            PointerParityRequire(localWorld is not null && !localWorld.Settings.ClassicStart,"Building inspection must use LegacyFixture, not the live classic skirmish start");
            softwarePointer=BattlePointerRect().GetCenter();UpdateWindowInput();
            await PointerParityFrame();await PointerParityFrame();
            PointerParityRequire(softwareBattlePointer && WantsBattleMouseConfined && Input.MouseMode==Input.MouseModeEnum.Captured,"Native battle capture did not activate");
            PointerParityRequire(!Input.UseAccumulatedInput,"OS nonlinear profile requires unaccumulated original reports");
            var safe=BattlePointerRect();var center=safe.GetCenter();var final=GetViewport().GetFinalTransform();
            var motions=new Godot.Collections.Array<Godot.Collections.Dictionary>();
            foreach(var raw in new[]{new Vector2(2,1),new Vector2(8,-3),new Vector2(-16,5)})
            {
                softwarePointer=center;
                var expectedScreen=profile.ToScreenPixels(raw)*clientPreferences.PointerSensitivity;
                var logical=final.AffineInverse().BasisXform(expectedScreen);
                PointerParityRequire(safe.HasPoint(center+logical) && ClampBattlePointer(center+logical).DistanceTo(center+logical)<.001f,"Raw-unit probe would hit the pointer clamp");
                GetViewport().PushInput(new InputEventMouseMotion
                {
                    Position=center,GlobalPosition=center,Relative=final.AffineInverse().BasisXform(raw),ScreenRelative=raw,WindowId=GetWindow().GetWindowId()
                },true);
                var actualLogical=softwarePointer-center;var actualScreen=final.BasisXform(actualLogical);
                PointerParityRequire(actualScreen.DistanceTo(expectedScreen)<.02f,"Captured raw counts were scaled or accelerated more than once");
                motions.Add(new Godot.Collections.Dictionary{["rawCounts"]=raw,["expectedScreen"]=expectedScreen,["actualScreen"]=actualScreen,["logicalDelta"]=actualLogical});
            }
            diagnostics["rawMotion"]=new Godot.Collections.Dictionary{["samples"]=motions,["sensitivity"]=clientPreferences.PointerSensitivity,["viewportTransform"]=final,["profile"]=profile.GetAudit()};
            checks.Add("capture: raw counts to OS screen pixels to inverse basis exactly once, unclipped");

            pointerParityReport["phase"]="synthetic-local-bypass";
            softwarePointer=center;var localDelta=new Vector2(17,-11);var reportsBefore=profile.GetAudit()["reports"].AsInt64();
            GetViewport().PushInput(new InputEventMouseMotion
            {
                Position=center,GlobalPosition=center,Relative=localDelta,ScreenRelative=Vector2.Zero,WindowId=GetWindow().GetWindowId()
            },true);
            PointerParityRequire(softwarePointer.DistanceTo(center+localDelta)<.001f,"Local synthetic event incorrectly received physical scaling");
            PointerParityRequire(profile.GetAudit()["reports"].AsInt64()==reportsBefore,"Zero ScreenRelative synthetic event entered the native profile");
            checks.Add("synthetic: zero ScreenRelative preserves the supplied logical delta");

            pointerParityReport["phase"]="focus-release";
            WindowInputFocusExited();
            PointerParityRequire(Input.MouseMode==Input.MouseModeEnum.Visible && !OriginalGamePointerVisible && !pointerChangedAccumulation,"FocusExited handler did not release/hide the software pointer");
            PointerParityRequire(Input.UseAccumulatedInput==menuAccumulation,"FocusExited handler did not restore accumulation");
            diagnostics["focusExit"]=new Godot.Collections.Dictionary{["window"]=GetWindowInputAudit(),["scope"]="real FocusExited handler; no external application focus switch"};
            WindowInputFocusEntered();
            PointerParityRequire(softwareBattlePointer && Input.MouseMode==Input.MouseModeEnum.Captured && !Input.UseAccumulatedInput,"FocusEntered handler did not restore native capture");
            checks.Add("focus: exit handler restores Visible and accumulation; entry restores capture");

            var world=localWorld??throw new InvalidOperationException("Legacy fixture was lost");
            var view=battlefield??throw new InvalidOperationException("Battlefield is missing");
            var building=world.Buildings.FirstOrDefault(b=>b.Owner==LocalPlayer && b.Alive && b.Complete && b.Type==BuildingType.ConstructionYard)
                ??throw new InvalidOperationException("Legacy fixture needs an owned, complete construction yard");
            fixtureBuilding=building;savedOwner=building.Owner;savedHealth=building.Health;savedBuildTicks=building.RemainingBuildTicks;savedRepairing=building.Repairing;
            var cell=new Vector2(building.CellLeft+building.Definition.Width*.5f,building.CellTop+building.Definition.Height*.5f);
            softwarePointer=center;view.SetCameraCell(cell);view.SetMaintenanceMode(null);await PointerParityFrame();
            Vector2 GroundPointer()=>view.GetGlobalTransformWithCanvas()*view.ProjectGround(cell);
            void PointAtBuilding()
            {
                var point=GroundPointer();PointerParityRequire(BattlePointerRect().HasPoint(point) && view.GetGlobalRect().HasPoint(point),"Building footprint center is outside the safe rendered view");
                softwarePointer=point;view.QueueRedraw();
            }
            building.RemainingBuildTicks=0;building.Repairing=false;PointAtBuilding();
            diagnostics["building"]=new Godot.Collections.Dictionary{["id"]=building.Id,["owner"]=building.Owner,["localPlayer"]=LocalPlayer,["groundCell"]=cell,["complete"]=building.Complete};

            pointerParityReport["phase"]="arrow-frame";
            captures.Add(await PointerParityCapture(folder,"arrow",0,0,Vector2.Zero));
            checks.Add("arrow: original frame 0, hotspot (0,0), native viewport PNG");

            pointerParityReport["phase"]="repair-frames";
            building.Health=Math.Max(1,building.MaxHealth/2);view.SetMaintenanceMode(CommandType.Repair);PointAtBuilding();
            PointerParityRequire(building.Owner==LocalPlayer && building.Alive && building.Complete && building.Health<building.MaxHealth && OriginalGamePointerKey=="repair","Owned damaged complete building did not select valid repair");
            captures.Add(await PointerParityCapture(folder,"repair",150,169,new Vector2(27,21)));
            building.Health=building.MaxHealth;PointAtBuilding();
            PointerParityRequire(OriginalGamePointerKey=="repair_invalid","Undamaged building must select invalid repair");
            captures.Add(await PointerParityCapture(folder,"repair_invalid",190,190,new Vector2(27,21)));
            building.Health=Math.Max(1,building.MaxHealth/2);building.RemainingBuildTicks=1;
            PointerParityRequire(OriginalGamePointerKey=="repair_invalid","Incomplete building must not select valid repair");
            building.RemainingBuildTicks=0;
            checks.Add("repair: owned/damaged/complete predicate, frames 150-169 or 190, hotspot (27,21), two PNGs");

            pointerParityReport["phase"]="sell-frames";
            building.Health=building.MaxHealth;view.SetMaintenanceMode(CommandType.Sell);PointAtBuilding();
            PointerParityRequire(OriginalGamePointerKey=="sell","Owned building did not select valid sell");
            captures.Add(await PointerParityCapture(folder,"sell",129,138,new Vector2(27,21)));
            building.Owner=world.Players.First(p=>p.Id!=LocalPlayer).Id;PointAtBuilding();
            PointerParityRequire(OriginalGamePointerKey=="sell_invalid","Enemy building must select invalid sell");
            captures.Add(await PointerParityCapture(folder,"sell_invalid",149,149,new Vector2(27,21)));
            building.Health=Math.Max(1,building.MaxHealth/2);view.SetMaintenanceMode(CommandType.Repair);
            PointerParityRequire(OriginalGamePointerKey=="repair_invalid","Enemy damaged complete building must select invalid repair");
            checks.Add("sell: ownership predicate, frames 129-138 or 149, hotspot (27,21), two PNGs");
        }
        catch(Exception exception){error=exception.ToString();GD.PushError("Pointer parity checks: "+error);}
        finally
        {
            pointerParityReport["phase"]="returning-to-menu";
            try
            {
                if(fixtureBuilding is not null){fixtureBuilding.Owner=savedOwner;fixtureBuilding.Health=savedHealth;fixtureBuilding.RemainingBuildTicks=savedBuildTicks;fixtureBuilding.Repairing=savedRepairing;}
                ReturnToDeployment();await PointerParityFrame();await PointerParityFrame();
                pointerInClient=GetViewport().GetVisibleRect().HasPoint(GetViewport().GetMousePosition());UpdateWindowInput();
                PointerParityRequire(Battle is null && !terrainInspection && !terrainInspectionMotion && !softwareBattlePointer,"Pointer checks left a frozen battle inspection open");
                PointerParityRequire(Input.MouseMode is Input.MouseModeEnum.Hidden or Input.MouseModeEnum.Visible,"Ordinary menu retained capture or confinement");
                PointerParityRequire(Input.UseAccumulatedInput==menuAccumulation,"Final menu did not restore input accumulation");
                diagnostics["finalMenu"]=GetWindowInputAudit();pointerParityReport["returnedToDeployment"]=true;
            }
            catch(Exception cleanupError){error+=(error.Length==0?"":"\n")+"Menu cleanup: "+cleanupError;pointerParityReport["returnedToDeployment"]=false;}
            pointerParityReport["running"]=false;pointerParityReport["completed"]=true;pointerParityReport["phase"]="complete";
            pointerParityReport["error"]=error;pointerParityReport["passed"]=error.Length==0 && checks.Count==7 && captures.Count==5;
            pointerParityReport["checkCount"]=checks.Count;pointerParityReport["captureCount"]=captures.Count;
            try{if(folder.Length>0)File.WriteAllText(Path.Combine(folder,"acceptance.json"),Godot.Json.Stringify(pointerParityReport,"\t"));}
            catch(Exception writeError){pointerParityReport["passed"]=false;pointerParityReport["reportWriteError"]=writeError.ToString();GD.PushError("Pointer parity report: "+writeError.Message);}
            pointerParityChecksRunning=false;
        }
    }
}
