using Godot;
using RedAlert.Simulation;

namespace RedAlert;

public partial class Main
{
    Task<World>? preparingLocalWorld;
    FrontPage preparationPage;
    Control? preparationOverlay;
    Control? matchMenu;
    Label? matchMenuTitle;
    Button? surrenderMenuButton;
    Button? stopMenuButton,cancelBuildingMenuButton;
    World? observedMatch;
    bool observedDefeat, observedEnd;
    public bool IsSpectating => Battle is { } world && (LocalPlayer < 0 || world.Players[LocalPlayer].Defeated || world.Finished);
    public bool MatchMenuOpen => matchMenu?.Visible == true;
    public bool ViewVisible(int x, int y) => Battle is { } world && world.Map.IsPlayable(x,y) && (IsSpectating || world.Visible(LocalPlayer,x,y));
    public bool ViewExplored(int x, int y) => Battle is { } world && world.Map.IsPlayable(x,y) && (IsSpectating || world.Explored(LocalPlayer,x,y));
    void BeginLocalPreparation(MatchSettings settings)
    {
        if (preparingLocalWorld is not null) return;
        SetBattleWindowInput(false);
        settings.Validate(); var snapshot = settings with { Players = settings.Players.ToArray() };
        preparationPage = frontPage; preparingLocalWorld = Task.Run(() => new World(snapshot));
        preparationOverlay = Full(new ColorRect { Color = new Color(0,0,0,.8f), MouseFilter = MouseFilterEnum.Stop }); AddChild(preparationOverlay);
        var center = Full(new CenterContainer()); preparationOverlay.AddChild(center);
        center.AddChild(Text("正在生成战场…",20,Gold));
    }
    void CompleteLocalPreparation()
    {
        if (preparingLocalWorld is not { } task) return;
        if (frontPage != preparationPage) { CancelLocalPreparation(); return; }
        if (!task.IsCompleted) return;
        preparingLocalWorld = null; RemovePreparationOverlay();
        try
        {
            var world = task.GetAwaiter().GetResult(); if (!StartExternalAi(world)) return;
            network?.Dispose(); network = null; localPending.Clear(); localReplay.Clear(); localWorld = world; accumulator = 0;
            ShowBattle();
        }
        catch (Exception error) { SetFrontNotice("无法生成战场：" + error.Message); }
    }
    void RemovePreparationOverlay()
    {
        if (preparationOverlay is not null && GodotObject.IsInstanceValid(preparationOverlay))
        { if(preparationOverlay.GetParent() is { } parent) parent.RemoveChild(preparationOverlay); preparationOverlay.QueueFree(); }
        preparationOverlay = null;
    }
    void CancelLocalPreparation()
    {
        if (preparingLocalWorld is { } task) _ = task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        preparingLocalWorld = null; RemovePreparationOverlay();
    }
    void ResetMatchMenuUi()
    {
        matchMenu=null;matchMenuTitle=null;surrenderMenuButton=null;stopMenuButton=null;cancelBuildingMenuButton=null;
    }
    void BuildMatchMenu()
    {
        matchMenu=Full(new Control{Name="MatchPauseMenu",Visible=false,MouseFilter=MouseFilterEnum.Stop});AddChild(matchMenu);
        matchMenu.AddChild(Full(new ColorRect{Color=new Color(0,0,0,.42f),MouseFilter=MouseFilterEnum.Stop}));
        var surface=Full(new BattleMenuSurface{Name="MatchOriginalDialog",Artwork=Graphics.MenuImage(sovietUi?"Shp/pudlgbgs/atlas":"Shp/pudlgbga/atlas")});
        matchMenu.AddChild(surface);
        var stage=new Control{Name="MatchDialogStage",MouseFilter=MouseFilterEnum.Stop};surface.SetStage(stage);
        matchMenuTitle=Text("作战菜单",18,new Color("ffff00"));matchMenuTitle.Name="MatchMenuTitle";
        matchMenuTitle.HorizontalAlignment=HorizontalAlignment.Center;matchMenuTitle.MouseFilter=MouseFilterEnum.Ignore;
        matchMenuTitle.AddThemeColorOverride("font_shadow_color",Colors.Black);
        matchMenuTitle.AddThemeConstantOverride("shadow_offset_x",1);matchMenuTitle.AddThemeConstantOverride("shadow_offset_y",1);
        Place(stage,matchMenuTitle,32,25,387,27);
        Button MenuAction(string name,string title,int column,int row,Action action)
        {
            var button=BattleMenuSurface.NativeButton(Graphics,name,title,action);
            return Place(stage,button,column==0?65:260,99+row*39,126,25);
        }
        MenuAction("ResumeMatch","返回战场",0,0,HideMatchMenu);
        MenuAction("SaveMatchReplay","保存录像",1,0,SaveReplay);
        surrenderMenuButton=MenuAction("SurrenderMatch","投降",0,1,()=>{HideMatchMenu();Issue(new GameCommand{Type=CommandType.Surrender});});
        MenuAction("EndMatch","结束游戏",1,1,ReturnToDeployment);
        supportButton=MenuAction("SupportMatch","空降伞兵",0,2,()=>
        {
            HideMatchMenu();if(battlefield is null || IsSpectating)return;
            battlefield.CancelMaintenance();battlefield.SupportMode=true;battlefield.Placement=null;Notify("左键选择可见地面投放伞兵");
        });
        stopMenuButton=MenuAction("StopMatchSelection","停止所选单位",1,2,()=>{HideMatchMenu();StopSelection();});
        cancelQueueButton=MenuAction("CancelMatchQueue","取消生产队列",0,3,()=>{HideMatchMenu();BuildingAction(CommandType.CancelQueue);});
        cancelQueueButton.TooltipText="先选择兵营或战车工厂，再取消其队列最后一项";
        cancelBuildingMenuButton=MenuAction("CancelMatchBuilding","取消建筑准备",1,3,()=>{HideMatchMenu();Issue(new GameCommand{Type=CommandType.CancelBuilding});});
        cancelBuildingMenuButton.TooltipText="取消正在准备的建筑并全额退款";
    }
    public void ShowMatchMenu()
    {
        if (Battle is null || matchMenu is null) return;
        if(ChatTyping)CloseBattleChat(); battleOptions?.Hide();
        matchMenuTitle!.Text = Battle.Finished ? Battle.WinningTeam < 0 ? "作战结束 · 平局" : LocalPlayer >= 0 && Battle.WinningTeam == Battle.Players[LocalPlayer].Setup.Team ? "作战胜利" : "作战结束" : IsSpectating ? "全局观战" : "作战菜单";
        surrenderMenuButton!.Visible = !IsSpectating;
        RefreshHud();matchMenu.Show();UpdateWindowInput();
    }
    public void HideMatchMenu() { matchMenu?.Hide(); battlefield?.GrabFocus();UpdateWindowInput(); }
    void ObserveBattleLifecycle()
    {
        if (Battle is not { } world) return;
        if (!ReferenceEquals(observedMatch,world)) { observedMatch = world; observedDefeat = false; observedEnd = false; }
        if (LocalPlayer >= 0 && world.Players[LocalPlayer].Defeated && !observedDefeat)
        {
            observedDefeat = true; battlefield?.Selection.Clear();
            if (battlefield is not null) { battlefield.Placement = null; battlefield.AttackMode = false; battlefield.SupportMode = false; }
            Notify("作战失败 · 已进入全局观战，按 Esc 打开菜单");
        }
        if (surrenderMenuButton is not null) surrenderMenuButton.Visible = !IsSpectating;
        if (world.Finished && !observedEnd)
        {
            observedEnd=true;
            if(battlefield is not null){battlefield.Placement=null;battlefield.AttackMode=false;battlefield.SupportMode=false;}
            ShowMatchMenu();
        }
    }
}
