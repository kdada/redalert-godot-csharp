using Godot;
using RedAlert.Simulation;

namespace RedAlert;

public partial class Main
{
    enum FrontPage { Main, SinglePlayer, Skirmish, Settings, Keyboard, NetworkSettings, Network, CreateNetwork, JoinNetwork, NetworkRoom, Customize, Movies, Unavailable, Exit }
    ClientPreferences clientPreferences=new();
    ClientPreferences? settingsDraft;
    FrontPage frontPage,customizeReturn=FrontPage.Skirmish;
    readonly List<Label[]> lobbyRows=[];
    readonly List<Control> lobbyIdentity=[];
    Label? frontHint;
    string unavailableText="";
    bool waitingShortcut;
    int selectedShortcut;
    Key pendingShortcut=Key.None;
    static readonly Color MenuGold=new("ffff00"),MenuDim=new("9e9a00"),MenuRed=new("b00000");
    public bool ShowOrderLines=>clientPreferences.TargetLines;
    public bool ShowHiddenUnits=>clientPreferences.SeeHiddenObjects;
    public int VisualDetail=>clientPreferences.VisualDetails;
    public Key AttackShortcut=>clientPreferences.AttackKey;
    public Key StopShortcut=>clientPreferences.StopKey;

    void ShowFrontPage(FrontPage page)
    {
        frontPage=page;ClearUi();lobbyRows.Clear();lobbyIdentity.Clear();frontHint=null;roomList=null;playersList=null;chatLog=null;waitingShortcut=false;
        if(page is not (FrontPage.Network or FrontPage.CreateNetwork or FrontPage.JoinNetwork or FrontPage.NetworkRoom))StopLanDiscovery();
        ApplyUiTheme();ConfigureMenuTheme();
        var mainShell=page is FrontPage.Main or FrontPage.SinglePlayer or FrontPage.Exit;
        var surface=Full(new ClassicMenuCanvas{Name="FrontCanvas",Background=Graphics.MenuImage(mainShell?"background":"dialog-background"),Header=mainShell?Graphics.MenuImage("main-header"):null});AddChild(surface);
        var stage=new Control{Name="MenuStage",MouseFilter=MouseFilterEnum.Ignore};surface.SetStage(stage);
        if(mainShell)AddMenuMovie(stage);
        switch(page)
        {
            case FrontPage.Main:BuildMainMenu(stage);break;
            case FrontPage.SinglePlayer:BuildSinglePlayerMenu(stage);break;
            case FrontPage.Skirmish:BuildSkirmishMenu(stage);break;
            case FrontPage.Settings:BuildSettingsMenu(stage);break;
            case FrontPage.Keyboard:BuildKeyboardMenu(stage);break;
            case FrontPage.NetworkSettings:BuildNetworkOptions(stage);break;
            case FrontPage.Network:BuildNetworkBrowser(stage);break;
            case FrontPage.CreateNetwork:BuildCreateNetwork(stage);break;
            case FrontPage.JoinNetwork:BuildDirectJoin(stage);break;
            case FrontPage.NetworkRoom:BuildNetworkRoom(stage);break;
            case FrontPage.Customize:BuildCustomizeMenu(stage);break;
            case FrontPage.Movies:BuildMoviesMenu(stage);break;
            case FrontPage.Unavailable:BuildUnavailable(stage);break;
            case FrontPage.Exit:BuildExitMenu(stage);break;
        }
        frontHint??=MenuText(stage,"",14,574,607,20,11,MenuDim);
        surface.PlayEntry();
    }
    public Godot.Collections.Dictionary GetFrontMenuLayoutAudit()
    {
        var bounds=new Godot.Collections.Dictionary();var disabled=new Godot.Collections.Dictionary();
        var railFaces=new Godot.Collections.Dictionary();var railHousings=new Godot.Collections.Dictionary();var transformedButtons=new Godot.Collections.Dictionary();
        var canvas=GetNodeOrNull<ClassicMenuCanvas>("FrontCanvas");
        if(canvas?.Stage is {} stage)foreach(var child in stage.GetChildren())if(child is ClassicMenuButton button)
        {
            var name=button.Name.ToString();var face=new Rect2(button.Position,button.Size);
            bounds[name]=face;disabled[name]=button.Disabled;transformedButtons[name]=canvas.ToCanvasBounds(face);
            if(button.RecessedRail){railFaces[name]=button.RailFaceBounds;railHousings[name]=button.RailHousingBounds;}
        }
        return new Godot.Collections.Dictionary{{"headerBounds",ClassicMenuCanvas.HeaderBounds},{"titleBackdropBounds",ClassicMenuCanvas.TitleBackdropBounds},
            {"titleInsideHeader",ClassicMenuCanvas.HeaderBounds.Encloses(ClassicMenuCanvas.TitleBackdropBounds)},{"buttons",bounds},{"disabled",disabled},
            {"railFaces",railFaces},{"railHousings",railHousings},{"transformedButtons",transformedButtons},{"canvasBounds",canvas?.CanvasBounds??new Rect2()}};
    }
    void ConfigureMenuTheme()
    {
        var t=uiTheme!;t.DefaultFontSize=12;
        foreach(var type in new[]{"Label","Button","OptionButton","LineEdit","PopupMenu","ItemList","TextEdit","CheckBox"})t.SetColor("font_color",type,MenuGold);
        foreach(var type in new[]{"Button","OptionButton"})
        {
            foreach(var (state,index) in new[]{("normal",0),("hover",1),("pressed",2),("hover_pressed",2),("disabled",0)})
            {
                var texture=Graphics.MenuButtonFrame(index);if(texture is null)continue;
                using var box=new StyleBoxTexture{Texture=texture,ModulateColor=state=="disabled"?new Color(.28f,.28f,.28f):Colors.White};box.SetTextureMarginAll(2);box.SetContentMarginAll(2);t.SetStylebox(state,type,box);
            }
            using var focus=Style(Colors.Transparent,new Color("ff7900"),1,2,2);t.SetStylebox("focus",type,focus);
            t.SetColor("font_disabled_color",type,new Color("706000"));t.SetColor("font_hover_color",type,MenuGold);t.SetColor("font_pressed_color",type,new Color("4a0000"));
        }
        using var well=Style(new Color(.015f,0,0,1),new Color("a00000"),1,3,1);
        foreach(var state in new[]{"normal","hover","pressed","hover_pressed","disabled"})t.SetStylebox(state,"OptionButton",well);
        if(Graphics.MenuImage("Pcx/dnarrowr") is {} arrow)t.SetIcon("arrow","OptionButton",arrow);
        t.SetConstant("arrow_margin","OptionButton",1);t.SetConstant("h_separation","OptionButton",4);
        foreach(var type in new[]{"LineEdit","TextEdit"}){t.SetStylebox("normal",type,well);t.SetStylebox("read_only",type,well);t.SetStylebox("focus",type,well);}
        t.SetStylebox("panel","ItemList",well);t.SetStylebox("panel","PopupMenu",well);t.SetStylebox("panel","PopupPanel",well);
        using var selected=Style(new Color("e60000"),new Color("e60000"),0,3,1);t.SetStylebox("selected","ItemList",selected);t.SetStylebox("selected_focus","ItemList",selected);t.SetStylebox("hover","PopupMenu",selected);
        t.SetColor("font_selected_color","ItemList",MenuGold);t.SetColor("font_selected_color","PopupMenu",MenuGold);t.SetColor("font_uneditable_color","LineEdit",MenuDim);
        using var empty=new StyleBoxEmpty();t.SetStylebox("normal","CheckBox",empty);t.SetStylebox("hover","CheckBox",empty);t.SetStylebox("pressed","CheckBox",empty);t.SetStylebox("hover_pressed","CheckBox",empty);t.SetStylebox("disabled","CheckBox",empty);t.SetStylebox("focus","CheckBox",empty);
        if(Graphics.MenuImage("Pcx/cce_i") is {} on){t.SetIcon("checked","CheckBox",on);t.SetIcon("checked_disabled","CheckBox",on);}
        if(Graphics.MenuImage("Pcx/cue_i") is {} off){t.SetIcon("unchecked","CheckBox",off);t.SetIcon("unchecked_disabled","CheckBox",off);}
        foreach(var state in new[]{"font_pressed_color","font_hover_color","font_hover_pressed_color","font_focus_color"})t.SetColor(state,"CheckBox",MenuGold);
        t.SetColor("font_disabled_color","CheckBox",MenuDim);
        using var track=Style(new Color(.02f,0,0,.25f),new Color("990000"),1,0,11);t.SetStylebox("slider","HSlider",track);t.SetStylebox("grabber_area","HSlider",empty);t.SetStylebox("grabber_area_highlight","HSlider",empty);
        if(Graphics.MenuImage("Pcx/trakgrip") is {} grip){t.SetIcon("grabber","HSlider",grip);t.SetIcon("grabber_highlight","HSlider",grip);t.SetIcon("grabber_disabled","HSlider",grip);}
    }
    void AddMenuMovie(Control stage)
    {
        if(Graphics.MenuImage("poster") is {} poster)Place(stage,new TextureRect{Texture=poster,ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,MouseFilter=MouseFilterEnum.Ignore},0,0,632,570);
        const string path="res://Assets/Generated/AuthenticShell/ra2ts_l/menu-loop.ogv";
        if(!headless && Godot.FileAccess.FileExists(path))
        {
            var stream=new VideoStreamTheora{File=path};
            Place(stage,new VideoStreamPlayer{Stream=stream,Autoplay=true,Loop=true,Expand=true,MouseFilter=MouseFilterEnum.Ignore,VolumeDb=-80},0,0,632,570);
        }
    }
    static T Place<T>(Control parent,T child,float x,float y,float w,float h) where T:Control
    {parent.AddChild(child);child.Position=new(x,y);child.Size=new(w,h);return child;}
    Label MenuText(Control parent,string value,float x,float y,float w,float h,int size=12,Color? color=null)
    {
        var label=Text(value,size,color??MenuGold);label.MouseFilter=MouseFilterEnum.Ignore;label.VerticalAlignment=VerticalAlignment.Center;
        label.AddThemeColorOverride("font_shadow_color",Colors.Black);label.AddThemeConstantOverride("shadow_offset_x",1);label.AddThemeConstantOverride("shadow_offset_y",1);
        return Place(parent,label,x,y,w,h);
    }
    void MenuPanel(Control parent,float x,float y,float w,float h,bool recessed=true)=>Place(parent,new ClassicMenuFrame{Recessed=recessed},x,y,w,h);
    void PageTitle(Control stage,string title,string subtitle="")
    {
        var label=MenuText(stage,title,659,3,114,29,12);label.Name="FrontPageTitle";label.ClipText=true;label.HorizontalAlignment=HorizontalAlignment.Center;
    }
    Button MenuButton(Control parent,string name,string text,float x,float y,float w,Action action,int fontSize=13)
    {
        var button=new ClassicMenuButton{Name=name,Text=text,MouseDefaultCursorShape=CursorShape.PointingHand,FocusMode=FocusModeEnum.All};button.AddThemeFontSizeOverride("font_size",fontSize);button.Pressed+=action;
        return Place(parent,button,x,y,w,25);
    }
    Button RailButton(Control stage,string name,string text,float y,Action action)
    {
        var housingBounds=ClassicMenuCanvas.RailHousingBounds(y);var faceBounds=ClassicMenuCanvas.RailFaceBounds(y);
        if(Graphics.MenuImage("housing") is {} housing)Place(stage,new TextureRect{Name=name+"Housing",Texture=housing,ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,MouseFilter=MouseFilterEnum.Ignore},housingBounds.Position.X,housingBounds.Position.Y,housingBounds.Size.X,housingBounds.Size.Y);
        var button=(ClassicMenuButton)MenuButton(stage,name,text,faceBounds.Position.X,faceBounds.Position.Y,faceBounds.Size.X,action,12);
        button.SetRailBounds(housingBounds,faceBounds);
        foreach(var (state,index) in new[]{("normal",0),("hover",1),("pressed",2),("hover_pressed",2),("disabled",0)})
        {
            var texture=Graphics.MenuRailButtonFrame(index);if(texture is null)continue;
            using var box=new StyleBoxTexture{Texture=texture,ModulateColor=state=="disabled"?new Color(.28f,.28f,.28f):Colors.White};
            // The cropped red face already excludes its SHP rim. Fill the full
            // native 126x25 face so the housing's baked-in caption is covered.
            box.SetTextureMarginAll(0);box.SetContentMarginAll(2);box.SetExpandMarginAll(0);button.AddThemeStyleboxOverride(state,box);
        }
        using var focus=new StyleBoxEmpty();button.AddThemeStyleboxOverride("focus",focus);
        return button;
    }
    LineEdit MenuEdit(Control stage,string name,string value,string placeholder,float x,float y,float w,int max=32)
    {
        var edit=new LineEdit{Name=name,Text=value,PlaceholderText=placeholder,MaxLength=max};edit.AddThemeFontSizeOverride("font_size",12);return Place(stage,edit,x,y,w,23);
    }
    OptionButton CountryPicker(Control parent,float x,float y,float w,string name="CountryChoice",Country? selected=null)
    {
        var choice=new OptionButton{Name=name};choice.AddThemeFontSizeOverride("font_size",12);foreach(var c in Enum.GetValues<Country>())choice.AddItem(Rules.CountryName(c),(int)c);
        choice.Select(choice.GetItemIndex((int)(selected??clientPreferences.Country)));return Place(parent,choice,x,y,w,25);
    }
    void BuildMainMenu(Control stage)
    {
        PageTitle(stage,"主选单");
        RailButton(stage,"MenuSingle","单人游戏",198,()=>ShowFrontPage(FrontPage.SinglePlayer));
        RailButton(stage,"MenuInternet","互联网",240,()=>Unavailable("Westwood 在线服务当前不可用。\n请选择“局域网”进行对战。"));
        RailButton(stage,"MenuNetwork","局域网",282,()=>ShowFrontPage(FrontPage.Network));
        RailButton(stage,"MenuMovies","影片与制作人员",324,()=>ShowFrontPage(FrontPage.Movies));
        RailButton(stage,"MenuOptions","选项",366,()=>{settingsDraft=clientPreferences.Clone();ShowFrontPage(FrontPage.Settings);});
        RailButton(stage,"MenuQuit","退出游戏",536,()=>ShowFrontPage(FrontPage.Exit));
    }
    void BuildSinglePlayerMenu(Control stage)
    {
        PageTitle(stage,"单人游戏");
        var campaign=RailButton(stage,"NewCampaign","新战役",198,()=>{});campaign.Disabled=true;campaign.TooltipText="没有可用的战役任务";
        var load=RailButton(stage,"LoadGame","载入游戏",240,()=>{});load.Disabled=true;load.TooltipText="没有可用的战役存档";
        RailButton(stage,"MenuSkirmish","遭遇战",282,()=>ShowFrontPage(FrontPage.Skirmish));
        RailButton(stage,"SingleBack","主选单",536,()=>ShowFrontPage(FrontPage.Main));
    }
    void Unavailable(string reason){unavailableText=reason;ShowFrontPage(FrontPage.Unavailable);}
    void BuildUnavailable(Control stage)
    {
        PageTitle(stage,"信息");MenuPanel(stage,91,160,450,200,false);
        var label=MenuText(stage,unavailableText,114,181,400,130,14);label.AutowrapMode=TextServer.AutowrapMode.WordSmart;
        RailButton(stage,"InfoBack","主选单",536,()=>ShowFrontPage(FrontPage.Main));
    }
    void BuildExitMenu(Control stage)
    {
        PageTitle(stage,"退出游戏");MenuPanel(stage,160,230,318,126,false);var label=MenuText(stage,"确定要退出游戏？",180,243,278,30,16);label.HorizontalAlignment=HorizontalAlignment.Center;
        MenuButton(stage,"ConfirmQuit","是",185,303,110,()=>GetTree().Quit());MenuButton(stage,"CancelQuit","否",340,303,110,()=>ShowFrontPage(FrontPage.Main));
    }
    void BuildMoviesMenu(Control stage)
    {
        PageTitle(stage,"影片与制作人员");MenuText(stage,"没有已解锁的战役影片。",100,120,440,30,15);
        MenuText(stage,"RED ALERT 2\n原版图像：Westwood Studios / Electronic Arts\n\n独立 Godot + C# 实现\n网络对战、游戏逻辑与界面代码为独立编写。",100,190,440,150,13);
        RailButton(stage,"MoviesBack","主选单",536,()=>ShowFrontPage(FrontPage.Main));
    }
    public override void _UnhandledKeyInput(InputEvent input)
    {
        if(battlefield is not null || input is not InputEventKey{Pressed:true,Echo:false} key)return;
        if(frontPage==FrontPage.Keyboard && waitingShortcut && key.Keycode!=Key.Escape){CaptureShortcut(key.Keycode);GetViewport().SetInputAsHandled();return;}
        if(key.Keycode!=Key.Escape)return;
        switch(frontPage)
        {
            case FrontPage.Main:ShowFrontPage(FrontPage.Exit);break;
            case FrontPage.Settings:SaveSettingsAndReturn();break;
            case FrontPage.Keyboard:case FrontPage.NetworkSettings:ShowFrontPage(FrontPage.Settings);break;
            case FrontPage.Skirmish:ShowFrontPage(FrontPage.SinglePlayer);break;
            case FrontPage.CreateNetwork:case FrontPage.JoinNetwork:ShowFrontPage(FrontPage.Network);break;
            case FrontPage.NetworkRoom:LeaveNetworkRoom();break;
            case FrontPage.Customize:ShowFrontPage(customizeReturn);break;
            default:ShowFrontPage(FrontPage.Main);break;
        }
        GetViewport().SetInputAsHandled();
    }
}
