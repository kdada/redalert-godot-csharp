using Godot;
using RedAlert.Simulation;
using System.Diagnostics;
using System.Text.Json;
using SimWorld=RedAlert.Simulation.World;

namespace RedAlert;

public partial class Main : Control
{
    public SimWorld? Battle => network?.World??localWorld;
    public int LocalPlayer => network?.MyPlayer??0;
    ArtLibrary? art;
    public ArtLibrary Graphics => art??=new ArtLibrary();
    NetworkSession? network;
    SimWorld? localWorld;
    readonly List<GameCommand> localPending=[];
    readonly List<GameCommand> localReplay=[];
    readonly Dictionary<string,string> options=[];
    readonly Stopwatch elapsed=Stopwatch.StartNew();
    bool headless;
    bool fast;
    bool bot;
    bool ended;
    bool reportWritten;
    double accumulator;
    double doneDelay;
    double hudTimer;
    int targetTicks=2400;
    int sequence;
    string failure="";
    string notice="准备就绪";
    string reportPath="";
    bool UiTest=>options.ContainsKey("ui-test");

    public override void _Ready()
    {
        foreach(var arg in OS.GetCmdlineUserArgs())
        {
            if(!arg.StartsWith("--"))continue;
            var split=arg[2..].Split('=',2);options[split[0]]=split.Length==2?split[1]:"true";
        }
        headless=DisplayServer.GetName()=="headless";fast=options.ContainsKey("fast");bot=options.ContainsKey("bot");
        clientPreferences=ClientPreferences.Load();if(!headless)clientPreferences.ApplyDisplay(GetWindow());InitWindowInput();InitGameAudio();AudioMenu();
        targetTicks=Number("ticks",UiTest?240:options.ContainsKey("smoke")?120:2400,1,2_000_000);reportPath=Option("report","");
        try
        {
            InitClassicRuntime();
            if(options.ContainsKey("network-tests"))
            {
                var json=JsonSerializer.Serialize(HeadlessNetworkChecks.Run(),new JsonSerializerOptions{WriteIndented=true});GD.Print(json);
                if(reportPath.Length>0){var path=System.IO.Path.GetFullPath(reportPath);System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);System.IO.File.WriteAllText(path,json);}
                ended=true;reportWritten=true;GetTree().Quit();return;
            }
            if(options.TryGetValue("verify-replay",out var replayPath))
            {
                localWorld=ReplayFile.Load(replayPath).Play();ended=true;WriteReport();GD.Print($"REPLAY VERIFIED tick={localWorld.Tick} hash={localWorld.StateHash():x16}");GetTree().Quit(failure.Length==0?0:2);return;
            }
            if(options.ContainsKey("server"))
            {
                SetupNetwork();network!.Host(Number("port",NetworkSession.DefaultPort,1024,65535),Option("name","主机"),ParseCountry(),expected:Number("players",2,2,8),dedicated:options.ContainsKey("dedicated"),startAutomatically:headless,mapSeed:(uint)Number("seed",20261018,0,int.MaxValue));
                GD.Print($"SERVER LISTEN port={Number("port",NetworkSession.DefaultPort,1024,65535)} expected={Number("players",2,2,8)} dedicated={options.ContainsKey("dedicated")}");
                if(!headless)BuildFrontEnd();
            }
            else if(options.TryGetValue("join",out var address))
            {
                SetupNetwork();network!.Join(address,Number("port",NetworkSession.DefaultPort,1024,65535),Option("name","指挥官"),ParseCountry());
                if(!headless)BuildFrontEnd();
            }
            else if(options.ContainsKey("terrain-inspect"))
            {
                OpenTerrainInspection(Option("feature","flat"),.25f);
            }
            else if(options.ContainsKey("radar-navigation-check"))
            {
                BuildFrontEnd();CallDeferred(nameof(RunRadarNavigationChecks));
            }
            else if(options.ContainsKey("battle-view-check"))
            {
                BuildFrontEnd();CallDeferred(nameof(RunBattleViewChecks));
            }
            else if(options.ContainsKey("presentation-check"))
            {
                BuildFrontEnd();CallDeferred(nameof(RunPresentationChecks));
            }
            else if(options.ContainsKey("experience-check"))
            {
                BuildFrontEnd();CallDeferred(nameof(RunExperienceChecks));
            }
            else if(BattleChatTest)
            {
                BuildFrontEnd();CallDeferred(nameof(RunBattleChatChecks));
            }
            else if(FrontEndTest)
            {
                BuildFrontEnd();CallDeferred(nameof(RunFrontEndChecks));
            }
            else if(headless)
            {
                localWorld=new SimWorld(MatchSettings.Practice(Number("players",4,2,8)));ConfigureHeadlessAi(localWorld);fast=externalAi is null || options.ContainsKey("fast");
                if(UiTest){BuildFrontEnd();ShowBattle();ValidateUi();}
                GD.Print($"HEADLESS SMOKE players={Battle!.Players.Length}");
            }
            else BuildFrontEnd();
        }
        catch(Exception ex){Fail(ex.ToString());}
    }
    string Option(string key,string fallback)=>options.TryGetValue(key,out var value)?value:fallback;
    int Number(string key,int fallback,int min,int max)=>options.TryGetValue(key,out var text)&&int.TryParse(text,out var n)?Math.Clamp(n,min,max):fallback;
    Country ParseCountry()=>Enum.TryParse<Country>(Option("country","America"),true,out var c)&&Enum.IsDefined(c)?c:Country.America;
    void SetupNetwork()
    {
        failure="";notice="准备就绪";accumulator=0;
        StopExternalAi();network?.Dispose();network=new NetworkSession();
        network.MatchStarted+=()=>{if(!headless || UiTest || FrontEndTest)ShowBattle();};
        network.StateAdvanced+=()=>
        {
            if(Battle is null)return;
            if(bot && LocalPlayer>=0)foreach(var c in PracticeCommander.Orders(Battle,LocalPlayer))network.Submit(c);
            AudioCaptureTick();if(!headless || UiTest)battlefield?.CaptureEvents();
        };
        network.Failed+=Fail;
    }
    public override void _Process(double delta)
    {
        UpdateWindowInput();
        if(terrainInspection){ProcessTerrainInspection(delta);return;}
        if(ended)
        {
            doneDelay+=delta;network?.Poll();
            if(doneDelay>.4)GetTree().Quit(failure.Length==0?0:2);
            return;
        }
        try
        {
            if(!headless || FrontEndTest)PollLanDiscovery();
            network?.Poll();CompleteLocalPreparation();PumpExternalAi();
            if(UiTest && ++uiFrames==3)ExerciseUiInput();
            if(headless && network?.Completed==true){End();return;}
            if(headless && elapsed.Elapsed.TotalSeconds>Number("timeout",120,5,3600)){Fail("headless 测试超时："+(network?.Status??"local"));return;}
            if(network is {IsHost:true,Running:true})
            {
                accumulator+=delta;var count=0;
                while(count++<(fast?12:5) && (fast || accumulator>=1.0/Rules.TicksPerSecond))
                {
                    if(!network.StepServer())break;
                    accumulator=Math.Max(0,accumulator-1.0/Rules.TicksPerSecond);
                    if(headless && Battle!.Tick>=targetTicks){network.Finish();break;}
                }
            }
            else if(localWorld is not null && !fullPlaythroughPaused && (!localWorld.Finished || headless) && (!MatchMenuOpen || headless))
            {
                accumulator+=delta;var count=0;var stepSeconds=1.0/Rules.TicksPerSecond/(headless?1:Math.Pow(1.25,clientPreferences.GameSpeed-3));
                while(count++<(fast?12:5) && (fast || accumulator>=stepSeconds))
                {
                    var commands=new List<GameCommand>(localPending);localPending.Clear();
                    if(externalAi is not null)commands.AddRange(externalAi.DrainCommands(localWorld.Tick));
                    if(!fullPlaythroughControlsEnemy)for(var p=headless&&bot?0:1;p<localWorld.Players.Length;p++)commands.AddRange(PracticeOrders(localWorld,p));
                    localReplay.AddRange(commands);
                    if(externalAi is null)localWorld.Step(commands);else localWorld.Step(commands,externalAi.CommandApplied);
                    accumulator=Math.Max(0,accumulator-stepSeconds);
                    AudioCaptureTick();ObserveFullPlaythroughTick(localWorld);if(!headless || UiTest)battlefield?.CaptureEvents();
                    if(headless && localWorld.Tick>=targetTicks){End();break;}
                    if(localWorld.Finished && !headless){accumulator=0;break;}
                }
            }
            if(!headless || UiTest || FrontEndTest)
            {
                ObserveBattleLifecycle();UpdateCamera(delta);hudTimer+=delta;
                if(hudTimer>.15){hudTimer=0;RefreshHud();}
                battlefield?.QueueRedraw();minimap?.QueueRedraw();
            }
        }
        catch(Exception ex){Fail(ex.ToString());}
    }
    public void Issue(GameCommand command)
    {
        if(Battle is null || LocalPlayer<0)return;
        var player=Battle.Players[LocalPlayer];
        if(Battle.Finished || player.Defeated || MatchMenuOpen){Notify("当前为观战或菜单状态，不能下达作战指令");return;}
        AudioSelection(true);
        if(network is not null)network.Submit(command);
        else localPending.Add(command with{Tick=Battle.Tick,Player=LocalPlayer,Sequence=++sequence});
    }
    void StartPractice(Country country,MatchSettings? configured=null)
    {
        failure="";notice="准备就绪";sequence=0;
        var setup=configured??MatchSettings.Practice(4);setup.Players[0]=setup.Players[0] with{Country=country,Name=clientPreferences.CommanderName};
        BeginLocalPreparation(setup);
    }
    void OpenHost(string name,Country country,int port,int team)
    {
        try{SetupNetwork();network!.Host(port,name,country,team);notice=$"已建房，其他玩家连接本机地址的 UDP {port} 端口";RefreshLobby();}
        catch(Exception ex){network?.Dispose();network=null;notice=ex.Message;RefreshLobby();}
    }
    void OpenJoin(string address,string name,Country country,int port,int team)
    {
        try{SetupNetwork();network!.Join(address,port,name,country,team);notice="正在连接…";RefreshLobby();}
        catch(Exception ex){network?.Dispose();network=null;notice=ex.Message;RefreshLobby();}
    }
    public void Notify(string text){notice=text;}
    void Fail(string error)
    {
        if(failure.Length>0)return;
        failure=error;notice=error;GD.PushError(error);
        if(headless){ended=true;WriteReport();}
    }
    void End()
    {
        if(ended)return;
        if(UiTest)VerifyUiOutcome();
        ended=true;WriteReport();
        GD.Print($"TEST COMPLETE role={(network is null?"local":network.IsHost?"server":"client")} tick={Battle?.Tick} hash={Battle?.StateHash():x16}");
    }
    void WriteReport()
    {
        if(reportWritten)return;reportWritten=true;
        var world=Battle;var replayVerified=false;string? replayPath=null;
        if(world is not null && failure.Length==0)
        {
            try
            {
                if(!options.ContainsKey("verify-replay"))
                {
                    var replay=network?.Replay()??new ReplayFile{Settings=world.Settings,Commands=localReplay,FinalTick=world.Tick,FinalHash=world.StateHash().ToString("x16")};
                    replayVerified=replay.Play().StateHash()==world.StateHash();
                    if(reportPath.Length>0){replayPath=System.IO.Path.ChangeExtension(reportPath,"replay.json");replay.Save(replayPath);}
                }
                else replayVerified=true;
            }
            catch(Exception ex){failure="录像验证失败："+ex.Message;}
        }
        var report=new{passed=failure.Length==0,uiTest=UiTest,uiInputVerified,drawCalls=battlefield?.DrawCalls,spriteAtlases=art?.SpriteCount,vehicleAtlases=art?.VehicleCount,textures=art?.TexturesCreated,role=network is null?"local":network.IsHost?network.Dedicated?"dedicated":"server":"client",player=LocalPlayer,tick=world?.Tick,hash=world?.StateHash().ToString("x16"),players=world?.Players.Length,countries=world?.Players.Select(p=>p.Setup.Country.ToString()).ToArray(),accepted=world?.AcceptedCommands,rejected=world?.RejectedCommands,submitted=network?.SubmittedCommands,received=network?.ReceivedCommands,checkpoints=network?.VerifiedCheckpoints,tickPackets=network?.TickPackets,bytesSent=network?.BytesSent,bytesReceived=network?.BytesReceived,rejectedPackets=network?.RejectedPackets,elapsedSeconds=elapsed.Elapsed.TotalSeconds,replayVerified,replayPath,content=SimWorld.ContentFingerprint(),error=failure};
        GD.Print(JsonSerializer.Serialize(report));
        if(reportPath.Length>0)
        {
            var path=System.IO.Path.GetFullPath(reportPath);System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        }
    }
    void ValidateUi()
    {
        if(Battle is null || battlefield is null || minimap is null)throw new InvalidOperationException("战场界面未创建");
        if(GetTree().Root.GetNodeOrNull("Command")==null)throw new InvalidOperationException("主场景不存在");
        var count=FindChildren("*","Button",true,false).Count;
        if(count<8)throw new InvalidOperationException($"操作按钮不足：{count}");
        Graphics.Validate();Graphics.ValidateClassic();
        var ground=Graphics.GroundPlan(Battle.Map,Battle.Settings.Seed);
        GD.Print($"HEADLESS UI STRUCTURE VERIFIED buttons={count} spriteAtlases={Graphics.SpriteCount} vehicleAtlases={Graphics.VehicleCount} textures={Graphics.TexturesCreated} terrainTiles={Graphics.TerrainTileCount} interfaceFrames={Graphics.InterfaceFrameCount} transitions={ground.TransitionCells} shores={ground.ShoreCells}");
    }
    public Godot.Collections.Dictionary GetClassicRenderAudit()
    {
        var result=new Godot.Collections.Dictionary{{"terrainTiles",Graphics.TerrainTileCount},{"interfaceFrames",Graphics.InterfaceFrameCount},{"spriteAtlases",Graphics.SpriteCount},{"textures",Graphics.TexturesCreated}};
        if(Battle is null)return result;
        var plan=Graphics.GroundPlan(Battle.Map,Battle.Settings.Seed);
        result["transitions"]=plan.TransitionCells;result["shoreCells"]=plan.ShoreCells;result["thickGrassCells"]=plan.ThickCells;result["roughGrassCells"]=plan.RoughCells;result["coastalSandCells"]=plan.SandCells;
        result["worldHash"]=Battle.StateHash().ToString("x16");result["tick"]=Battle.Tick;
        return result;
    }
    public override void _ExitTree(){StopWindowInput();CancelLocalPreparation();StopGameAudio();StopExternalAi();StopLanDiscovery();network?.Dispose();Theme=null;uiTheme?.Dispose();uiTheme=null;art?.Dispose();art=null;}
}
