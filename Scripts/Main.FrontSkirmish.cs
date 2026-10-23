using Godot;
using RedAlert.Simulation;

namespace RedAlert;

public partial class Main
{
    // Seeds remain independent from map identity. Kept for older lobby callers;
    // official selections below use catalog IDs and never choose a map by seed.
    readonly uint[] mapSeeds=[20261018,20261019,20261021,20261027];
    ClassicMapInfo[] classicMaps=[];
    string classicMapError="";
    int selectedMap,skirmishCredits=10000,skirmishStartingUnits=10;
    bool skirmishShortGame=true,skirmishMcvRepacks=true,skirmishCrates=true,skirmishSuperWeapons=true,skirmishBuildNearAlly=true;
    readonly int[] skirmishOpponents=[0,2,2,2,0,0,0,0];
    readonly Country[] skirmishCountries=Enumerable.Range(0,8).Select(i=>Enum.GetValues<Country>()[i%Enum.GetValues<Country>().Length]).ToArray();
    readonly int[] skirmishTeams=Enumerable.Range(0,8).ToArray();
    int[] practiceDifficulties=Enumerable.Repeat(1,8).ToArray();
    ClassicMapInfo? SelectedClassicMap=>classicMaps.Length==0?null:classicMaps[Math.Clamp(selectedMap,0,classicMaps.Length-1)];

    bool EnsureClassicMaps()
    {
        if(classicMaps.Length>0)return true;
        try
        {
            if(!Godot.FileAccess.FileExists(ClassicMapCatalog.ResourcePath))throw new InvalidDataException("官方地图目录尚未就绪");
            classicMaps=ClassicMapCatalog.Parse(Godot.FileAccess.GetFileAsString(ClassicMapCatalog.ResourcePath)).Maps;
            if(classicMaps.Length==0)throw new InvalidDataException("官方地图目录为空");
            var defaultPlayers=1+skirmishOpponents.Skip(1).Count(value=>value>0);
            selectedMap=Math.Max(0,Array.FindIndex(classicMaps,map=>map.MaxPlayers>=defaultPlayers));
            classicMapError="";return true;
        }
        catch(Exception error){classicMaps=[];classicMapError="无法加载官方地图："+error.Message;return false;}
    }
    static string ClassicMapTitle(ClassicMapInfo map)=>map.Name.EndsWith(')')?map.Name:$"{map.Name} ({map.MinPlayers}–{map.MaxPlayers})";
    ClassicMapInfo? FindClassicMap(string id)=>classicMaps.FirstOrDefault(map=>map.Id==id);
    static Texture2D? ClassicMapPreview(ClassicMapInfo? map)
    {
        if(map is null || string.IsNullOrEmpty(map.PreviewPath) || !Godot.FileAccess.FileExists(map.PreviewPath))return null;
        try{return GD.Load<Texture2D>(map.PreviewPath);}catch(Exception){return null;}
    }

