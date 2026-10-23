using Godot;
using RedAlert.Simulation;

namespace RedAlert;

public partial class Main
{
    bool battlePolishChecksRunning;
    public Godot.Collections.Dictionary GetBattlePolishAudit()
    {
        var result=new Godot.Collections.Dictionary
        {
            ["runningChecks"]=battlePolishChecksRunning,["display"]=DisplayServer.GetName(),
            ["window"]=GetWindowInputAudit(),["menu"]=GetFrontMenuLayoutAudit(),
            ["surface"]=GetTerrainInspectionAudit()
        };
        if(battlefield is not null && GodotObject.IsInstanceValid(battlefield))
        {
            result["camera"]=battlefield.CameraCell;result["battlefieldSize"]=battlefield.Size;
            result["buildingDepthConstraints"]=battlefield.BuildingDepthConstraintCount;
            result["buildingDepthFallbacks"]=battlefield.BuildingDepthFallbacks;
            result["originalBridgePieces"]=battlefield.OriginalBridgePiecesDrawn;
            result["originalBridgeForeground"]=battlefield.OriginalBridgeForegroundDraws;
        }
        if(minimap is not null && GodotObject.IsInstanceValid(minimap))
        {
            result["radarBounds"]=minimap.RadarBounds;
            result["radarMap"]=new Godot.Collections.Array<Vector2>(minimap.PlayablePolygon());
            result["radarViewport"]=new Godot.Collections.Array<Vector2>(minimap.ViewportPolygon());
            result["radarDecorativePixels"]=minimap.DecorativePixels;
        }
        return result;
    }
    async Task SaveBattlePolishFrame(string tag)
    {
        var drawn=new TaskCompletionSource();
        void AfterDraw(){RenderingServer.FramePostDraw-=AfterDraw;drawn.TrySetResult();}
        RenderingServer.FramePostDraw+=AfterDraw;await drawn.Task;
        var folder=ProjectSettings.GlobalizePath("res://Reports/battle-polish");System.IO.Directory.CreateDirectory(folder);
        using var image=GetViewport().GetTexture().GetImage();var error=image.SavePng(System.IO.Path.Combine(folder,tag+".png"));
        if(error!=Error.Ok)throw new IOException("Frame capture failed: "+error);
        var audit=GetBattlePolishAudit();audit["frame"]=Engine.GetFramesDrawn();audit["width"]=image.GetWidth();audit["height"]=image.GetHeight();
        System.IO.File.WriteAllText(System.IO.Path.Combine(folder,tag+".json"),Godot.Json.Stringify(audit,"\t"));
    }
    public async void CaptureBattlePolishFrame(string tag)
    {
        try
        {
            if(tag.Length==0 || tag.Any(c=>!char.IsAsciiLetterOrDigit(c) && c!='-'))throw new ArgumentException("Invalid capture name");
            await SaveBattlePolishFrame(tag);
        }
        catch(Exception error){GD.PushError("Battle polish capture: "+error.Message);}
    }
    public Godot.Collections.Dictionary OpenBuildingPolishInspection(string side="front")
    {
        OpenTerrainInspection("flat");var world=localWorld!;
        var building=world.Buildings.First(b=>b.Owner==0 && b.Type==BuildingType.ConstructionYard);
        var tank=world.Units.First(u=>u.Id==inspectionUnit);var d=building.Definition;
        var point=side switch
        {
            "back"=>new Vector2(building.CellLeft+d.Width*.5f,building.CellTop-.5f),
            "east"=>new Vector2(building.CellLeft+d.Width+.5f,building.CellTop+d.Height*.5f),
            _=>new Vector2(building.CellLeft+d.Width*.5f,building.CellTop+d.Height+.5f)
        };
        SetInspectionUnit(tank,point,new Cell(1,0));
        if(!world.CanOccupyUnitPosition(tank,tank.X,tank.Y))throw new InvalidOperationException("Building inspection must start outside its physical foundation");
        battlefield!.SetCameraCell(new Vector2(building.X/(float)Rules.Scale,building.Y/(float)Rules.Scale));battlefield.QueueRedraw();
        var audit=GetBattlePolishAudit();audit["buildingFoundation"]=new Vector2(d.Width,d.Height);
        audit["unitInFront"]=Battlefield.EntityInFrontOfBuilding(building,tank);return audit;
    }
    // A short native-only acceptance pass for the changed UI/camera paths. It
    // warps the real viewport pointer, uses UpdateCamera, and saves GPU frames.
    // The simulation stays frozen; building traversal has its own focused checks.
    void MoveBattlePolishPointer(Vector2 target,MouseButtonMask buttons=0)
    {
        if(!softwareBattlePointer){GetViewport().WarpMouse(target);return;}
        GetViewport().PushInput(new InputEventMouseMotion{Position=GetViewport().GetMousePosition(),GlobalPosition=GetViewport().GetMousePosition(),Relative=target-BattlePointerPosition(),ButtonMask=buttons,WindowId=GetWindow().GetWindowId()},true);
    }
    void ClickBattlePolishPointer(MouseButton button,bool pressed)
    {
        var point=BattlePointerPosition();GetViewport().PushInput(new InputEventMouseButton{Position=point,GlobalPosition=point,ButtonIndex=button,Pressed=pressed,WindowId=GetWindow().GetWindowId()},true);
    }
    public async void RunBattlePolishChecks()
    {
        if(battlePolishChecksRunning)return;battlePolishChecksRunning=true;
        var checks=new Godot.Collections.Array<string>();var diagnostics=new Godot.Collections.Dictionary();var error="";
        void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        try
        {
            Require(!headless,"Native acceptance needs a real game window");
            ReturnToDeployment();await Frame();await Frame();await ToSignal(GetTree().CreateTimer(.4),SceneTreeTimer.SignalName.Timeout);
            var canvas=GetNode<ClassicMenuCanvas>("FrontCanvas");var railCount=0;
            var stage=canvas.Stage??throw new InvalidOperationException("Menu stage is missing");
            foreach(var button in stage.GetChildren().OfType<ClassicMenuButton>().Where(b=>b.RecessedRail))
            {
                var face=new Rect2(button.Position,button.Size);Require(face==button.RailFaceBounds && Math.Abs(face.Size.Y-25)<.01f,"Menu button does not cover its original face");
                var style=button.GetThemeStylebox("normal") as StyleBoxTexture;
                Require(style is not null && style.ExpandMarginTop==0 && style.ExpandMarginBottom==0,"Menu button face still has a negative inset");railCount++;
            }
            Require(railCount>0,"No original menu rail buttons inspected");checks.Add("menu faces cover all 25 source pixels with the background's canvas transform");
            await SaveBattlePolishFrame("menu");
            OpenTerrainInspection("bridge");await Frame();await Frame();var view=battlefield!;var radar=minimap!;
            MoveBattlePolishPointer(view.GetGlobalRect().GetCenter());await Frame();
            var mapCorners=radar.PlayablePolygon();var bounds=radar.RadarBounds;
            var expected=new[]{bounds.Position,new Vector2(bounds.End.X,bounds.Position.Y),bounds.End,new Vector2(bounds.Position.X,bounds.End.Y)};
            for(var i=0;i<4;i++)Require(mapCorners[i].DistanceTo(expected[i])<.01f,"Radar map does not fill the real rectangle");
            var corners=new[]{Vector2.Zero,new Vector2(view.Size.X,0),view.Size,new Vector2(0,view.Size.Y)};var viewport=radar.ViewportPolygon();
            for(var i=0;i<4;i++)Require(radar.RadarToMap(viewport[i]).DistanceTo(view.UnprojectViewPlane(corners[i]))<.001f,"Radar viewport inverse mismatch");
            Require(Math.Abs(viewport[0].Y-viewport[1].Y)<.01f && Math.Abs(viewport[0].X-viewport[3].X)<.01f && Math.Abs(viewport[2].Y-viewport[3].Y)<.01f && Math.Abs(viewport[1].X-viewport[2].X)<.01f,"Radar view frame is not a rectangle");
            Require(radar.DecorativePixels==0,"Radar still adds decorative padding");checks.Add("rectangular map and actual viewport frame share an invertible U/V projection");
            await SaveBattlePolishFrame("bridge-and-radar");
            if(!GetWindow().HasFocus()){GetWindow().GrabFocus();await Frame();await Frame();}
            Require(GetWindow().HasFocus(),"Native game needs focus to verify actual cursor clipping");UpdateWindowInput();
            var safe=BattlePointerRect();MoveBattlePolishPointer(BattleContentRect().Position-Vector2.One*100);await Frame();await Frame();UpdateWindowInput();
            Require(safe.HasPoint(BattlePointerPosition()),"Visible pointer escaped the inset game content rectangle");
            Require(softwareBattlePointer?Input.MouseMode==Input.MouseModeEnum.Captured && battlePointerLayer?.Visible==true:Input.MouseMode==Input.MouseModeEnum.Confined && GetWindow().Unresizable,"Native battle pointer strategy is not active");
            diagnostics["pointerAtOuterCorner"]=GetWindowInputAudit();checks.Add("visible pointer remains inside content; unsupported-warp backend uses actual stationary OS capture and a visible bounded pointer");
            var rect=view.GetGlobalRect();var middle=rect.GetCenter();
            var edges=new[]{(Name:"left",Point:new Vector2(safe.Position.X+.2f,middle.Y),Direction:Vector2.Left),
                (Name:"right",Point:new Vector2(rect.End.X-1,middle.Y),Direction:Vector2.Right),
                (Name:"top",Point:new Vector2(middle.X,safe.Position.Y+.2f),Direction:Vector2.Up),
                (Name:"bottom",Point:new Vector2(middle.X,safe.End.Y-.2f),Direction:Vector2.Down)};
            foreach(var edge in edges)
            {
                view.SetCameraCell(new Vector2(48,48));MoveBattlePolishPointer(edge.Point);await Frame();await Frame();
                var pointer=BattlePointerPosition();Require(BattleEdgeDirection(pointer)==edge.Direction,"Wrong battlefield edge: "+edge.Name);
                var before=view.CameraCell;UpdateCamera(.1);var delta=view.CameraCell-before;
                var screen=new Vector2((delta.X-delta.Y)*30,(delta.X+delta.Y)*15)*view.Zoom;
                Require(screen.Dot(edge.Direction)>1 && Math.Abs(screen.Cross(edge.Direction))<.1f,"Camera did not move toward "+edge.Name);
                diagnostics[edge.Name]=new Godot.Collections.Dictionary{["pointer"]=pointer,["screenDelta"]=screen};
            }
            var sidebar=battleSidebar!.GetGlobalRect().GetCenter();Require(BattleEdgeDirection(sidebar)==Vector2.Zero,"Sidebar triggers battlefield edge scrolling");
            MoveBattlePolishPointer(sidebar);await Frame();var still=view.CameraCell;UpdateCamera(.1);Require(view.CameraCell.DistanceTo(still)<.001f,"Sidebar moves camera");
            checks.Add("all four actual battlefield edges scroll correctly; sidebar and its far-right window edge do not");
            MoveBattlePolishPointer(repairButton!.GetGlobalRect().GetCenter());await Frame();
            var hovered=GetViewport().GuiGetHoveredControl();Require(hovered==repairButton || hovered is not null && repairButton.IsAncestorOf(hovered),"Visible pointer did not update real GUI hover");
            ClickBattlePolishPointer(MouseButton.Left,true);ClickBattlePolishPointer(MouseButton.Left,false);
            Require(view.MaintenanceMode==CommandType.Repair,"Captured pointer did not activate the actual repair button");view.SetMaintenanceMode(null);
            var destination=radar.RadarBounds.Position+radar.RadarBounds.Size*new Vector2(.8f,.5f);MoveBattlePolishPointer(radar.GlobalPosition+destination);
            ClickBattlePolishPointer(MouseButton.Left,true);ClickBattlePolishPointer(MouseButton.Left,false);
            Require(view.CameraCell.DistanceTo(radar.RadarToMap(destination))<.01f && !radar.DraggingViewport,"Captured pointer did not navigate/release the actual radar");
            checks.Add("captured virtual pointer uses real GUI hover, sidebar button actions and radar click navigation");
            MoveBattlePolishPointer(view.GetGlobalRect().GetCenter());ShowMatchMenu();UpdateWindowInput();Require(!WantsBattleMouseConfined && Input.MouseMode==Input.MouseModeEnum.Visible,"Battle menu did not release pointer");HideMatchMenu();UpdateWindowInput();
            checks.Add("battle menu releases and restores cursor confinement");
            OpenBuildingPolishInspection("front");await Frame();await SaveBattlePolishFrame("building-front");
            OpenBuildingPolishInspection("east");await Frame();await SaveBattlePolishFrame("building-east");
            OpenBuildingPolishInspection("back");await Frame();await SaveBattlePolishFrame("building-back");
            checks.Add("native front, east and rear building views captured using the corrected physical foundation");
            OpenTerrainInspection("bridge");MoveBattlePolishPointer(battlefield!.GetGlobalRect().GetCenter());
        }
        catch(Exception exception){error=exception.ToString();GD.PushError("Battle polish checks: "+error);}
        finally
        {
            battlePolishChecksRunning=false;var folder=ProjectSettings.GlobalizePath("res://Reports/battle-polish");System.IO.Directory.CreateDirectory(folder);
            System.IO.File.WriteAllText(System.IO.Path.Combine(folder,"acceptance.json"),Godot.Json.Stringify(new Godot.Collections.Dictionary{["passed"]=error.Length==0,["checks"]=checks,["diagnostics"]=diagnostics,["error"]=error,["native"]=!headless,["oldLongSuitesRun"]=false},"\t"));
        }
    }
}
