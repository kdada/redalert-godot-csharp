using Godot;

namespace RedAlert;

public partial class Main
{
    BattleChat? localBattleChat;
    BattleChat? CurrentBattleChat => network is null ? localBattleChat : network.Chat;
    Control? battleChatPanel;
    RichTextLabel? battleChatLog;
    HBoxContainer? battleChatComposer;
    Label? battleChatChannel;
    LineEdit? battleChatInput;
    Label? battleChatFeedback;
    string selectedBattleChatChannel="all";
    long displayedChatId = -1, latestVisibleChatId;
    ulong chatArrivedAt;
    string lastChatError = "";
    public bool ChatTyping => battleChatComposer?.Visible == true;
    public string SelectedBattleChatChannel=>selectedBattleChatChannel;
    const int BattleChatFontSize=(int)(12*Battlefield.ClassicZoom);
    static readonly Color AllChatColor=new("ffff00"),TeamChatColor=new("80ff80");

    void BuildBattleChat()
    {
        // Text-only log at the left. This is deliberately a Control, not a panel:
        // chat never paints a box, toolbar, timestamp, sender ID or modern button.
        battleChatPanel=new Control{Name="BattleChatPanel",Visible=false,MouseFilter=MouseFilterEnum.Ignore};
        battlefield!.AddChild(battleChatPanel);
        battleChatLog=new RichTextLabel{Name="BattleChatLog",BbcodeEnabled=false,ScrollActive=false,ScrollFollowing=true,
            SelectionEnabled=false,MouseFilter=MouseFilterEnum.Ignore,AutowrapMode=TextServer.AutowrapMode.WordSmart};
        using(var empty=new StyleBoxEmpty())
        {
            battleChatLog.AddThemeStyleboxOverride("normal",empty);
            battleChatLog.AddThemeStyleboxOverride("focus",empty);
        }
        battleChatLog.AddThemeFontSizeOverride("normal_font_size",BattleChatFontSize);
        battleChatLog.AddThemeColorOverride("font_shadow_color",new Color(0,0,0,.9f));
        battleChatLog.AddThemeConstantOverride("shadow_offset_x",1);battleChatLog.AddThemeConstantOverride("shadow_offset_y",1);
        battleChatPanel.AddChild(battleChatLog);

        // Composer is a separate bottom-centered sibling, with only a channel
        // label and transparent native LineEdit. It never moves the sent log.
        battleChatComposer=new HBoxContainer{Name="BattleChatComposer",Visible=false,MouseFilter=MouseFilterEnum.Ignore};
        battleChatComposer.AddThemeConstantOverride("separation",7);battlefield.AddChild(battleChatComposer);
        battleChatChannel=new Label{Name="BattleChatChannel",Text="[所有人]"};
        battleChatChannel.AddThemeFontSizeOverride("font_size",BattleChatFontSize);battleChatChannel.AddThemeColorOverride("font_color",AllChatColor);
        battleChatChannel.VerticalAlignment=VerticalAlignment.Center;battleChatChannel.MouseFilter=MouseFilterEnum.Ignore;
        battleChatChannel.AddThemeColorOverride("font_shadow_color",Colors.Black);
        battleChatChannel.AddThemeConstantOverride("shadow_offset_x",1);battleChatChannel.AddThemeConstantOverride("shadow_offset_y",1);
        battleChatComposer.AddChild(battleChatChannel);
        battleChatInput=new LineEdit{Name="BattleChatInput",MaxLength=BattleChat.MaxTextLength,
            PlaceholderText="输入文字 · Enter 发送 · Esc 取消",SizeFlagsHorizontal=SizeFlags.ExpandFill,CustomMinimumSize=new Vector2(80,34)};
        using(var empty=new StyleBoxEmpty{ContentMarginTop=3,ContentMarginBottom=3})
            foreach(var state in new[]{"normal","focus","read_only"})battleChatInput.AddThemeStyleboxOverride(state,empty);
        battleChatInput.AddThemeFontSizeOverride("font_size",BattleChatFontSize);
        battleChatInput.AddThemeColorOverride("font_color",AllChatColor);
        battleChatInput.AddThemeColorOverride("font_placeholder_color",new Color(.86f,.83f,.68f,.8f));
        battleChatInput.AddThemeColorOverride("font_outline_color",new Color(0,0,0,.85f));
        battleChatInput.AddThemeConstantOverride("outline_size",3);
        battleChatInput.TextSubmitted+=_=>SubmitBattleChat();battleChatComposer.AddChild(battleChatInput);
        battleChatFeedback=Text("",12,new Color("ffcc83"));battleChatFeedback.Name="BattleChatFeedback";
        battleChatFeedback.Visible=false;battleChatFeedback.MouseFilter=MouseFilterEnum.Ignore;
        battleChatFeedback.AddThemeColorOverride("font_shadow_color",Colors.Black);
        battleChatFeedback.AddThemeConstantOverride("shadow_offset_x",1);battleChatFeedback.AddThemeConstantOverride("shadow_offset_y",1);
        battlefield.AddChild(battleChatFeedback);
        battlefield.Resized+=LayoutBattleChat;LayoutBattleChat();RefreshBattleChat();
    }
    void ResetBattleChatUi()
    {
        battleChatPanel=null;battleChatLog=null;battleChatComposer=null;battleChatChannel=null;battleChatInput=null;battleChatFeedback=null;
        displayedChatId=-1;latestVisibleChatId=0;chatArrivedAt=0;lastChatError="";selectedBattleChatChannel="all";
    }
    void LayoutBattleChat()
    {
        if(battleChatPanel is null || battleChatLog is null || battleChatComposer is null || battlefield is null)return;
        var available=Math.Max(0,battlefield.Size.X-32);
        var logWidth=Math.Min(650,available);var logHeight=Math.Max(0,Math.Min(210,battlefield.Size.Y-110));
        battleChatPanel.Position=new Vector2(16,12);battleChatPanel.Size=new Vector2(logWidth,logHeight);
        battleChatLog.Position=Vector2.Zero;battleChatLog.Size=battleChatPanel.Size;
        var width=Math.Min(650,available);
        battleChatComposer.Size=new Vector2(width,34);
        battleChatComposer.Position=new Vector2((battlefield.Size.X-width)/2,Math.Max(0,battlefield.Size.Y-54));
        if(battleChatFeedback is not null)
        {
            battleChatFeedback.Position=battleChatComposer.Position-new Vector2(0,23);
            battleChatFeedback.Size=new Vector2(width,20);
        }
    }
    public void OpenBattleChat(string channel = "all")
    {
        if(CurrentBattleChat is null || LocalPlayer<0 || battleChatComposer is null || battleChatInput is null || MatchMenuOpen)return;
        selectedBattleChatChannel=channel=="team"?"team":"all";
        var team=selectedBattleChatChannel=="team";var color=team?TeamChatColor:AllChatColor;
        battleChatChannel!.Text=team?"[队伍]":"[所有人]";
        battleChatChannel.AddThemeColorOverride("font_color",color);battleChatInput.AddThemeColorOverride("font_color",color);
        battleChatComposer.Show();displayedChatId=-1;RefreshBattleChat();LayoutBattleChat();battleChatInput.GrabFocus();
    }
    public void CloseBattleChat()
    {
        if(battleChatComposer is null)return;
        battleChatComposer.Hide();battleChatInput!.Clear();battleChatInput.ReleaseFocus();battleChatFeedback!.Hide();
        battlefield?.GrabFocus();displayedChatId=-1;RefreshBattleChat();
    }
    void SubmitBattleChat()
    {
        if(battleChatInput is null || CurrentBattleChat is null || Battle is null || LocalPlayer<0)return;
        var text=battleChatInput.Text;var channel=selectedBattleChatChannel;
        bool sent;string error;lastChatError="";
        if(network is not null){sent=network.SendBattleChat(channel,text);error=network.ChatError;}
        else sent=CurrentBattleChat.TrySend(LocalPlayer,channel,text,Battle.Tick,out _,out error);
        if(sent){CloseBattleChat();return;}
        battleChatFeedback!.Text=ChatErrorText(error);battleChatFeedback.Show();battleChatInput.GrabFocus();
    }
    static string ChatErrorText(string error) => error switch
    {
        "empty_text"=>"请输入文字。","text_too_long"=>"消息最多 280 个字符。","rate_limited"=>"发送过于频繁，请稍后再试。",
        "player_inactive"=>"当前玩家已结束作战，不能发送消息。","invalid_channel"=>"请选择所有人或队伍频道。",_=>"消息未发送："+error
    };
    void RefreshBattleChat()
    {
        if(battleChatPanel is null || battleChatLog is null || CurrentBattleChat is null || LocalPlayer<0)return;
        var messages=CurrentBattleChat.Recent(LocalPlayer,5);
        var newest=messages.LastOrDefault()?.Id??0;
        if(newest>latestVisibleChatId){latestVisibleChatId=newest;chatArrivedAt=Time.GetTicksMsec();AudioChat();}
        if(newest!=displayedChatId)
        {
            displayedChatId=newest;battleChatLog.Clear();
            foreach(var message in messages)
            {
                battleChatLog.PushColor(message.Channel=="team"?TeamChatColor:AllChatColor);
                battleChatLog.AddText($"[{(message.Channel=="team"?"队伍":"所有人")}] {message.SenderName}: {message.Text}\n");
                battleChatLog.Pop();
            }
        }
        battleChatPanel.Visible=messages.Length>0 && (ChatTyping || Time.GetTicksMsec()-chatArrivedAt<12000);
        LayoutBattleChat();
        if(network is not null && network.ChatError.Length>0 && network.ChatError!=lastChatError)
        {lastChatError=network.ChatError;Notify(ChatErrorText(lastChatError));}
    }
    public override void _Input(InputEvent input)
    {
        ConstrainBattlePointer(input);
        if(input is InputEventMouseButton{Pressed:true,ButtonIndex:MouseButton.Left} && GetViewport().GuiGetHoveredControl() is BaseButton)AudioUiClick();
        if(Battle is null || battleChatComposer is null || input is not InputEventKey{Pressed:true,Echo:false} key)return;
        if(key.Keycode==Key.Escape)
        {
            if(ChatTyping)CloseBattleChat();else if(MatchMenuOpen)HideMatchMenu();else if(battlefield?.CancelMaintenance()==true)Notify("已取消维修 / 出售模式");else ShowMatchMenu();
            GetViewport().SetInputAsHandled();return;
        }
        if(!ChatTyping && !MatchMenuOpen && (key.Keycode is Key.Enter or Key.KpEnter))
        {OpenBattleChat(key.ShiftPressed?"team":"all");GetViewport().SetInputAsHandled();}
    }
}
