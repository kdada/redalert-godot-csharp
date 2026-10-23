using Godot;
using RedAlert.Simulation;

namespace RedAlert;

public partial class Main
{
    int uiFrames;
    int uiTank;
    int uiTankX;
    bool uiInputVerified;
    void ExerciseUiInput()
    {
        if(battlefield is null || Battle is null)throw new InvalidOperationException("无战场可测试");
        var tank=Battle.Units.First(u=>u.Owner==0 && u.Type==UnitType.Grizzly);
        uiTank=tank.Id;uiTankX=tank.X;
        var pixel=battlefield.Project(new(tank.X/(float)Rules.Scale,tank.Y/(float)Rules.Scale));
        void Mouse(MouseButton button,bool pressed,Vector2 position)
        {
            using var input=new InputEventMouseButton{ButtonIndex=button,Pressed=pressed,Position=position};battlefield._GuiInput(input);
        }
        Mouse(MouseButton.Left,true,pixel);Mouse(MouseButton.Left,false,pixel);
        if(!battlefield.Selection.Contains(tank.Id))throw new InvalidOperationException("单击选兵失败");
        var destination=new Vector2(27.5f,22.5f);var screen=battlefield.Project(destination);
        if(battlefield.Unproject(screen).DistanceTo(destination)>.001f)throw new InvalidOperationException("等角坐标往返失败");
        Mouse(MouseButton.Right,true,screen);
        using(var group=new InputEventKey{Keycode=(Key)49,CtrlPressed=true,Pressed=true})battlefield._UnhandledKeyInput(group);
        battlefield.Selection.Clear();
        using(var recall=new InputEventKey{Keycode=(Key)49,Pressed=true})battlefield._UnhandledKeyInput(recall);
        if(!battlefield.Selection.Contains(tank.Id))throw new InvalidOperationException("编组选择失败");
        var barracks=Battle.Buildings.First(b=>b.Owner==0 && b.Type==BuildingType.Barracks);
        battlefield.Selection.Clear();battlefield.Selection.Add(barracks.Id);Train(UnitType.Gi);
        battlefield.Placement=BuildingType.WarFactory;
        Mouse(MouseButton.Left,true,battlefield.Project(new(17.5f,9.5f)));
        if(battlefield.Placement is not null)throw new InvalidOperationException("建筑放置模式未清除");
        battlefield.AttackMode=true;Mouse(MouseButton.Right,true,screen);
        if(battlefield.AttackMode)throw new InvalidOperationException("右键取消模式失败");
        uiInputVerified=true;GD.Print("HEADLESS INPUT VERIFIED selection / move / control group / train / construction / cancel");
    }
    void VerifyUiOutcome()
    {
        if(!uiInputVerified || Battle is null)throw new InvalidOperationException("界面输入测试未执行");
        if(Battle.Find(uiTank) is not Unit tank || tank.X==uiTankX)throw new InvalidOperationException("界面移动命令未生效");
        if(!Battle.HasBuilding(0,BuildingType.WarFactory))throw new InvalidOperationException("界面建造命令未完成");
        if(Battle.Units.Count(u=>u.Owner==0 && u.Type==UnitType.Gi)<4)throw new InvalidOperationException("界面生产命令未完成");
    }
}