    void BuildSkirmishMenu(Control stage)
    {
        EnsureClassicMaps();var map=SelectedClassicMap;var slotCount=map?.MaxPlayers??0;
        PageTitle(stage,"遭遇战游戏");skirmishCountries[0]=clientPreferences.Country;
        for(var i=0;i<Rules.MaxPlayers;i++)
        {
            var slot=i;var y=25+i*29;var available=i<slotCount;
            if(i==0)nameInput=MenuEdit(stage,"CommanderName",clientPreferences.CommanderName,"",61,y,186);
            else
            {
                var opponent=new OptionButton{Name="SkirmishOpponent"+i,Disabled=!available};
                opponent.AddItem(available?"关闭":$"地图仅支持 {slotCount} 人",0);opponent.AddItem("简单电脑",1);opponent.AddItem("普通电脑",2);opponent.AddItem("困难电脑",3);opponent.AddItem("AI",4);
                opponent.TooltipText=available?"AI 席位可由外部程序控制":map is null?"请先选择有效的官方地图":$"{map.Name} 最多支持 {slotCount} 人";
                opponent.Select(available?skirmishOpponents[i]:0);
                opponent.ItemSelected+=index=>{skirmishOpponents[slot]=(int)index;clientPreferences.CommanderName=nameInput!.Text;ShowFrontPage(FrontPage.Skirmish);};
                Place(stage,opponent,61,y,186,25);
            }
            var choice=CountryPicker(stage,295,y,147,i==0?"CountryChoice":"SkirmishCountry"+i,skirmishCountries[i]);choice.Disabled=i>0 && (!available || skirmishOpponents[i]==0);
            var flag=Place(stage,new CountryFlag{Country=skirmishCountries[i],MouseFilter=MouseFilterEnum.Ignore,Modulate=available?Colors.White:new Color(.4f,.4f,.4f)},254,y+3,32,19);
            choice.ItemSelected+=_=>{skirmishCountries[slot]=(Country)choice.GetSelectedId();flag.Country=skirmishCountries[slot];flag.QueueRedraw();if(slot==0)clientPreferences.Country=skirmishCountries[slot];};if(i==0)countryChoice=choice;
            Place(stage,new ColorRect{Color=available?SlotColor(i):new Color("353535"),MouseFilter=MouseFilterEnum.Ignore},454,y+2,38,20);
            var team=new OptionButton{Name="SkirmishTeam"+i};for(var n=0;n<Rules.MaxPlayers;n++)team.AddItem($"队 {n+1}",n);team.Select(skirmishTeams[i]);team.Disabled=choice.Disabled;team.ItemSelected+=index=>skirmishTeams[slot]=(int)index;Place(stage,team,505,y,99,25);
        }
        if(skirmishOpponents.Take(slotCount).Any(n=>n==4))MenuText(stage,"AI 席位等待外部控制程序",61,263,543,23,11);
        MenuCheck(stage,"SkirmishShortGame","短游戏",62,297,skirmishShortGame,on=>skirmishShortGame=on).TooltipText="摧毁敌方全部建筑及基地车即可获胜；关闭后还须消灭其余单位";
        MenuCheck(stage,"SkirmishMcvRepacks","基地重新部署",62,325,skirmishMcvRepacks,on=>skirmishMcvRepacks=on).TooltipText="允许建造场重新打包成可移动基地车";
        MenuCheck(stage,"SkirmishCrates","随机工具箱",62,353,skirmishCrates,on=>skirmishCrates=on).TooltipText="允许地图出现可拾取的随机补给箱";
        MenuCheck(stage,"SkirmishSuperWeapons","超级武器",62,381,skirmishSuperWeapons,on=>skirmishSuperWeapons=on).TooltipText="允许建造并使用超级武器";
        MenuCheck(stage,"SkirmishBuildNearAlly","盟友附近建设",62,409,skirmishBuildNearAlly,on=>skirmishBuildNearAlly=on).TooltipText="允许依托盟友建筑扩展基地";
        MenuText(stage,"经典开局：基地车",62,451,239,24,12,MenuDim);
        MenuText(stage,"初始资金",312,289,160,24);var credits=MenuText(stage,skirmishCredits.ToString(),526,289,77,24);
        MenuSlider(stage,"SkirmishCredits",312,317,220,5000,10000,100,skirmishCredits,n=>{skirmishCredits=(int)n;credits.Text=((int)n).ToString();});
        MenuText(stage,"初始单位",312,350,160,24);var units=MenuText(stage,skirmishStartingUnits.ToString(),526,350,77,24);
        MenuSlider(stage,"SkirmishStartingUnits",312,378,220,0,10,1,skirmishStartingUnits,n=>{skirmishStartingUnits=(int)n;units.Text=((int)n).ToString();});
        MenuText(stage,"基地车之外的初始作战单位",312,411,290,24,11,MenuDim);
        BuildSetupRail(stage,mapSeeds[0],"遭遇战游戏",true);
        var start=RailButton(stage,"StartPractice","开始游戏",306,StartConfiguredSkirmish);start.Disabled=map is null;
        RailButton(stage,"CustomizeSkirmish","自订战役",348,()=>{clientPreferences.CommanderName=nameInput!.Text;customizeReturn=FrontPage.Skirmish;ShowFrontPage(FrontPage.Customize);});
        RailButton(stage,"SkirmishBack","返回",536,()=>ShowFrontPage(FrontPage.SinglePlayer));
        if(map is null)MenuText(stage,classicMapError,61,491,543,52,11,MenuGold).AutowrapMode=TextServer.AutowrapMode.WordSmart;
    }
    void StartConfiguredSkirmish()
    {
        if(!EnsureClassicMaps() || SelectedClassicMap is not { } map){SetFrontNotice(classicMapError);return;}
        if(!StoreIdentity(false))return;
        var players=new List<PlayerSetup>();var difficulties=new List<int>();var slots=new List<int>();
        for(var i=0;i<map.MaxPlayers;i++)if(i==0 || skirmishOpponents[i]>0)
        {players.Add(new PlayerSetup(i==0?clientPreferences.CommanderName:skirmishOpponents[i]==4?$"AI 玩家 {i}":$"电脑 {i}",skirmishCountries[i],skirmishTeams[i]));difficulties.Add(i==0?1:skirmishOpponents[i]-1);slots.Add(i);}
        if(players.Count<map.MinPlayers || players.Select(p=>p.Team).Distinct().Count()<2){SetFrontNotice($"需要至少 {map.MinPlayers} 位玩家及两个不同队伍。");return;}
        practiceDifficulties=difficulties.ToArray();practiceSlots=slots.ToArray();
        var setup=new MatchSettings(mapSeeds[0],players.ToArray(),skirmishCredits,map.Width,map.Height)
        {
            MapId=map.Id,ClassicStart=true,StartingUnits=skirmishStartingUnits,ShortGame=skirmishShortGame,
            MCVRepacks=skirmishMcvRepacks,Crates=skirmishCrates,SuperWeapons=skirmishSuperWeapons,BuildNearAlly=skirmishBuildNearAlly,GameSpeed=0
        };
        StartPractice(clientPreferences.Country,setup);
    }
    void BuildSetupRail(Control stage,uint seed,string caption,bool editable)
    {
        EnsureClassicMaps();
        if(Graphics.MenuImage("setup-header") is {} header)Place(stage,new TextureRect{Texture=header,ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,MouseFilter=MouseFilterEnum.Ignore},632,0,168,290);
        else Place(stage,new ColorRect{Color=new Color("090506"),MouseFilter=MouseFilterEnum.Ignore},642,35,148,250);
        var map=frontPage==FrontPage.NetworkRoom && network is not null?FindClassicMap(network.LobbyMapId):SelectedClassicMap;
        PageTitle(stage,caption);
        Place(stage,new TextureRect{Name="SetupMapPreview",Texture=ClassicMapPreview(map),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,MouseFilter=MouseFilterEnum.Ignore},646,52,140,119);
        Place(stage,new ColorRect{Color=new Color("030203"),MouseFilter=MouseFilterEnum.Ignore},644,189,144,77);
        var mode=MenuText(stage,"作战",649,195,134,23,13);mode.HorizontalAlignment=HorizontalAlignment.Center;
        var name=MenuText(stage,map is null?"请选择有效地图":ClassicMapTitle(map),646,223,140,42,11);name.Name="SetupMapName";name.HorizontalAlignment=HorizontalAlignment.Center;name.AutowrapMode=TextServer.AutowrapMode.WordSmart;
    }
    // Called by the lobby's authoritative MapId refresh; no seed-to-map guessing.
    void RefreshClassicSetupMap(string mapId)
    {
        EnsureClassicMaps();var map=FindClassicMap(mapId);
        if(FindChild("SetupMapPreview",true,false) is TextureRect preview)preview.Texture=ClassicMapPreview(map);
        if(FindChild("SetupMapName",true,false) is Label name)name.Text=map is null?"请选择有效地图":ClassicMapTitle(map);
    }
    void BuildCustomizeMenu(Control stage)
    {
        EnsureClassicMaps();
        if(customizeReturn==FrontPage.NetworkRoom && network is not null)
        {var roomMap=Array.FindIndex(classicMaps,map=>map.Id==network.LobbyMapId);if(roomMap>=0)selectedMap=roomMap;}
        PageTitle(stage,"自订战役");MenuText(stage,"游戏模式",72,58,480,24,14);var mode=new OptionButton{Name="BattleMode"};mode.AddItem("作战");Place(stage,mode,72,91,480,25);
        MenuText(stage,"地图",72,137,480,24,14);var maps=new ItemList{Name="BattleMaps"};foreach(var map in classicMaps)maps.AddItem(ClassicMapTitle(map));if(classicMaps.Length>0)maps.Select(selectedMap);Place(stage,maps,72,170,480,246);
        var preview=Place(stage,new TextureRect{Name="CustomizeMapPreview",Texture=ClassicMapPreview(SelectedClassicMap),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,MouseFilter=MouseFilterEnum.Ignore},646,52,140,119);
        var details=MenuText(stage,ClassicMapDetails(SelectedClassicMap),72,429,480,75,12,MenuDim);details.Name="ClassicMapDetails";details.AutowrapMode=TextServer.AutowrapMode.WordSmart;
        maps.ItemSelected+=index=>{selectedMap=(int)index;preview.Texture=ClassicMapPreview(SelectedClassicMap);details.Text=ClassicMapDetails(SelectedClassicMap);};
        var use=RailButton(stage,"UseBattleMap","使用地图",306,()=>
        {
            if(SelectedClassicMap is not { } selected){SetFrontNotice(classicMapError);return;}
            if(customizeReturn==FrontPage.NetworkRoom)
            {
                if(network is not {IsHost:true})return;
                if(network.LobbyPlayers.Length>selected.MaxPlayers){SetFrontNotice($"该地图最多支持 {selected.MaxPlayers} 位玩家");return;}
                if(!network.SetLobbyMap(selected.Id)){SetFrontNotice(network.Status);return;}
            }
            ShowFrontPage(customizeReturn);
        });
        use.Disabled=SelectedClassicMap is null || customizeReturn==FrontPage.NetworkRoom && network?.IsHost!=true;
        RailButton(stage,"CustomizeBack","返回",536,()=>ShowFrontPage(customizeReturn));
    }
    static string ClassicMapDetails(ClassicMapInfo? map)=>map is null?"官方地图资源未就绪":$"{map.Name}\n{map.MinPlayers}–{map.MaxPlayers} 人 · {(map.NavalSuitable?"海陆作战":"陆地作战")} · {map.Theater}";
    public Godot.Collections.Dictionary GetSkirmishConfigurationAudit()=>new()
    {
        {"countryCount",Enum.GetValues<Country>().Length},{"mapCount",classicMaps.Length},{"mapId",SelectedClassicMap?.Id??""},
        {"maxPlayers",SelectedClassicMap?.MaxPlayers??0},{"activePlayers",SelectedClassicMap is { } map?1+skirmishOpponents.Take(map.MaxPlayers).Skip(1).Count(value=>value>0):0},
        {"startingCredits",skirmishCredits},{"startingUnits",skirmishStartingUnits},{"shortGame",skirmishShortGame},
        {"mcvRepacks",skirmishMcvRepacks},{"crates",skirmishCrates},{"superWeapons",skirmishSuperWeapons},{"buildNearAlly",skirmishBuildNearAlly},{"classicStart",true},{"mapError",classicMapError}
    };
    IEnumerable<GameCommand> PracticeOrders(RedAlert.Simulation.World world,int player)
    {
        var difficulty=practiceDifficulties.ElementAtOrDefault(player);
        if(difficulty==ExternalAiController.ExternalDifficulty)yield break;
        if(difficulty==0 && world.Tick%400>=200)yield break;
        foreach(var command in PracticeCommander.Orders(world,player))yield return command;
        if(difficulty!=2 || world.Tick%100!=70)yield break;
        var barracks=world.Buildings.FirstOrDefault(b=>b.Owner==player && b.Type==BuildingType.Barracks && b.Complete && b.Queue.Count<3);
        if(barracks is not null)yield return new GameCommand{Tick=world.Tick,Player=player,Sequence=world.Tick*10+5,Type=CommandType.Train,Actors=[barracks.Id],Unit=Rules.BasicInfantry(world.Players[player].Setup.Country)};
    }
}

