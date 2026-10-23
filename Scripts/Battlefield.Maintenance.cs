using Godot;
using RedAlert.Simulation;

namespace RedAlert;

public partial class Battlefield
{
    public CommandType? MaintenanceMode { get; private set; }

    public void SetMaintenanceMode(CommandType? mode)
    {
        if(mode is not (CommandType.Repair or CommandType.Sell) || Controller.Battle is null || Controller.IsSpectating || Controller.MatchMenuOpen)mode=null;
        MaintenanceMode=mode;
        if(mode is not null)
        {
            Placement=null;SupportMode=false;AttackMode=false;dragging=false;
            GrabFocus();
        }
        MouseDefaultCursorShape=mode is null?CursorShape.Arrow:CursorShape.Cross;
        Controller.RefreshMaintenanceButtons();QueueRedraw();
    }

    public bool CancelMaintenance()
    {
        if(MaintenanceMode is null)return false;
        SetMaintenanceMode(null);return true;
    }

    // Called before the normal selection/order branches. Commands use the same
    // authenticated controller route as every other player action.
    public bool HandleMaintenanceInput(InputEvent input)
    {
        if(MaintenanceMode is not { } mode)return false;
        if(Controller.IsSpectating || Controller.Battle is null){CancelMaintenance();return false;}
        // Another tool or its keyboard shortcut supersedes maintenance.
        if(Placement is not null || SupportMode || AttackMode){CancelMaintenance();return false;}
        if(input is InputEventKey{Pressed:true,Echo:false,Keycode:Key.Escape})
        {CancelMaintenance();Controller.Notify("已取消维修 / 出售模式");GetViewport().SetInputAsHandled();return true;}
        if(input is not InputEventMouseButton button || button.ButtonIndex is not (MouseButton.Left or MouseButton.Right))return false;
        pointer=button.Position;dragging=false;
        if(button.ButtonIndex==MouseButton.Right)
        {
            if(button.Pressed){CancelMaintenance();Controller.Notify("已取消维修 / 出售模式");}
            AcceptEvent();return true;
        }
        if(button.Pressed)
        {
            GrabFocus();
            var building=MaintenanceBuildingAt(pointer);
            if(building is null)Controller.Notify(mode==CommandType.Repair?"维修模式：左键点击己方建筑；右键 / Esc 取消":"出售模式：左键点击己方建筑；右键 / Esc 取消");
            else
            {
                Controller.Issue(new GameCommand{Type=mode,Actors=[building.Id]});
                Controller.Notify(mode==CommandType.Repair?$"{building.Definition.Name}：{(building.Repairing?"停止维修":"开始维修")}":$"出售 {building.Definition.Name}");
            }
        }
        AcceptEvent();return true;
    }

    public string OriginalMaintenanceCursor(Vector2 viewportPoint)
    {
        if(MaintenanceMode is not { } mode || Controller.MatchMenuOpen)return "arrow";
        var local=GetGlobalTransformWithCanvas().AffineInverse()*viewportPoint;
        if(!new Rect2(Vector2.Zero,Size).HasPoint(local))return "arrow";
        var building=MaintenanceBuildingAt(local);
        var valid=building is {Alive:true} && (mode==CommandType.Sell || building.Complete && building.Health<building.MaxHealth);
        return mode==CommandType.Sell?(valid?"sell":"sell_invalid"):(valid?"repair":"repair_invalid");
    }
    Building? MaintenanceBuildingAt(Vector2 point)
    {
        // Units standing in front of a building must not block the building tool.
        // Prefer its ground footprint, then the same tall artwork hit region used
        // by normal building selection.
        var world=Controller.Battle!;var cell=UnprojectGround(point);
        var own=world.Buildings.Where(b=>b.Alive && b.Owner==Controller.LocalPlayer).Reverse().ToArray();
        var footprint=own.FirstOrDefault(b=>cell.X>=b.CellLeft && cell.Y>=b.CellTop && cell.X<b.CellLeft+b.Definition.Width && cell.Y<b.CellTop+b.Definition.Height);
        if(footprint is not null)return footprint;
        foreach(var b in own)
        {
            var corners=Footprint(b.CellLeft,b.CellTop,b.Definition.Width,b.Definition.Height);
            var left=corners.Min(p=>p.X);var right=corners.Max(p=>p.X);var top=corners.Min(p=>p.Y)-45*Zoom;var bottom=corners.Max(p=>p.Y);
            if(new Rect2(left,top,right-left,bottom-top).HasPoint(point))return b;
        }
        return null;
    }
}
