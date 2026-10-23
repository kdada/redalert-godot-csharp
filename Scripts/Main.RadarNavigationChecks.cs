using Godot;
using RedAlert.Simulation;

namespace RedAlert;

public partial class Main
{
    Godot.Collections.Dictionary radarNavigationReport=new();
    public Godot.Collections.Dictionary GetRadarNavigationAudit()=>radarNavigationReport;
    // Native window geometry, focus, GUI input and actual camera movement are
    // intentional requirements; dummy/headless window edges are not a substitute.
    public async void RunRadarNavigationChecks()
    {
        if(radarNavigationReport.TryGetValue("running",out var running) && running.AsBool())return;
        var checks=new Godot.Collections.Array<string>();var folder=ProjectSettings.GlobalizePath("res://Reports/radar-navigation-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));Directory.CreateDirectory(folder);
        radarNavigationReport=new(){{"running",true},{"passed",false},{"native",!headless},{"checks",checks},{"folder",folder}};
        void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        void Move(Vector2 point,MouseButtonMask mask=0)
        {
            if(softwareBattlePointer)softwarePointer=ClampBattlePointer(point);
            forwardingBattlePointer=true;
            try{using var motion=new InputEventMouseMotion{Position=point,GlobalPosition=point,ButtonMask=mask};GetViewport().PushInput(motion,true);}
            finally{forwardingBattlePointer=false;}
        }
        void Button(Vector2 point,bool pressed)
        {
            forwardingBattlePointer=true;
            try{using var button=new InputEventMouseButton{Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left,Pressed=pressed};GetViewport().PushInput(button,true);}
            finally{forwardingBattlePointer=false;}
        }
        try
        {
            Require(!headless,"Radar navigation checks require a focused native game window");
            OpenClassicInspection("mp02t2","city",0);await PointerParityFrame();await PointerParityFrame();
            if(!headless && !GetWindow().HasFocus()){GetWindow().GrabFocus();await PointerParityFrame();}
            var view=battlefield!;var radar=minimap!;var content=BattleContentRect();var seam=view.GetGlobalRect().End.X;var y=content.GetCenter().Y;var pointerBounds=BattlePointerRect();
            Require(BattleEdgeDirection(new(seam-2,y))==Vector2.Zero && BattleEdgeDirection(new(seam+2,y))==Vector2.Zero,"Sidebar seam still scrolls");
            Require(BattleEdgeDirection(new((seam+content.End.X)/2,y))==Vector2.Zero,"Sidebar center scrolls");
            var right=new Vector2(pointerBounds.End.X-.1f,y);Require(BattleEdgeDirection(right)==Vector2.Right,"Outer right edge is unreachable");
            Require(BattleEdgeDirection(content.End+Vector2.One)==Vector2.Zero,"Outside client scrolls");
            Require(BattleEdgeDirection(pointerBounds.Position+new Vector2(.1f,.1f))==new Vector2(-1,-1),"Client corner scroll direction");
            checks.Add("sidebar seam and center stay still; reachable outer right/corner edges; outside stays still");
            view.SetCameraCell(radar.RadarToMap(radar.RadarBounds.GetCenter()));var camera=view.CameraCell;
            if(!headless)
            {
                Require(GetWindow().HasFocus(),"Native window has no focus");Move(new(seam-2,y));UpdateCamera(.1);Require(view.CameraCell.DistanceTo(camera)<.0001f,"Real camera pans at sidebar seam");
                Move(right);UpdateCamera(.1);Require(view.CameraCell.X-view.CameraCell.Y>camera.X-camera.Y,"Real camera fails to pan at outer edge");
                radarNavigationReport["actualEdgePanVerified"]=true;
            }
            Move(content.GetCenter());view.SetCameraCell(camera);
            var p=radar.MapToRadar(camera);var x=radar.MapToRadar(camera+Vector2.Right)-p;var z=radar.MapToRadar(camera+Vector2.Down)-p;
            Require(Math.Abs(x.Length()-z.Length())<.0001f && Math.Abs(x.Dot(z))<.0001f,"Radar stretches orthogonal ground axes");
            Require(x.X>0 && x.Y>0 && z.X<0 && z.Y>0,"Radar compass rotated or mirrored");
            checks.Add("radar preserves compass and equal-length perpendicular ground axes");
            var frame=radar.ViewportPolygon();Require(frame.Length==4 && Math.Abs(frame[0].Y-frame[1].Y)<.0001f && Math.Abs(frame[0].X-frame[3].X)<.0001f,"Radar viewport is not rectangular");
            var ratio=(frame[1].X-frame[0].X)/(frame[3].Y-frame[0].Y);Require(Math.Abs(ratio-view.Size.X/view.Size.Y/2)<.0001f,"Viewport kept battlefield vertical compression");
            foreach(var point in new[]{camera,camera+new Vector2(3,-2),camera+new Vector2(-4,3)})Require(radar.RadarToMap(radar.MapToRadar(point)).DistanceTo(point)<.0001f,"Radar marker/input inverse mismatch");
            checks.Add("white viewport uses uncompressed ground projection and marker/input inverse");
            var target=camera+new Vector2(24,0);var click=radar.GlobalPosition+radar.MapToRadar(target);Move(click);Button(click,true);Button(click,false);
            Require(view.CameraCell.DistanceTo(target)<.01f,"Actual radar click misses projected target");
            var grab=radar.GlobalPosition+radar.MapToRadar(view.CameraCell);Move(grab);Button(grab,true);Require(radar.DraggingViewport,"Actual radar drag did not begin");
            var before=view.CameraCell;Move(grab+new Vector2(8,0),MouseButtonMask.Left);Require(view.CameraCell.X-view.CameraCell.Y>before.X-before.Y,"Actual radar drag pans wrong compass direction");
            Button(content.GetCenter(),false);Require(!radar.DraggingViewport,"Outside radar release retained drag");Move(content.GetCenter());
            checks.Add("real GUI radar click, viewport drag and outside release");
            radarNavigationReport["radarBounds"]=radar.RadarBounds;radarNavigationReport["viewportRatio"]=ratio;radarNavigationReport["groundXAxis"]=x;radarNavigationReport["groundYAxis"]=z;
            if(!headless)
            {
                await PointerParityAfterDraw();using var image=GetViewport().GetTexture().GetImage();var png=Path.Combine(folder,"native-radar.png");Require(image.SavePng(png)==Error.Ok,"Native capture failed");radarNavigationReport["png"]=png;
            }
            radarNavigationReport["passed"]=true;
        }
        catch(Exception error){radarNavigationReport["error"]=error.ToString();GD.PushError("Radar navigation checks: "+error.Message);}
        finally
        {
            radarNavigationReport["running"]=false;radarNavigationReport["completed"]=true;
            File.WriteAllText(Path.Combine(folder,"report.json"),Godot.Json.Stringify(radarNavigationReport,"\t"));
            if(options.ContainsKey("radar-navigation-check"))GetTree().Quit(radarNavigationReport["passed"].AsBool()?0:1);
        }
    }
}
