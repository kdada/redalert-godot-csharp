using Godot;
using RedAlert.Simulation;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace RedAlert;

public partial class Main
{
    bool FrontEndTest=>options.ContainsKey("frontend-test") || options.ContainsKey("experience-check") || options.ContainsKey("presentation-check") || options.ContainsKey("battle-view-check") || BattleChatTest;
    async void RunFrontEndChecks()
    {
        var original=clientPreferences.Clone();var checks=new List<string>();var error="";NetworkSession? guest=null;LanDiscovery? discoveryProbe=null;
        void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        T Find<T>(string name) where T:Node=>FindChild(name,true,false) as T??throw new InvalidOperationException("missing control "+name);
        async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        async Task Click(string name)
        {
            var button=Find<Control>(name);Require(button is not BaseButton{Disabled:true},"disabled control "+name);var point=button.GetGlobalRect().GetCenter();
            using var down=new InputEventMouseButton{Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left,Pressed=true};GetViewport().PushInput(down,true);
            using var up=new InputEventMouseButton{Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left,Pressed=false};GetViewport().PushInput(up,true);
            await Frame();await Frame();
        }
        void Select(string name,int id)
        {
            var choice=Find<OptionButton>(name);var index=choice.GetItemIndex(id);Require(index>=0,"missing choice "+name+"/"+id);choice.Select(index);choice.EmitSignal(OptionButton.SignalName.ItemSelected,(long)index);
        }
        void Layout()
        {
            var view=GetViewportRect();
            foreach(var control in FindChildren("*","Control",true,false).OfType<Control>().Where(c=>c.IsVisibleInTree() && (c is BaseButton || c is LineEdit)))
            {var rect=control.GetGlobalRect();Require(rect.Size.X>=20 && rect.Size.Y>=20,"tiny menu control "+control.Name);Require(view.Encloses(rect),"menu control outside viewport "+control.Name+" "+rect);}
        }
        async Task Wait(Func<bool> condition,Action poll,string label)
        {
            var deadline=Time.GetTicksMsec()+6000;
            while(!condition() && Time.GetTicksMsec()<deadline){poll();await ToSignal(GetTree().CreateTimer(.01),SceneTreeTimer.SignalName.Timeout);}
            Require(condition(),"timeout "+label);
        }
        try
        {
            await Frame();await Frame();Layout();Require(frontPage==FrontPage.Main,"startup is not main menu");
            foreach(var name in new[]{"MenuSingle","MenuInternet","MenuNetwork","MenuMovies","MenuOptions","MenuQuit"})Find<Button>(name);
            await Click("MenuQuit");await Click("CancelQuit");Require(frontPage==FrontPage.Main,"exit cancellation failed");checks.Add("six original menu entries; separate single-player branch; exit confirmation and cancellation");
            await Click("MenuOptions");Layout();Find<HSlider>("SettingDetails").Value=0;Find<HSlider>("SettingCameraSpeed").Value=1.5;Find<HSlider>("SettingSound").Value=4;Find<HSlider>("SettingVoice").Value=6;Find<CheckBox>("SettingTargetLines").ButtonPressed=false;
            await Click("OptionsKeyboard");await Click("PressNewHotkey");
            using(var key=new InputEventKey{Keycode=Key.Q,PhysicalKeycode=Key.Q,Pressed=true})GetViewport().PushInput(key,true);await Frame();await Frame();
            Require(settingsDraft!.AttackKey!=Key.Q && pendingShortcut==Key.Q,"key capture assigned before explicit Assign");await Click("AssignShortcut");Require(settingsDraft.AttackKey==Key.Q,"keyboard assignment failed");await Click("KeyboardBack");await Click("OptionsNetwork");
            Find<LineEdit>("NetworkSocket").Text="1";await Click("NetworkOptionsOk");Require(frontPage==FrontPage.NetworkSettings,"invalid socket accepted");
            int freePort;using(var socket=new Socket(AddressFamily.InterNetwork,SocketType.Dgram,ProtocolType.Udp)){socket.Bind(new IPEndPoint(IPAddress.Loopback,0));freePort=((IPEndPoint)socket.LocalEndPoint!).Port;}
            Find<LineEdit>("NetworkSocket").Text=freePort.ToString();Find<LineEdit>("NetworkDestination").Text="127.0.0.1";await Click("NetworkOptionsOk");await Click("SettingsBack");
            var saved=ClientPreferences.Load();Require(saved.CameraSpeed==1.5f && saved.SoundVolume==4 && saved.VoiceVolume==6 && !saved.TargetLines && saved.VisualDetails==0 && saved.AttackKey==Key.Q && saved.Port==freePort,"options persistence failed");
            Require(Math.Abs(AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex("Effects"))-20*MathF.Log10(.4f))<.01,"sound slider did not reach actual mixer");
            await Click("MenuOptions");Require(Find<HSlider>("SettingSound").Value==4,"reopened audio value changed");await Click("SettingsBack");checks.Add("one-page original options layout; sliders, actual audio buses, keyboard assignment and network socket save/reload");
            await Click("MenuNetwork");Layout();Require(lobbyRows.Count==0 && Find<ItemList>("LanGames").ItemCount>=1,"LAN browser incorrectly shows setup slots");
            await Click("JoinRoom");Require(network is null && notice.Contains("选择",StringComparison.Ordinal),"empty game selection joined");
            await Click("DirectConnect");Find<LineEdit>("HostAddress").Text="";await Click("JoinDirect");Require(network is null && notice.Contains("IP",StringComparison.Ordinal),"empty join address accepted");
            Find<LineEdit>("HostAddress").Text="127.0.0.1";Find<LineEdit>("HostPort").Text="1";await Click("JoinDirect");Require(network is null && notice.Contains("端口",StringComparison.Ordinal),"invalid game port accepted");await Click("DirectBack");
            Find<LineEdit>("CommanderName").Text="界面测试房主";await Click("HostRoom");Require(frontPage==FrontPage.NetworkRoom && lobbyRows.Count==8 && Find<Button>("StartNetwork").Disabled,"New did not enter guarded eight-slot setup");Select("RoomCountry0",(int)Country.France);
            discoveryProbe=new LanDiscovery();await Wait(()=>discoveryProbe.Rooms.Any(r=>r.Port==freePort),()=>discoveryProbe.Poll(),"real UDP room discovery");
            Require(discoveryProbe.Rooms.Any(r=>r.Name.Contains("界面测试房主",StringComparison.Ordinal)),"advertised room has wrong name");discoveryProbe.Dispose();discoveryProbe=null;
            guest=new NetworkSession();guest.Join("127.0.0.1",freePort,"界面测试来宾",Country.Russia);
            await Wait(()=>network!.LobbyPlayers.Length==2 && guest.MyPlayer==1,()=>guest.Poll(),"real second ENet peer");RefreshLobby();Require(Find<Button>("StartNetwork").Disabled,"host bypassed guest acceptance");
            guest.SetLobbyAccepted(true);await Wait(()=>network!.AllGuestsAccepted && guest.LocalAccepted,()=>guest.Poll(),"guest accepts current setup");
            Select("RoomCountry0",(int)Country.Britain);await Wait(()=>!guest.LocalAccepted,()=>guest.Poll(),"country change resets acceptance");Require(!network!.StartMatch(true),"host bypassed reset acceptance");
            guest.SetLobbyIdentity(Country.Russia,0);await Wait(()=>network.LobbyPlayers[1].Team==0,()=>guest.Poll(),"guest changes real team");guest.SetLobbyAccepted(true);
            await Wait(()=>network.AllGuestsAccepted,()=>guest.Poll(),"same-team acceptance");RefreshLobby();Require(Find<Button>("StartNetwork").Disabled,"single team allowed start");
            guest.SetLobbyIdentity(Country.Russia,1);await Wait(()=>network.LobbyPlayers[1].Team==1,()=>guest.Poll(),"restore second team");
            guest.SendLobbyChat("聊天室回归");await Wait(()=>network.LobbyChat.Any(line=>line.Contains("聊天室回归",StringComparison.Ordinal)) && guest.LobbyChat.Count>0,()=>guest.Poll(),"reliable room chat");
            Find<HSlider>("RoomCredits").Value=11000;await Wait(()=>guest.LobbyCredits==11000,()=>guest.Poll(),"shared credits update");
            await Click("CustomizeNetwork");var maps=Find<ItemList>("BattleMaps");maps.Select(1);maps.EmitSignal(ItemList.SignalName.ItemSelected,1L);await Click("UseBattleMap");await Wait(()=>guest.LobbySeed==mapSeeds[1],()=>guest.Poll(),"shared selected map");
            var host=network;network=guest;
            try
            {
                ShowFrontPage(FrontPage.NetworkRoom);RefreshNetworkPage();Require(Find<HSlider>("RoomCredits").Value==11000 && !Find<OptionButton>("RoomCountry1").Disabled && Find<OptionButton>("RoomCountry0").Disabled,"guest setup view does not reflect ownership/settings");
                host.SetLobbyGameOptions(mapSeeds[2],12500);await Wait(()=>guest.LobbyCredits==12500 && guest.LobbySeed==mapSeeds[2],()=>host.Poll(),"host options reach actual guest view");RefreshNetworkPage();
                Require(Find<HSlider>("RoomCredits").Value==12500 && Find<Label>("RoomCreditsValue").Text=="12500" && Find<ShellMapPreview>("SetupMapPreview").Seed==mapSeeds[2],"live guest widgets stayed on stale map/credits");
                await Click("StartNetwork");await Wait(()=>host.AllGuestsAccepted,()=>host.Poll(),"guest accepts through actual GUI");RefreshNetworkPage();Require(Find<OptionButton>("RoomCountry1").Disabled,"accepted guest controls remained editable");
            }
            finally{network=host;ShowFrontPage(FrontPage.NetworkRoom);}
            RefreshLobby();Layout();Require(!Find<Button>("StartNetwork").Disabled,"accepted two-team lobby cannot start");
            await Click("StartNetwork");await Wait(()=>network.Running && guest.Running,()=>guest.Poll(),"UI starts two-peer match");
            Require(battlefield is not null && network.World!.Settings.StartingCredits==12500 && guest.World!.Settings.Seed==mapSeeds[2] && network.World.Players[0].Setup.Country==Country.Britain,"selected lobby settings lost at launch");
            checks.Add("LAN browser → New → setup; actual UDP discovery, two real ENet peers, country/team changes, readiness reset, chat, shared map/credits and host start");
            network.Dispose();network=null;guest.Dispose();guest=null;ReturnToDeployment();await Frame();await Frame();
            await Click("MenuSingle");Require(Find<Button>("NewCampaign").Disabled && Find<Button>("LoadGame").Disabled,"unavailable campaigns presented as playable");await Click("MenuSkirmish");Layout();
            Find<LineEdit>("CommanderName").Text="单机指挥官";Select("CountryChoice",(int)Country.Britain);Select("SkirmishOpponent4",1);Find<HSlider>("SkirmishCredits").Value=5000;await Click("StartPractice");
            await Wait(()=>localWorld is not null,()=>{},"async skirmish preparation");
            Require(localWorld is not null && localWorld.Players.Length==5 && localWorld.Settings.StartingCredits==5000 && localWorld.Players[0].Setup.Country==Country.Britain && localWorld.Players[0].Setup.Name=="单机指挥官" && battlefield is not null,"configured skirmish start lost roster or settings");
            checks.Add("Single Player → Skirmish → adjustable computer roster and countries → actual five-player battle with selected starting credits");
        }
        catch(Exception exception){error=exception.ToString();GD.PushError(error);}
        finally{network?.Dispose();network=null;guest?.Dispose();discoveryProbe?.Dispose();StopLanDiscovery();localWorld=null;clientPreferences=original;original.Save();original.ApplyAudio();}
        var result=new{passed=error.Length==0,frontendTest=true,checks,error};var json=JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true});GD.Print(json);
        if(reportPath.Length>0){var path=System.IO.Path.GetFullPath(reportPath);System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);System.IO.File.WriteAllText(path,json);}
        reportWritten=true;ended=true;GetTree().Quit(error.Length==0?0:2);
    }
}
