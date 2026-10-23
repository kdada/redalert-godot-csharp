using Godot;
using RedAlert.Simulation;

namespace RedAlert;

public partial class Battlefield
{
    bool deploymentPressConsumed;
    static bool DeploymentModifierHeld=>Input.IsKeyPressed(Key.Shift) || Input.IsKeyPressed(Key.Ctrl) || Input.IsKeyPressed(Key.Alt);

    // Same screen-to-cell transform and clamp as PanScreen, without moving the camera.
    // At a diagonal edge one axis may still move while the other is clamped.
    public bool CanScrollScreen(Vector2 direction)
    {
        if(Controller.Battle is null || direction==Vector2.Zero)return false;
        var before=ClampCameraCell(CameraCell);var d=direction/Zoom;
        var after=ClampCameraCell(before+new Vector2(d.X/60+d.Y/30,d.Y/30-d.X/60));
        return before.DistanceSquaredTo(after)>1e-10f;
    }
    public string OriginalActionCursor(Vector2 viewportPoint)
    {
        var local=GetGlobalTransformWithCanvas().AffineInverse()*viewportPoint;
        if(!new Rect2(Vector2.Zero,Size).HasPoint(local) || Controller.IsSpectating || Controller.MatchMenuOpen)return "arrow";
        if(Placement is not null || SupportMode || Controller.ChatTyping)return "arrow";
        if(MaintenanceMode is not null)return OriginalMaintenanceCursor(viewportPoint);
        if(dragging || panning)return "arrow";
        if(TryGetTerrainHoverTarget(local,Input.IsKeyPressed(Key.Ctrl),out var target,out var cellX,out var cellY))
        {
            var world=Controller.Battle!;
            return Selection.Select(world.Find).OfType<Unit>().Any(unit=>unit.Alive && unit.Owner==Controller.LocalPlayer
                && (target<0?world.CanAttackTerrain(unit,target):world.CanAttackGround(unit,cellX,cellY))
                && world.TerrainAttackInRange(unit,target,cellX,cellY))?"attack":"attack_out_of_range";
        }
        if(AttackMode)return "arrow";
        var mcv=SelectedMcvAt(local);
        return mcv is null?"arrow":CanDeployMcv(mcv)?"deploy":"deploy_invalid";
    }
    Unit? SelectedMcvAt(Vector2 localPoint)
    {
        var world=Controller.Battle;
        if(world is null || Controller.IsSpectating || Controller.MatchMenuOpen || Controller.ChatTyping || DeploymentModifierHeld
            || MaintenanceMode is not null || Placement is not null || SupportMode || AttackMode
            || !new Rect2(Vector2.Zero,Size).HasPoint(localPoint)
            || Controller.IsBattleScrollPointer(GetGlobalTransformWithCanvas()*localPoint))return null;
        if(Hit(localPoint) is not Unit{Type:UnitType.Mcv} mcv || mcv.Owner!=Controller.LocalPlayer || !Selection.Contains(mcv.Id))return null;
        // Match CommandAt's single-owned-unit actor set; stale/enemy/building
        // selection entries do not become deploy actors.
        foreach(var id in Selection)
            if(id!=mcv.Id && world.Find(id) is Unit other && other.Owner==Controller.LocalPlayer)return null;
        return mcv;
    }
    public Rect2I McvDeploymentFootprint(Unit mcv)
    {
        var definition=Rules.Building(BuildingType.ConstructionYard,mcv.OriginCountry);
        return new Rect2I(mcv.CellX-(definition.Width-1)/2,mcv.CellY-(definition.Height-1)/2,definition.Width,definition.Height);
    }
    public bool CanDeployMcv(Unit mcv)
    {
        var world=Controller.Battle;var player=Controller.LocalPlayer;
        if(world is null || world.Finished || player<0 || player>=world.Players.Length || world.Players[player].Defeated
            || !mcv.Alive || mcv.Type!=UnitType.Mcv || mcv.Owner!=player || mcv.TransportId!=0 || world.Find(mcv.Id)!=mcv
            || world.EntityCount>=Rules.MaxEntities)return false;
        var definition=Rules.Building(BuildingType.ConstructionYard,mcv.OriginCountry);var footprint=McvDeploymentFootprint(mcv);
        if(!world.Map.FootprintFree(definition,footprint.Position.X,footprint.Position.Y))return false;
        // Exact World.BuildingAreaHasUnits AABB semantics. Authoritative deploy
        // temporarily kills its MCV to ignore self; this read-only query skips ID.
        var left=(long)footprint.Position.X*Rules.Scale;var top=(long)footprint.Position.Y*Rules.Scale;
        var right=(long)footprint.End.X*Rules.Scale;var bottom=(long)footprint.End.Y*Rules.Scale;
        foreach(var other in world.Units)
        {
            if(other.Id==mcv.Id || !other.Alive || other.TransportId!=0 || other.ParasiteHostId!=0 || other.Definition.Domain==MovementDomain.Air)continue;
            var radius=World.UnitCollisionRadius(other);
            if((long)other.X+radius>=left && (long)other.X-radius<=right && (long)other.Y+radius>=top && (long)other.Y-radius<=bottom)return false;
        }
        return true;
    }
    bool IssueMcvDeploymentAt(Vector2 localPoint)
    {
        var mcv=SelectedMcvAt(localPoint);if(mcv is null)return false;
        dragging=false;
        if(CanDeployMcv(mcv))Controller.Issue(new GameCommand{Type=CommandType.Deploy,Actors=[mcv.Id]});
        else Controller.Notify("此处无法展开基地车，请移动到空旷平地后重试");
        return true;
    }
    bool HandleDeploymentRelease(InputEvent input)
    {
        if(input is not InputEventMouseButton{ButtonIndex:MouseButton.Left} button)return false;
        if(button.Pressed){deploymentPressConsumed=false;return false;}
        if(!deploymentPressConsumed)return false;
        deploymentPressConsumed=false;dragging=false;AcceptEvent();return true;
    }
    bool HandleDeploymentPress(InputEventMouseButton button)
    {
        if(button.ShiftPressed || button.CtrlPressed || button.AltPressed || !IssueMcvDeploymentAt(button.Position))return false;
        deploymentPressConsumed=true;AcceptEvent();return true;
    }
    public Vector2 DeploymentPointerPosition(Unit mcv)=>GetGlobalTransformWithCanvas()*(UnitActorPoint(mcv)-new Vector2(0,7)*Zoom);
    public Godot.Collections.Dictionary GetDeploymentCursorAudit(Vector2 viewportPoint)
    {
        var local=GetGlobalTransformWithCanvas().AffineInverse()*viewportPoint;var mcv=SelectedMcvAt(local);
        return new(){["key"]=OriginalActionCursor(viewportPoint),["mcvId"]=mcv?.Id??0,["valid"]=mcv is not null && CanDeployMcv(mcv),
            ["footprint"]=mcv is null?new Rect2I():McvDeploymentFootprint(mcv),["selectionCount"]=Selection.Count,["readOnly"]=true};
    }
}
