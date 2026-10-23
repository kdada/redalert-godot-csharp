using Godot;
using RedAlert.Simulation;
using System.Diagnostics;
using System.Text.Json;

namespace RedAlert;

public partial class Main
{
    // Only the newly requested window, radar, mining and maintenance paths.
    async void RunBattleViewChecks()
    {
        var checks=new List<string>();var error="";var diagnostics=new Dictionary<string,object>();
        void Require(bool value,string text){if(!value)throw new InvalidOperationException(text);}
        async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        async Task Wait(Func<bool> condition,string text)
        {var watch=Stopwatch.StartNew();while(!condition()){if(watch.Elapsed.TotalSeconds>6)throw new TimeoutException(text);await Frame();}}
        void Pointer(Vector2 point,MouseButton button,bool pressed)
        {using var e=new InputEventMouseButton{Position=point,GlobalPosition=point,ButtonIndex=button,Pressed=pressed};GetViewport().PushInput(e,true);}
        void Motion(Vector2 point,Vector2 delta,MouseButtonMask mask=0)
        {using var e=new InputEventMouseMotion{Position=point,GlobalPosition=point,Relative=delta,ButtonMask=mask};GetViewport().PushInput(e,true);}
        async Task KeyPress(Key key)
        {
            using var down=new InputEventKey{Keycode=key,PhysicalKeycode=key,Pressed=true};using var up=new InputEventKey{Keycode=key,PhysicalKeycode=key};
            GetViewport().PushInput(down,true);GetViewport().PushInput(up,true);await Frame();
        }
        void Click(Control target)
        {var point=target.GetGlobalRect().GetCenter();Motion(point,Vector2.Zero);Pointer(point,MouseButton.Left,true);Pointer(point,MouseButton.Left,false);}
        try
        {
            var menuAudit=GetFrontMenuLayoutAudit();Require((bool)menuAudit["titleInsideHeader"],"homepage title backdrop protrudes from original header");
            diagnostics["homeTitleInsideHeader"]=(bool)menuAudit["titleInsideHeader"];
            await Frame();await Frame();
            var window=GetWindowInputAudit();var dummyWindow=(bool)window["headless"];
            // Dummy DisplayServer does not retain native resize flags. Check the
            // configured policy here; inspect the actual window flag when headed.
            Require(dummyWindow?!ProjectSettings.GetSetting("display/window/size/resizable").AsBool():!(bool)window["resizable"],"setup window resize enabled");
            StartPractice(Country.America,MatchSettings.Practice(3));await Wait(()=>Battle is not null,"prepare");await Frame();await Frame();
            var world=Battle!;var view=battlefield!;var radar=minimap!;
            window=GetWindowInputAudit();Require((bool)window["battleActive"] && (dummyWindow || !(bool)window["resizable"]),"battle input/window state");
            if((bool)window["headless"])Require(!(bool)window["desiredConfine"] && (bool)window["visibleCursor"],"headless cursor changed");
            await KeyPress(Key.Escape);Require(MatchMenuOpen && !(bool)GetWindowInputAudit()["desiredConfine"],"menu retained cursor confinement");await KeyPress(Key.Escape);
            var fixedZoom=view.Zoom;var center=view.GetGlobalRect().GetCenter();Motion(center,Vector2.Zero);Pointer(center,MouseButton.WheelUp,true);Pointer(center,MouseButton.WheelUp,false);
            Require(Math.Abs(view.Zoom-fixedZoom)<.0001f,"combat zoom changed");checks.Add("nonresizable setup/battle and cursor policy; Esc release; combat wheel locked");
            view.SetCameraCell(new Vector2(48,48));var frame=radar.ViewportPolygon();
            Require(frame.Length==4 && Math.Abs(frame[0].Y-frame[1].Y)<.001f && Math.Abs(frame[2].Y-frame[3].Y)<.001f,"radar top/bottom are tilted");
            Require(Math.Abs(frame[0].X-frame[3].X)<.001f && Math.Abs(frame[1].X-frame[2].X)<.001f,"radar sides are not rectangular");
            var corners=new[]{Vector2.Zero,new Vector2(view.Size.X,0),view.Size,new Vector2(0,view.Size.Y)};
            for(var i=0;i<4;i++)Require(radar.RadarToMap(frame[i]).DistanceTo(view.UnprojectViewPlane(corners[i]))<.001f,"radar inverse/view corner mismatch");
            var before=view.CameraCell;var start=radar.GlobalPosition+radar.MapToRadar(before);Motion(start,Vector2.Zero);Pointer(start,MouseButton.Left,true);
            Require(radar.DraggingViewport && before.DistanceTo(view.CameraCell)<.001f,"grabbing frame jumps");
            Motion(start+new Vector2(12,0),new Vector2(12,0),MouseButtonMask.Left);var after=view.CameraCell;
            Require(after.X-after.Y>before.X-before.Y,"radar right drag did not move camera right");
            Pointer(new Vector2(20,20),MouseButton.Left,false);Require(!radar.DraggingViewport,"outside release retained drag");
            view.SetCameraCell(new Vector2(-1000,-1000));
            foreach(var c in corners){var p=view.Unproject(c);Require(world.Map.IsPlayablePoint(p.X,p.Y),"camera sees beyond playable rectangle");}
            view.SetCameraCell(new Vector2(1000,1000));
            foreach(var c in corners){var p=view.Unproject(c);Require(world.Map.IsPlayablePoint(p.X,p.Y),"camera far boundary escaped");}
            Require(radar.DecorativePixels==0 && radar.CopyTerrainPixels().Where((_,i)=>i%4==3).All(v=>v==255),"rectangular radar includes decorative padding or missing pixels");
            checks.Add("rectangular map and viewport, terrain/marker/input inverse, rightward drag, outside release, rectangular camera limits without decorative padding");
            var spawn=world.Map.Spawns[0];view.SetCameraCell(new Vector2(spawn.X+5,spawn.Y+5));await Frame();await Frame();view.QueueRedraw();await Frame();
            Require(view.OreTilesDrawn>0 && radar.OrePixels>0,"starting minerals not visible");
            await Wait(()=>view.MiningEffectCount>0,"visible harvesting effect");
            diagnostics["oreTilesDrawn"]=view.OreTilesDrawn;diagnostics["miningEffects"]=view.MiningEffectCount;
            var miner=world.Units.First(u=>u.Owner==0 && u.Definition.Miner);await Wait(()=>miner.Cargo>0,"miner did not collect ore");
            checks.Add("visible starting ore on battlefield/radar and actual harvesting-driven effect/cargo");
            var capacity=productPageCapacity;productPageCapacity=2;productPage=0;RefreshProductPage();
            Require(ProductPageCount>1 && scrollDown is not null && scrollDown.Visible,"multiple catalog pages missing");
            var firstItem=productButtons[0].Button;Click(scrollDown!);Require(productPage==1 && !firstItem.Visible && productButtons.Count(p=>p.Button.Visible)<=2,"footer did not move one full page");
            Require(productionScroll?.ScrollVertical==0,"page retained pixel scroll");Click(scrollUp!);Require(productPage==0 && firstItem.Visible,"footer previous page failed");
            productPageCapacity=capacity;RefreshProductPage();Require((bool)GetSidebarInteractionAudit()["fundsAtTop"],"money not at sidebar top");
            checks.Add("top funds and actual footer buttons move complete catalog pages without pixel scrolling");
            RefreshHud();Require(repairButton is not null && !repairButton.Disabled && sellButton is not null && !sellButton.Disabled,"repair/sell disabled without selection");
            var building=world.Buildings.First(b=>b.Owner==0 && b.Type==BuildingType.PowerPlant);building.Health=Math.Max(1,building.MaxHealth-100);
            Click(repairButton!);Require(view.MaintenanceMode==CommandType.Repair,"repair button did not activate tool");
            var point=view.GlobalPosition+view.ProjectGround(new Vector2(building.X/(float)Rules.Scale,building.Y/(float)Rules.Scale));
            Motion(point,Vector2.Zero);Pointer(point,MouseButton.Left,true);Pointer(point,MouseButton.Left,false);await Wait(()=>building.Repairing,"repair command not applied");
            await KeyPress(Key.Escape);Require(view.MaintenanceMode is null && !MatchMenuOpen,"Esc did not only cancel tool");
            var credits=world.Players[0].Credits;Click(sellButton!);Require(view.MaintenanceMode==CommandType.Sell,"sell button did not activate tool");
            Motion(point,Vector2.Zero);Pointer(point,MouseButton.Left,true);Pointer(point,MouseButton.Left,false);await Wait(()=>world.Find(building.Id) is null,"sell command not applied");
            Require(world.Players[0].Credits>credits,"sell did not refund");view.CancelMaintenance();
            var sidebar=GetSidebarInteractionAudit();diagnostics["sidebar"]=sidebar.ToString();
            checks.Add("actual repair/sell tool buttons, authenticated building commands, refund and first-Esc tool cancellation");
            Issue(new GameCommand{Type=CommandType.Surrender});await Wait(()=>IsSpectating,"spectator");center=view.GetGlobalRect().GetCenter();Motion(center,Vector2.Zero);
            Pointer(center,MouseButton.WheelUp,true);Pointer(center,MouseButton.WheelUp,false);Require(Math.Abs(view.Zoom-fixedZoom)<.0001f,"spectator wheel zoom changed");
            Require(!ViewVisible(0,0) && !ViewExplored(0,0),"spectator reveals exterior");ReturnToDeployment();
            Require(!(bool)GetWindowInputAudit()["battleActive"] && !(bool)GetWindowInputAudit()["desiredConfine"],"return retained confinement");
            checks.Add("spectator scale stays locked; exterior remains nonvisible; return releases cursor");
        }
        catch(Exception exception){error=exception.ToString();GD.PushError(error);}
        var path=System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://"+options.GetValueOrDefault("report","Reports/battle-view-check.json")));
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);System.IO.File.WriteAllText(path,JsonSerializer.Serialize(new{passed=error.Length==0,checks,diagnostics,error,oldRegressionSuitesRun=false},new JsonSerializerOptions{WriteIndented=true}));
        GD.Print($"BATTLE VIEW CHECKS {(error.Length==0?"PASS":"FAIL")}: {checks.Count} groups");GetTree().Quit(error.Length==0?0:1);
    }
}
