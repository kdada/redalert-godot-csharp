using Godot;
using RedAlert.Simulation;

namespace RedAlert;

public partial class Main
{
    LanDiscovery? lanDiscovery;
    bool discoveryPublisher,updatingRoomChoices;
    ItemList? roomList,playersList;
    TextEdit? chatLog;
    readonly List<OptionButton> roomCountries=[],roomTeams=[];
    readonly List<ColorRect> roomColors=[];
    readonly Dictionary<string,CheckBox> roomRuleChecks=[];
    string roomMapPreviewId="";
    IReadOnlyList<LanRoom> listedRooms=[];
    string listedSignature="";
    void StopLanDiscovery(){lanDiscovery?.Dispose();lanDiscovery=null;listedSignature="";}
    void PollLanDiscovery()
    {
        var publish=network is {IsHost:true,Started:false};var browse=frontPage==FrontPage.Network && network is null;
        if(!publish && !browse){StopLanDiscovery();return;}
        try
        {
            if(lanDiscovery is null || discoveryPublisher!=publish){StopLanDiscovery();lanDiscovery=new LanDiscovery(publish);discoveryPublisher=publish;}
            lanDiscovery.Poll(network?.GameName??"",clientPreferences.Port,network?.LobbyPlayers.Length??0,network?.Started??false,network?.LobbyPlayers.FirstOrDefault()?.Name??clientPreferences.CommanderName);
        }
        catch(System.Net.Sockets.SocketException){if(frontHint is not null)frontHint.Text="无法搜索局域网，可使用“IP 直连”。";}
    }
    bool StoreIdentity(bool requireAddress)
    {
        var name=nameInput?.Text??clientPreferences.CommanderName;
        if(portInput is not null)
        {
            if(!int.TryParse(portInput.Text,out var port) || port is <1024 or >65535){SetFrontNotice("端口应为 1024–65535。 ");return false;}clientPreferences.Port=port;
        }
        if(requireAddress && string.IsNullOrWhiteSpace(addressInput?.Text)){SetFrontNotice("请输入房主 IP 地址。 ");return false;}
        clientPreferences.CommanderName=name;
        if(countryChoice is not null)clientPreferences.Country=(Country)countryChoice.GetSelectedId();
        if(addressInput is not null)clientPreferences.LastHost=addressInput.Text.Trim();
        if(clientPreferences.Save()!=Error.Ok){SetFrontNotice("无法保存玩家设置。 ");return false;}return true;
    }
    void SetFrontNotice(string text){notice=text;if(frontHint is not null)frontHint.Text=text;}
    void BuildNetworkBrowser(Control stage)
    {
        PageTitle(stage,"大厅");listedSignature="";
        MenuText(stage,"你的名字",146,76,72,24);nameInput=MenuEdit(stage,"CommanderName",clientPreferences.CommanderName,"",215,76,138);
        nameInput.TextSubmitted+=_=>{clientPreferences.CommanderName=nameInput.Text;clientPreferences.Save();RefreshNetworkPage();};
        MenuText(stage,"玩家",94,113,215,24);MenuText(stage,"游戏",314,113,223,24);
        playersList=Place(stage,new ItemList{Name="LanPlayers"},94,137,214,180);
        roomList=Place(stage,new ItemList{Name="LanGames"},314,137,222,180);roomList.ItemActivated+=_=>JoinSelectedLanRoom();
        chatLog=Place(stage,new TextEdit{Name="LanChat",Editable=false,MouseFilter=MouseFilterEnum.Ignore},94,324,442,153);
        var chat=MenuEdit(stage,"LobbyChatInput","","建立或加入游戏后可交谈",94,478,442,128);chat.Editable=false;
        RailButton(stage,"HostRoom","新建",198,CreateLanRoom);
        RailButton(stage,"JoinRoom","加入",240,JoinSelectedLanRoom);
        RailButton(stage,"DirectConnect","IP 直连",282,()=>{clientPreferences.CommanderName=nameInput.Text;ShowFrontPage(FrontPage.JoinNetwork);});
        RailButton(stage,"NetworkBack","主选单",536,()=>ShowFrontPage(FrontPage.Main));
        PollLanDiscovery();RefreshNetworkPage();
    }
    void BuildCreateNetwork(Control stage)=>BuildNetworkBrowser(stage);
    void CreateLanRoom()
    {
        if(!StoreIdentity(false))return;StopLanDiscovery();
        OpenHost(clientPreferences.CommanderName,clientPreferences.Country,clientPreferences.Port,-1);
        if(network is null)return;network.SetGameName(clientPreferences.CommanderName+"的游戏");ShowFrontPage(FrontPage.NetworkRoom);
    }
    void JoinSelectedLanRoom()
    {
        if(roomList is null)return;var selected=roomList.GetSelectedItems();
        if(selected.Length==0 || selected[0]<=0 || selected[0]>listedRooms.Count){SetFrontNotice("请从游戏列表选择一个房间。 ");return;}
        if(!StoreIdentity(false))return;var room=listedRooms[selected[0]-1];clientPreferences.LastHost=room.Address;clientPreferences.Port=room.Port;clientPreferences.Save();StopLanDiscovery();
        OpenJoin(room.Address,clientPreferences.CommanderName,clientPreferences.Country,room.Port,-1);if(network is not null)ShowFrontPage(FrontPage.NetworkRoom);
    }
    void BuildDirectJoin(Control stage)
    {
        PageTitle(stage,"直接连接");MenuText(stage,"你的名字",96,98,440,24,14);
        nameInput=MenuEdit(stage,"CommanderName",clientPreferences.CommanderName,"",96,130,430);
        MenuText(stage,"主机 IP",96,192,440,24,14);addressInput=MenuEdit(stage,"HostAddress",clientPreferences.LastHost,"房主的局域网 IP",96,224,430,253);
        MenuText(stage,"Socket 号码",96,286,440,24,14);portInput=MenuEdit(stage,"HostPort",clientPreferences.Port.ToString(),"19842",96,318,430,5);
        RailButton(stage,"JoinDirect","加入",198,()=>
        {
            if(!StoreIdentity(true))return;StopLanDiscovery();OpenJoin(clientPreferences.LastHost,clientPreferences.CommanderName,clientPreferences.Country,clientPreferences.Port,-1);
            if(network is not null)ShowFrontPage(FrontPage.NetworkRoom);
        });
        RailButton(stage,"DirectBack","取消",536,()=>ShowFrontPage(FrontPage.Network));
    }
    void BuildNetworkRoom(Control stage)
    {
        PageTitle(stage,"网络游戏");roomCountries.Clear();roomTeams.Clear();roomColors.Clear();roomRuleChecks.Clear();lobbyRows.Clear();roomMapPreviewId="";
        for(var i=0;i<Rules.MaxPlayers;i++)
        {
            var row=i;var y=25+i*29;
            MenuPanel(stage,61,y,186,25);var name=MenuText(stage,"空位",63,y,180,25,13);name.ClipText=true;
            var status=MenuText(stage,"",251,y,29,25,13);
            var country=CountryPicker(stage,295,y,147,"RoomCountry"+i);country.ItemSelected+=_=>ChangeOwnRoomIdentity(row);
            var color=Place(stage,new ColorRect{Color=SlotColor(i),MouseFilter=MouseFilterEnum.Ignore},454,y+2,38,20);
            var team=new OptionButton{Name="RoomTeam"+i};for(var n=0;n<Rules.MaxPlayers;n++)team.AddItem($"队 {n+1}",n);Place(stage,team,505,y,99,25);team.ItemSelected+=_=>ChangeOwnRoomIdentity(row);
            roomCountries.Add(country);roomTeams.Add(team);roomColors.Add(color);lobbyRows.Add([name,status]);
        }
        var rules=network?.LobbySettings??new MatchSettings(20261018,[]);
        AddRoomRule(stage,"RoomShortGame","短游戏",273,rules.ShortGame,(settings,on)=>settings with{ShortGame=on});
        AddRoomRule(stage,"RoomMcvRepacks","基地重新部署",300,rules.MCVRepacks,(settings,on)=>settings with{MCVRepacks=on});
        AddRoomRule(stage,"RoomCrates","随机工具箱",327,rules.Crates,(settings,on)=>settings with{Crates=on});
        AddRoomRule(stage,"RoomSuperWeapons","超级武器",354,rules.SuperWeapons,(settings,on)=>settings with{SuperWeapons=on});
        AddRoomRule(stage,"RoomBuildNearAlly","盟友附近建设",381,rules.BuildNearAlly,(settings,on)=>settings with{BuildNearAlly=on});
        MenuText(stage,"初始资金",312,272,160,24);var credits=MenuText(stage,rules.StartingCredits.ToString(),526,272,77,24,13);credits.Name="RoomCreditsValue";
        var money=MenuSlider(stage,"RoomCredits",312,300,220,5000,10000,100,rules.StartingCredits,n=>ChangeRoomRules(settings=>settings with{StartingCredits=(int)n}));money.Editable=network?.IsHost==true;
        MenuText(stage,"初始单位",312,340,160,24);var units=MenuText(stage,rules.StartingUnits.ToString(),526,340,77,24,13);units.Name="RoomStartingUnitsValue";
        var count=MenuSlider(stage,"RoomStartingUnits",312,368,220,0,10,1,rules.StartingUnits,n=>ChangeRoomRules(settings=>settings with{StartingUnits=(int)n}));count.Editable=network?.IsHost==true;
        MenuText(stage,"经典开局：基地车",312,399,292,23,11,MenuDim);
        chatLog=Place(stage,new TextEdit{Name="RoomChat",Editable=false},62,430,542,67);
        var chat=MenuEdit(stage,"RoomChatInput","","输入文字并按 Enter 交谈",62,503,542,128);chat.TextSubmitted+=text=>{network?.SendLobbyChat(text);chat.Clear();RefreshNetworkPage();};
        BuildSetupRail(stage,network?.LobbySeed??20261018,"网络对战",network?.IsHost==true);
        startButton=RailButton(stage,"StartNetwork",network?.IsHost==true?"开始游戏":"接受",306,()=>
        {
            if(network is null)return;
            if(network.IsHost){if(!network.StartMatch(true))SetFrontNotice(network.Status);}
            else network.SetLobbyAccepted(!network.LocalAccepted);
            RefreshNetworkPage();
        });
        var customize=RailButton(stage,"CustomizeNetwork","自订战役",348,()=>{customizeReturn=FrontPage.NetworkRoom;ShowFrontPage(FrontPage.Customize);});customize.Disabled=network?.IsHost!=true;
        RailButton(stage,"RoomBack","返回",536,LeaveNetworkRoom);RefreshNetworkPage();
    }
    void AddRoomRule(Control stage,string name,string caption,float y,bool value,Func<MatchSettings,bool,MatchSettings> change)
    {
        var check=MenuCheck(stage,name,caption,62,y,value,on=>ChangeRoomRules(settings=>change(settings,on)));
        check.Disabled=network?.IsHost!=true;roomRuleChecks.Add(name,check);
    }
    void ChangeRoomRules(Func<MatchSettings,MatchSettings> change)
    {
        if(updatingRoomChoices || network is not {IsHost:true,Started:false})return;
        if(!network.SetLobbyRules(change(network.LobbySettings)))SetFrontNotice(network.Status);
        RefreshNetworkPage();
    }
    static Color SlotColor(int slot)=>Battlefield.PlayerColors[slot%8];
    void ChangeOwnRoomIdentity(int row)
    {
        if(updatingRoomChoices || network is null || row!=network.MyPlayer)return;
        network.SetLobbyIdentity((Country)roomCountries[row].GetSelectedId(),roomTeams[row].GetSelectedId());
    }
    void LeaveNetworkRoom()
    {
        network?.Dispose();network=null;failure="";notice="准备就绪";StopLanDiscovery();ShowFrontPage(FrontPage.Network);
    }
    void RefreshNetworkPage()
    {
        if(frontPage==FrontPage.Network && roomList is not null && playersList is not null)
        {
            var rooms=lanDiscovery?.Rooms??[];var signature=string.Join("|",rooms.Select(r=>$"{r.Address}:{r.Port}:{r.Name}:{r.Players}"));
            if(signature!=listedSignature || roomList.ItemCount==0)
            {
                var old=roomList.GetSelectedItems();var oldIndex=old.Length==0?0:old[0];listedRooms=rooms;listedSignature=signature;roomList.Clear();roomList.AddItem("大厅");
                foreach(var room in rooms)roomList.AddItem($"{room.Name}  ({room.Players} 人)");
                roomList.Select(Math.Min(oldIndex,roomList.ItemCount-1));
            }
            playersList.Clear();playersList.AddItem(nameInput?.Text??clientPreferences.CommanderName);
            foreach(var owner in rooms.Select(r=>r.Owner).Where(s=>s.Length>0).Distinct())if(owner!=clientPreferences.CommanderName)playersList.AddItem(owner);
            if(frontHint is not null && lanDiscovery?.Error.Length>0)frontHint.Text=lanDiscovery.Error;
            return;
        }
        if(frontPage!=FrontPage.NetworkRoom || lobbyRows.Count==0)return;
        var players=network?.LobbyPlayers??[];var slotLimit=network?.LobbyMaxPlayers??0;updatingRoomChoices=true;
        for(var i=0;i<Rules.MaxPlayers;i++)
        {
            var populated=i<players.Length;var available=i<slotLimit;
            lobbyRows[i][0].Text=populated?players[i].Name:available?"空位":"不可用";
            lobbyRows[i][0].Modulate=available?Colors.White:new Color(.45f,.45f,.45f);
            lobbyRows[i][0].TooltipText=available?"":$"当前地图最多支持 {slotLimit} 人";
            lobbyRows[i][1].Text=populated && network!.LobbyAcceptances.ElementAtOrDefault(i)?"●":"";
            roomColors[i].Color=available?SlotColor(i):new Color("353535");
            roomCountries[i].Visible=populated;roomTeams[i].Visible=populated;
            if(!populated)continue;
            roomCountries[i].Select(roomCountries[i].GetItemIndex((int)players[i].Country));roomTeams[i].Select(roomTeams[i].GetItemIndex(players[i].Team));
            var editable=network is {Started:false} && i==network.MyPlayer && (network.IsHost || !network.LocalAccepted);
            roomCountries[i].Disabled=!editable;roomTeams[i].Disabled=!editable;
        }
        if(network is not null)
        {
            var settings=network.LobbySettings;var editable=network is {IsHost:true,Started:false};
            if(FindChild("RoomCredits",true,false) is HSlider slider){slider.Value=settings.StartingCredits;slider.Editable=editable;}
            if(FindChild("RoomCreditsValue",true,false) is Label value)value.Text=settings.StartingCredits.ToString();
            if(FindChild("RoomStartingUnits",true,false) is HSlider count){count.Value=settings.StartingUnits;count.Editable=editable;}
            if(FindChild("RoomStartingUnitsValue",true,false) is Label unitValue)unitValue.Text=settings.StartingUnits.ToString();
            foreach(var (name,check) in roomRuleChecks)
            {
                check.SetPressedNoSignal(name switch
                {
                    "RoomShortGame"=>settings.ShortGame,"RoomMcvRepacks"=>settings.MCVRepacks,"RoomCrates"=>settings.Crates,
                    "RoomSuperWeapons"=>settings.SuperWeapons,"RoomBuildNearAlly"=>settings.BuildNearAlly,_=>false
                });check.Disabled=!editable;
            }
            if(roomMapPreviewId!=network.LobbyMapId){RefreshClassicSetupMap(network.LobbyMapId);roomMapPreviewId=network.LobbyMapId;}
            if(FindChild("CustomizeNetwork",true,false) is Button customize)customize.Disabled=!editable;
        }
        updatingRoomChoices=false;
        if(startButton is not null && network is not null)
        {
            startButton.Text=network.IsHost?"开始游戏":network.LocalAccepted?"取消接受":"接受";
            startButton.Disabled=network.MyPlayer<0 || network.LobbyRevision<1 || network.Started || network.Error.Length>0
                || network.IsHost && (players.Length<network.LobbyMinPlayers || players.Length>slotLimit || players.Select(p=>p.Team).Distinct().Count()<2 || !network.AllGuestsAccepted);
        }
        if(chatLog is not null && network is not null)
        {var text=string.Join("\n",network.LobbyChat);if(chatLog.Text!=text){chatLog.Text=text;chatLog.ScrollVertical=chatLog.GetLineCount();}}
        if(frontHint is not null)frontHint.Text=network?.Error.Length>0?network.Error:network?.Status??notice;
    }
}
