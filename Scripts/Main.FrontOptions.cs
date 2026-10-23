using Godot;

namespace RedAlert;

public partial class Main
{
    HSlider MenuSlider(Control stage,string name,float x,float y,float width,double min,double max,double step,double value,Action<double> changed)
    {
        var slider=new HSlider{Name=name,MinValue=min,MaxValue=max,Step=step,Value=value,FocusMode=FocusModeEnum.All};slider.ValueChanged+=value=>changed(value);return Place(stage,slider,x,y,width,24);
    }
    CheckBox MenuCheck(Control stage,string name,string caption,float x,float y,bool value,Action<bool> changed)
    {
        var check=new CheckBox{Name=name,Text=caption,ButtonPressed=value,FocusMode=FocusModeEnum.All};check.AddThemeFontSizeOverride("font_size",12);check.Toggled+=value=>changed(value);return Place(stage,check,x,y,240,23);
    }
    void BuildSettingsMenu(Control stage)
    {
        settingsDraft??=clientPreferences.Clone();var draft=settingsDraft;PageTitle(stage,"选项");
        MenuText(stage,"显示选项",94,73,225,24,13);
        MenuText(stage,"视觉效果",116,103,160,23);var detailLabel=MenuText(stage,new[]{"低","中","高"}[draft.VisualDetails],273,103,48,23);
        MenuSlider(stage,"SettingDetails",116,132,183,0,2,1,draft.VisualDetails,n=>{draft.VisualDetails=(int)n;detailLabel.Text=new[]{"低","中","高"}[(int)n];});
        MenuText(stage,"设置游戏分辨率",334,103,220,23);
        var resolution=new OptionButton{Name="SettingResolution"};resolution.AddThemeFontSizeOverride("font_size",12);
        var sizes=new[]{new Vector2I(640,480),new Vector2I(800,600),new Vector2I(1024,768),new Vector2I(1280,960),new Vector2I(1440,900)};
        for(var i=0;i<sizes.Length;i++)resolution.AddItem($"{sizes[i].X} × {sizes[i].Y}",i);
        resolution.AddItem("全屏 / 当前分辨率",99);var current=Array.FindIndex(sizes,s=>s.X==draft.ResolutionWidth && s.Y==draft.ResolutionHeight);resolution.Select(draft.Fullscreen?5:Math.Max(0,current));
        resolution.ItemSelected+=index=>{var id=resolution.GetItemId((int)index);draft.Fullscreen=id==99;if(id<99){draft.ResolutionWidth=sizes[id].X;draft.ResolutionHeight=sizes[id].Y;}};Place(stage,resolution,334,132,183,25);
        MenuText(stage,"游戏选项",94,179,225,24,13);
        MenuText(stage,"难度",116,208,150,23);var difficulty=MenuText(stage,new[]{"简单","普通","困难"}[draft.AIDifficulty],273,208,65,23);
        MenuSlider(stage,"SettingDifficulty",116,237,183,0,2,1,draft.AIDifficulty,n=>{draft.AIDifficulty=(int)n;difficulty.Text=new[]{"简单","普通","困难"}[(int)n];});
        MenuText(stage,"界面选项",94,284,225,24,13);
        MenuCheck(stage,"SettingTooltips","工具提示",116,318,draft.Tooltips,on=>draft.Tooltips=on);
        MenuCheck(stage,"SettingTargetLines","目标线",116,343,draft.TargetLines,on=>draft.TargetLines=on);
        MenuCheck(stage,"SettingHiddenObjects","显示隐藏物体",116,368,draft.SeeHiddenObjects,on=>draft.SeeHiddenObjects=on);
        MenuText(stage,"卷屏速度",334,313,150,24);var speedLabel=MenuText(stage,$"{draft.CameraSpeed:0.##}",475,313,70,24);
        MenuSlider(stage,"SettingCameraSpeed",334,343,183,.5,2,.25,draft.CameraSpeed,n=>{draft.CameraSpeed=(float)n;speedLabel.Text=n.ToString("0.##");});
        MenuText(stage,"鼠标速度",334,369,140,24);var pointerSpeedLabel=MenuText(stage,$"{draft.PointerSensitivity:0.00}×",475,369,70,24);
        MenuSlider(stage,"SettingPointerSpeed",334,394,183,.5,2,.05,draft.PointerSensitivity,n=>{draft.PointerSensitivity=(float)n;pointerSpeedLabel.Text=$"{n:0.00}×";});
        MenuText(stage,"音频选项",94,405,225,24,13);
        foreach(var (caption,name,x,initial,change) in new (string,string,int,int,Action<int>)[]{("音乐音量","SettingMusic",116,draft.MusicVolume,n=>draft.MusicVolume=n),("音效音量","SettingSound",263,draft.SoundVolume,n=>draft.SoundVolume=n),("语音音量","SettingVoice",410,draft.VoiceVolume,n=>draft.VoiceVolume=n)})
        {
            var title=MenuText(stage,caption,x,438,130,24);title.HorizontalAlignment=HorizontalAlignment.Center;
            var value=MenuText(stage,initial.ToString(),x+85,463,45,24);value.HorizontalAlignment=HorizontalAlignment.Center;
            MenuPanel(stage,x+83,463,47,24);value.MoveToFront();
            MenuSlider(stage,name,x,463,81,0,10,1,initial,n=>{change((int)n);value.Text=((int)n).ToString();});
        }
        RailButton(stage,"OptionsKeyboard","键盘",198,()=>ShowFrontPage(FrontPage.Keyboard));
        RailButton(stage,"OptionsNetwork","网络",240,()=>ShowFrontPage(FrontPage.NetworkSettings));
        RailButton(stage,"SettingsBack","主选单",536,SaveSettingsAndReturn);
    }
    void SaveSettingsAndReturn()
    {
        if(settingsDraft is not null)
        {
            var previous=clientPreferences;clientPreferences=settingsDraft.Clone();
            if(clientPreferences.Save()!=Error.Ok){clientPreferences=previous;if(frontHint is not null)frontHint.Text="设置未能保存。";return;}
            if(previous.AIDifficulty!=clientPreferences.AIDifficulty)for(var i=1;i<8;i++)if(skirmishOpponents[i] is >0 and <4)skirmishOpponents[i]=clientPreferences.AIDifficulty+1;
            clientPreferences.ApplyAudio();if(!headless)clientPreferences.ApplyDisplay(GetWindow());settingsDraft=null;
        }
        ShowFrontPage(FrontPage.Main);
    }
    void BuildKeyboardMenu(Control stage)
    {
        settingsDraft??=clientPreferences.Clone();PageTitle(stage,"键盘选项");
        MenuText(stage,"类别",78,63,220,24);var category=new OptionButton{Name="ShortcutCategory"};category.AddItem("单位命令");Place(stage,category,78,90,200,25);
        MenuText(stage,"命令",308,63,240,24);var commands=new ItemList{Name="ShortcutCommands"};commands.AddItem("进攻移动");commands.AddItem("停止");commands.Select(selectedShortcut);Place(stage,commands,308,90,235,170);
        pendingShortcut=Key.None;
        MenuText(stage,"目前快捷键",78,285,464,24,14);var current=MenuEdit(stage,"CurrentHotkey","","",78,318,464);current.Editable=false;
        MenuText(stage,"按下新的快捷键",78,363,464,24,14);var candidate=MenuEdit(stage,"PressNewHotkey","","点击此处后按下按键",78,396,464);candidate.Editable=false;
        candidate.FocusEntered+=()=>waitingShortcut=true;
        candidate.GuiInput+=input=>{if(input is InputEventKey{Pressed:true,Echo:false} key && key.Keycode!=Key.Escape){CaptureShortcut(key.Keycode);candidate.AcceptEvent();}};
        void Refresh(){current.Text=(selectedShortcut==0?settingsDraft.AttackKey:settingsDraft.StopKey).ToString();candidate.Text="";pendingShortcut=Key.None;waitingShortcut=false;}
        commands.ItemSelected+=index=>{selectedShortcut=(int)index;Refresh();};Refresh();
        var assign=RailButton(stage,"AssignShortcut","指定",198,AssignCapturedShortcut);assign.Disabled=true;
        RailButton(stage,"ResetShortcuts","重置全部",240,()=>{settingsDraft.AttackKey=Key.A;settingsDraft.StopKey=Key.S;Refresh();assign.Disabled=true;});
        RailButton(stage,"KeyboardBack","返回",536,()=>ShowFrontPage(FrontPage.Settings));
    }
    void CaptureShortcut(Key key)
    {
        if(key is Key.None or Key.Escape or Key.Ctrl or Key.Shift or Key.Alt)return;
        pendingShortcut=key;waitingShortcut=false;
        if(FindChild("PressNewHotkey",true,false) is LineEdit edit)edit.Text=key.ToString();
        if(FindChild("AssignShortcut",true,false) is Button button)button.Disabled=false;
    }
    void AssignCapturedShortcut()
    {
        var key=pendingShortcut;if(key==Key.None)return;settingsDraft??=clientPreferences.Clone();
        if(selectedShortcut==0){if(settingsDraft.StopKey==key)settingsDraft.StopKey=settingsDraft.AttackKey;settingsDraft.AttackKey=key;}
        else{if(settingsDraft.AttackKey==key)settingsDraft.AttackKey=settingsDraft.StopKey;settingsDraft.StopKey=key;}
        waitingShortcut=false;ShowFrontPage(FrontPage.Keyboard);
    }
    void BuildNetworkOptions(Control stage)
    {
        settingsDraft??=clientPreferences.Clone();var draft=settingsDraft;PageTitle(stage,"网络选项");
        MenuText(stage,"网络卡",96,80,450,24,14);
        var addresses=IP.GetLocalAddresses().Where(a=>a.Contains('.') && !a.StartsWith("127.",StringComparison.Ordinal)).ToArray();
        var address=MenuEdit(stage,"NetworkAdapter",addresses.FirstOrDefault()??"自动","",96,113,430,64);address.Editable=false;
        MenuText(stage,"Socket 号码",96,170,430,24,14);var socket=MenuEdit(stage,"NetworkSocket",draft.Port.ToString(),"19842",96,203,430,5);
        MenuText(stage,"目的地网络 / 主机地址",96,260,430,24,14);var destination=MenuEdit(stage,"NetworkDestination",draft.LastHost,"房主 IP（直接连接时使用）",96,293,430,253);
        RailButton(stage,"NetworkOptionsOk","确定",198,()=>
        {
            if(!int.TryParse(socket.Text,out var port) || port is <1024 or >65535){frontHint!.Text="Socket 号码应为 1024–65535。";return;}
            draft.Port=port;draft.LastHost=destination.Text.Trim();ShowFrontPage(FrontPage.Settings);
        });
        RailButton(stage,"NetworkOptionsCancel","取消",536,()=>ShowFrontPage(FrontPage.Settings));
    }
}