public partial class CountryFlag : Control
{
    public Country Country {get;set;}
    public override void _Draw()
    {
        var rect=new Rect2(Vector2.Zero,Size);DrawRect(rect,Colors.White);
        if(Country==Country.France){DrawRect(new Rect2(0,0,Size.X/3,Size.Y),new Color("1555af"));DrawRect(new Rect2(Size.X*2/3,0,Size.X/3,Size.Y),new Color("e32a24"));}
        else if(Country==Country.Russia){DrawRect(new Rect2(0,Size.Y/3,Size.X,Size.Y/3),new Color("1c4bb4"));DrawRect(new Rect2(0,Size.Y*2/3,Size.X,Size.Y/3),new Color("de251d"));}
        else if(Country==Country.America){for(var i=0;i<7;i++)DrawRect(new Rect2(0,i*Size.Y/7,Size.X,Size.Y/14),new Color("d62520"));DrawRect(new Rect2(0,0,Size.X*.43f,Size.Y*.56f),new Color("184179"));}
        else if(Country==Country.Germany){DrawRect(new Rect2(0,0,Size.X,Size.Y/3),Colors.Black);DrawRect(new Rect2(0,Size.Y/3,Size.X,Size.Y/3),new Color("cf202e"));DrawRect(new Rect2(0,Size.Y*2/3,Size.X,Size.Y/3),new Color("f4c542"));}
        else if(Country==Country.Korea)
        {
            var center=Size/2;var radius=Size.Y*.22f;DrawCircle(center,radius,new Color("c92c3c"));
            DrawArc(center,radius,0,MathF.PI,16,new Color("1e519a"),radius,true);
            foreach(var x in new[]{Size.X*.18f,Size.X*.82f})foreach(var y in new[]{Size.Y*.24f,Size.Y*.76f})DrawLine(new(x-2,y-1),new(x+2,y+1),Colors.Black,2);
        }
        else if(Country==Country.Cuba){for(var i=0;i<5;i+=2)DrawRect(new Rect2(0,i*Size.Y/5,Size.X,Size.Y/5),new Color("184e9d"));DrawColoredPolygon([Vector2.Zero,new Vector2(Size.X*.45f,Size.Y/2),new Vector2(0,Size.Y)],new Color("d22330"));DrawCircle(new Vector2(Size.X*.14f,Size.Y/2),Size.Y*.09f,Colors.White);}
        else if(Country==Country.Libya)DrawRect(rect,new Color("11883b")); // Original game's 2000-era flag.
        else if(Country==Country.Iraq){DrawRect(new Rect2(0,0,Size.X,Size.Y/3),new Color("ce2837"));DrawRect(new Rect2(0,Size.Y*2/3,Size.X,Size.Y/3),Colors.Black);for(var i=1;i<=3;i++)DrawCircle(new Vector2(Size.X*i/4,Size.Y/2),1,new Color("168e44"));}
        else{DrawRect(rect,new Color("17407a"));DrawLine(Vector2.Zero,Size,Colors.White,4);DrawLine(new(0,Size.Y),new(Size.X,0),Colors.White,4);DrawLine(new(Size.X/2,0),new(Size.X/2,Size.Y),new Color("db201d"),5);DrawLine(new(0,Size.Y/2),new(Size.X,Size.Y/2),new Color("db201d"),5);}
    }
}
