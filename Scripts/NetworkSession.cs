using Godot;
using System.Text.Json;
using RedAlert.Simulation;

namespace RedAlert;

public sealed class WirePacket
{
    public string Kind {get;set;}="";
    public int Protocol {get;set;}=Rules.Protocol;
    public string Content {get;set;}=World.ContentFingerprint();
    public string MapManifest {get;set;}="";
    public string MapContent {get;set;}="";
    public string SettingsHash {get;set;}="";
    public int LobbyRevision {get;set;}
    public string Name {get;set;}="";
    public Country Country {get;set;}
    public int Team {get;set;}=-1;
    public int Slot {get;set;}=-1;
    public int Tick {get;set;}
    public string Hash {get;set;}="";
    public MatchSettings? Settings {get;set;}
    public PlayerSetup[]? Players {get;set;}
    public GameCommand[] Commands {get;set;}=[];
    public string Message {get;set;}="";
    public string ChatMatchId {get;set;}="";
    public string ChatChannel {get;set;}="";
    public BattleChatMessage? ChatMessage {get;set;}
    public bool Accepted {get;set;}
    public bool[] Acceptances {get;set;}=[];
}

public sealed class LobbyMember
{
    public long Peer;
    public int Slot;
    public required PlayerSetup Player;
    public bool Ready;
    public bool Accepted;
    public bool Connected=true;
    public int AckTick;
    public int LastSequence;
    public int LastHashTick;
    public int RateTick;
    public int RateCount;
    public bool FinalAck;
}

// ENet transports one canonical, ordered tick stream. The Godot scene tree is never replicated.
public sealed partial class NetworkSession : IDisposable
{
    public const int DefaultPort=19842;
    public const int InputDelay=3;
    public ENetMultiplayerPeer Peer {get;}=new();
    public bool IsHost {get;private set;}
    public bool Dedicated {get;private set;}
    public bool Running {get;private set;}
    public bool Completed {get;private set;}
    public bool Started => World is not null || Preparing;
    public int MyPlayer {get;private set;}=-1;
    public World? World {get;private set;}
    public string Error {get;private set;}="";
    public string Status {get;private set;}="等待连接";
    public List<LobbyMember> Members {get;}=[];
    public PlayerSetup[] LobbyPlayers {get;private set;}=[];
    public List<GameCommand> ReplayCommands {get;}=[];
    public long BytesSent {get;private set;}
    public long BytesReceived {get;private set;}
    public int SubmittedCommands {get;private set;}
    public int ReceivedCommands {get;private set;}
    public int VerifiedCheckpoints {get;private set;}
    public int TickPackets {get;private set;}
    public int RejectedPackets {get;private set;}
    public event Action? MatchStarted;
    public event Action? StateAdvanced;
    public event Action<string>? Failed;
    readonly Dictionary<int,List<GameCommand>> pending=[];
    readonly Dictionary<int,int> pendingBytes=[];
    readonly int[] lastQueuedTick=new int[Rules.MaxPlayers];
    readonly Dictionary<int,string> checkpoints=[];
    static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,MaxDepth=16};
    string localName="指挥官";
    Country localCountry;
    int localTeam=-1;
    int localSequence;
    int expectedPlayers=2;
    uint seed=20261018;
    bool autoStart;
    bool helloSent;
    bool joinStarted;
    ulong joinStartedMsec;
    bool disposed;
    bool finishing;

    public void Host(int port,string name,Country country,int team=-1,int expected=2,bool dedicated=false,bool startAutomatically=false,uint mapSeed=20261018)
    {
        IsHost=true;Dedicated=dedicated;autoStart=startAutomatically;expectedPlayers=expected;seed=mapSeed;
        if(port is <1024 or >65535 || expected is <2 or >8 || !Enum.IsDefined(country) || team is <-1 or >7)throw new ArgumentException("端口或玩家设置无效");
        InitializeLobbySettings(mapSeed);
        if(startAutomatically && expected>LobbyMaxPlayers)throw new ArgumentException($"默认地图最多支持 {LobbyMaxPlayers} 位玩家");
        _=MapManifestContent;
        var error=Peer.CreateServer(port,dedicated?8:7);
        if(error!=Godot.Error.Ok)throw new InvalidOperationException($"创建房间失败：{error}");
        Peer.TransferMode=MultiplayerPeer.TransferModeEnum.Reliable;
        Peer.PeerDisconnected+=Disconnected;
        if(!dedicated){MyPlayer=0;Members.Add(new LobbyMember{Peer=1,Slot=0,Player=new PlayerSetup(CleanName(name),country,team<0?0:team),Ready=true});}
        Status=$"房间已建立 · UDP {port}";UpdateLobby();
    }
    public void Join(string address,int port,string name,Country country,int team=-1)
    {
        if(string.IsNullOrWhiteSpace(address) || port is <1024 or >65535 || !Enum.IsDefined(country) || team is <-1 or >7)throw new ArgumentException("服务器地址、端口或玩家设置无效");
        InitializeLobbySettings(seed);_=MapManifestContent;
        localName=CleanName(name);localCountry=country;localTeam=team;
        var error=Peer.CreateClient(address.Trim(),port);
        if(error!=Godot.Error.Ok)throw new InvalidOperationException($"连接失败：{error}");
        Peer.TransferMode=MultiplayerPeer.TransferModeEnum.Reliable;
        Peer.PeerDisconnected+=Disconnected;joinStarted=true;joinStartedMsec=Time.GetTicksMsec();Status="正在连接服务器…";
    }
    static string CleanName(string name)
    {
        if(string.IsNullOrWhiteSpace(name))return "指挥官";
        var value=name.Trim();var length=Math.Min(value.Length,32);
        if(length>0 && char.IsHighSurrogate(value[length-1]))length--;
        return length==0?"指挥官":value[..length];
    }

    public void Poll()
    {
        if(disposed || Error.Length>0)return;
        if(!IsHost && joinStarted && !helloSent && Time.GetTicksMsec()-joinStartedMsec>10000){Fail("连接超时，请检查服务器地址和 UDP 端口");return;}
        if(Peer.GetConnectionStatus()==MultiplayerPeer.ConnectionStatus.Disconnected)
        {
            if(!IsHost && joinStarted && !Completed && !finishing)Fail("与服务器的连接已断开或无法建立");
            return;
        }
        Peer.Poll();CompleteWorldPreparation();
        if(Error.Length>0 || Peer.GetConnectionStatus()==MultiplayerPeer.ConnectionStatus.Disconnected)return;
        if(!IsHost && !helloSent && Peer.GetConnectionStatus()==MultiplayerPeer.ConnectionStatus.Connected)
        {
            helloSent=true;Send(1,new WirePacket{Kind="hello",Name=localName,Country=localCountry,Team=localTeam});
        }
        var read=0;
        while(Error.Length==0 && Peer.GetAvailablePacketCount()>0 && read++<512)
        {
            var sender=Peer.GetPacketPeer();var bytes=Peer.GetPacket();BytesReceived+=bytes.Length;
            if(bytes.Length is <2 or >60000){RejectedPackets++;continue;}
            var battleChatPacket=false;
            try
            {
                var packet=JsonSerializer.Deserialize<WirePacket>(bytes,Json);
                battleChatPacket=packet?.Kind is "battle-chat-send" or "battle-chat" or "chat-error";
                if(packet is null || packet.Protocol!=Rules.Protocol || packet.Content!=WorldContent || packet.Kind is null
                    || !battleChatPacket && (packet.MapManifest!=MapManifestContent || packet.Hash is null || packet.Name is null || packet.Message is null))
                {
                    RejectedPackets++;
                    if(battleChatPacket)
                    {
                        if(IsHost)SendBattleChatPacket(sender,new WirePacket{Kind="chat-error",ChatMatchId=Chat?.MatchId??"",Message="聊天协议或规则数据不一致"},out _);
                    }
                    else if(IsHost)Send(sender,new WirePacket{Kind="error",Message="游戏版本、规则或地图内容不一致"});
                    else Fail("服务器版本、规则或地图内容不一致");
                    continue;
                }
                if(IsHost)ReceiveHost(sender,packet);else if(sender==1)ReceiveClient(packet);
            }
            catch(Exception ex) when(ex is JsonException or InvalidOperationException or ArgumentException or InvalidDataException)
            {RejectedPackets++;if(!IsHost && !battleChatPacket)Fail("无效服务器数据："+ex.Message);}
        }
    }
    static readonly string WorldContent=Simulation.World.ContentFingerprint();
    void Send(long target,WirePacket packet)
    {
        packet.MapManifest=MapManifestContent;
        var bytes=JsonSerializer.SerializeToUtf8Bytes(packet,Json);
        if(bytes.Length>60000){Fail("命令包过大");return;}
        Peer.SetTargetPeer((int)target);
        var result=Peer.PutPacket(bytes);
        if(result!=Godot.Error.Ok){if(!disposed)Fail($"发送失败：{result}");return;}
        BytesSent+=bytes.Length;
    }
    void Broadcast(WirePacket packet)
    {
        foreach(var member in Members)if(member.Peer!=1 && member.Connected)Send(member.Peer,packet);
    }
    void UpdateLobby()
    {
        var ordered=Members.OrderBy(m=>m.Slot).ToArray();
        var snapshot=LobbySettings with{Seed=seed,Players=ordered.Select(m=>m.Player).ToArray()};
        if(!ValidateLobbySnapshot(snapshot,false,out var error)){Status=error;return;}
        for(var i=0;i<ordered.Length;i++)
        {
            if(ordered[i].Slot==i)continue;ordered[i].Slot=i;
            if(ordered[i].Peer==1)MyPlayer=i;else Send(ordered[i].Peer,new WirePacket{Kind="welcome",Slot=i});
        }
        var settingsHash=LobbySnapshotHash(snapshot);
        if(settingsHash!=LobbySettingsHash){LobbyRevision++;LobbySettingsHash=settingsHash;ResetLobbyAcceptance();}
        LobbySettings=snapshot;LobbyPlayers=snapshot.Players;
        LobbyAcceptances=ordered.Select(m=>m.Peer==1 || m.Accepted).ToArray();
        Broadcast(new WirePacket{Kind="lobby",Players=LobbyPlayers,Acceptances=LobbyAcceptances,Name=GameName,Settings=snapshot,
            LobbyRevision=LobbyRevision,SettingsHash=LobbySettingsHash,MapContent=LobbyMapContent});
    }
    void ReceiveHost(long sender,WirePacket packet)
    {
        var member=Members.FirstOrDefault(m=>m.Peer==sender && m.Connected);
        if(packet.Kind=="hello")
        {
            if(member is not null)return;
            if(Started || Members.Count>=LobbyMaxPlayers || !Enum.IsDefined(packet.Country) || packet.Team is < -1 or >7)
            {Send(sender,new WirePacket{Kind="error",Message=$"房间已开始、玩家设置无效，或地图已满（最多 {LobbyMaxPlayers} 人）"});return;}
            var slot=Enumerable.Range(0,8).First(i=>Members.All(m=>m.Slot!=i));
            Members.Add(new LobbyMember{Peer=sender,Slot=slot,Player=new PlayerSetup(CleanName(packet.Name),packet.Country,packet.Team<0?slot:packet.Team)});
            Send(sender,new WirePacket{Kind="welcome",Slot=slot});UpdateLobby();
            if(autoStart && Members.Count==expectedPlayers)StartMatch();
            return;
        }
        if(member is null){RejectedPackets++;return;}
        switch(packet.Kind)
        {
            case "lobby-accept":
                if(!Started)
                {
                    member.Accepted=packet.Accepted && packet.LobbyRevision==LobbyRevision && packet.SettingsHash==LobbySettingsHash && packet.MapContent==LobbyMapContent;
                    UpdateLobby();
                }
                break;
            case "lobby-identity":
                if(!Started && Enum.IsDefined(packet.Country) && packet.Team is >=-1 and <=7)
                {member.Player=member.Player with{Country=packet.Country,Team=packet.Team<0?member.Slot:packet.Team};UpdateLobby();}break;
            case "lobby-chat":
                if(!Started)RelayLobbyChat(member.Player.Name,packet.Message);break;
            case "battle-chat-send":ReceiveBattleChatSend(member,packet);break;
            case "ready":
                if(World is null || packet.Hash!=World.StateHash().ToString("x16") || packet.LobbyRevision!=LobbyRevision || packet.SettingsHash!=LobbySettingsHash || packet.MapContent!=LobbyMapContent)
                {
                    var reason=$"玩家 {member.Slot+1} 初始状态或开局设置不一致";
                    Broadcast(new WirePacket{Kind="error",Message=reason});Fail(reason);return;
                }
                member.Ready=true;BeginWhenReady();break;
            case "orders":
                if(finishing)return; // In-flight player input can arrive after the final canonical frame.
                if(!Running || World is null || packet.Commands is null || packet.Commands.Length>32){RejectedPackets++;return;}
                if(member.RateTick!=World.Tick){member.RateTick=World.Tick;member.RateCount=0;}
                foreach(var c in packet.Commands)
                {
                    if(c is null || c.Sequence<=member.LastSequence || c.Actors is null || c.Actors.Length>Rules.MaxEntities || ++member.RateCount>64){RejectedPackets++;continue;}
                    member.LastSequence=c.Sequence;
                    Queue(c with{Player=member.Slot,Tick=World.Tick+InputDelay});ReceivedCommands++;
                }
                break;
            case "ack":
                if(World is null || packet.Tick<member.AckTick || packet.Tick>World.Tick)return;
                member.AckTick=packet.Tick;
                if(packet.Hash.Length>0 && packet.Tick>member.LastHashTick)
                {
                    if(!checkpoints.TryGetValue(packet.Tick,out var expected) || expected!=packet.Hash){Fail($"玩家 {member.Slot+1} 在 tick {packet.Tick} 不同步");return;}
                    member.LastHashTick=packet.Tick;VerifiedCheckpoints++;
                }
                break;
            case "final":
                if(!finishing || World is null || packet.Tick!=World.Tick || packet.Hash!=World.StateHash().ToString("x16")){Fail("最终状态不一致");return;}
                member.FinalAck=true;VerifiedCheckpoints++;CompleteWhenAcknowledged();break;
        }
    }
    public bool StartMatch(bool requireLobbyAcceptance=false)
    {
        if(!IsHost || Started || Error.Length>0)return false;
        UpdateLobby();
        var snapshot=LobbySettings with{Players=Members.OrderBy(member=>member.Slot).Select(member=>member.Player).ToArray()};
        if(!ValidateLobbySnapshot(snapshot,true,out var error)){Status=error;return false;}
        if(requireLobbyAcceptance && Members.Any(m=>m.Peer!=1 && !m.Accepted)){Status="等待所有玩家接受当前设置";return false;}
        BeginWorldPreparation(snapshot);return true;
    }
    void BeginWhenReady()
    {
        if(Running || Error.Length>0 || World is null || Members.Any(m=>m.Connected && !m.Ready))return;
        Running=true;Status="对战进行中";Broadcast(new WirePacket{Kind="run"});
    }
    void ReceiveClient(WirePacket packet)
    {
        switch(packet.Kind)
        {
            case "welcome":
                if(packet.Slot is <0 or >=Rules.MaxPlayers){Fail("无效的玩家席位");return;}
                MyPlayer=packet.Slot;Status="已加入房间，等待开始";break;
            case "lobby":ReceiveLobbySnapshot(packet);break;
            case "lobby-chat":AppendLobbyChat(packet.Message);break;
            case "battle-chat":ReceiveBattleChat(packet);break;
            case "chat-error":ReceiveBattleChatError(packet);break;
            case "start":
                if(Started){Fail("重复开局数据");return;}
                if(!ValidatePacketSettings(packet,true,out var startError)){Fail(startError);return;}
                if(packet.Slot<0 || packet.Slot>=packet.Settings!.Players.Length || packet.Slot!=MyPlayer || string.IsNullOrWhiteSpace(packet.ChatMatchId)
                    || packet.LobbyRevision!=LobbyRevision || packet.SettingsHash!=LobbySettingsHash)
                {Fail("开局数据与已同步的房间设置不一致");return;}
                MyPlayer=packet.Slot;BeginWorldPreparation(packet.Settings,packet.ChatMatchId);break;
            case "run":if(World is not null){Running=true;Status="对战进行中";}break;
            case "tick":
                if(!Running || World is null || packet.Tick!=World.Tick || packet.Commands is null){Fail($"命令帧不连续：收到 {packet.Tick}，预期 {World?.Tick}");return;}
                World.Step(packet.Commands);ReplayCommands.AddRange(packet.Commands);TickPackets++;
                if(packet.Hash.Length>0)
                {
                    if(World.StateHash().ToString("x16")!=packet.Hash){Fail($"tick {World.Tick} 状态校验失败");return;}
                    VerifiedCheckpoints++;
                }
                StateAdvanced?.Invoke();
                if(World.Tick%20==0)Send(1,new WirePacket{Kind="ack",Tick=World.Tick,Hash=World.StateHash().ToString("x16")});
                break;
            case "finish":
                if(World is null || packet.Tick!=World.Tick || packet.Hash!=World.StateHash().ToString("x16")){Fail("最终状态校验失败");return;}
                Running=false;finishing=true;Send(1,new WirePacket{Kind="final",Tick=World.Tick,Hash=packet.Hash});Status="最终状态已验证";break;
            case "goodbye":Completed=true;Status="联机测试完成";break;
            case "error":Fail(packet.Message);break;
        }
    }
    public void Submit(GameCommand command)
    {
        if(!Running || World is null || MyPlayer<0)return;
        var c=command with{Player=MyPlayer,Sequence=++localSequence,Tick=World.Tick+InputDelay};SubmittedCommands++;
        if(IsHost)Queue(c);else Send(1,new WirePacket{Kind="orders",Commands=[c]});
    }
    void Queue(GameCommand command)
    {
        if(World is null || command.Player<0 || command.Player>=Rules.MaxPlayers){RejectedPackets++;return;}
        var bytes=JsonSerializer.SerializeToUtf8Bytes(command,Json).Length+1;
        if(bytes>45000){RejectedPackets++;return;}
        // Bound the full canonical batch; preserve each player's input order when a busy frame spills.
        var tick=Math.Max(command.Tick,lastQueuedTick[command.Player]);
        while(tick<=World.Tick+40)
        {
            if(pendingBytes.GetValueOrDefault(tick)+bytes<=45000 && (!pending.TryGetValue(tick,out var existing) || existing.Count<128))
            {
                if(!pending.TryGetValue(tick,out var list))pending[tick]=list=[];
                list.Add(command with{Tick=tick});pendingBytes[tick]=pendingBytes.GetValueOrDefault(tick)+bytes;lastQueuedTick[command.Player]=tick;return;
            }
            tick++;
        }
        RejectedPackets++;
    }
    public bool StepServer()
    {
        if(!IsHost || !Running || World is null)return false;
        var remote=Members.Where(m=>m.Peer!=1 && m.Connected).ToArray();
        if(remote.Length>0 && World.Tick-remote.Min(m=>m.AckTick)>=80)return false;
        pendingBytes.Remove(World.Tick);pending.Remove(World.Tick,out var orders);var commands=orders?.OrderBy(c=>c.Player).ThenBy(c=>c.Sequence).ToArray()??[];
        var tick=World.Tick;World.Step(commands);ReplayCommands.AddRange(commands);TickPackets++;
        var hash=World.Tick%20==0?World.StateHash().ToString("x16"):"";
        if(hash.Length>0)checkpoints[World.Tick]=hash;
        Broadcast(new WirePacket{Kind="tick",Tick=tick,Commands=commands,Hash=hash});StateAdvanced?.Invoke();
        if(checkpoints.Count>128){var min=remote.Length>0?remote.Min(m=>m.LastHashTick):World.Tick;foreach(var key in checkpoints.Keys.Where(k=>k<min-40).ToArray())checkpoints.Remove(key);}
        if(World.Finished)Finish();
        return true;
    }
    public void Finish()
    {
        if(!IsHost || World is null || finishing)return;
        Running=false;finishing=true;Broadcast(new WirePacket{Kind="finish",Tick=World.Tick,Hash=World.StateHash().ToString("x16")});CompleteWhenAcknowledged();
    }
    void CompleteWhenAcknowledged()
    {
        if(Members.Any(m=>m.Peer!=1 && m.Connected && !m.FinalAck))return;
        Broadcast(new WirePacket{Kind="goodbye"});Completed=true;Status="所有客户端最终状态一致";
    }
    void Disconnected(long id)
    {
        if(disposed || Completed || Error.Length>0)return;
        if(!IsHost){if(!finishing)Fail("与服务器的连接已断开");return;}
        var member=Members.FirstOrDefault(m=>m.Peer==id);
        if(member is null)return;
        if(Started && !Running && !finishing)
        {
            var reason="地图加载期间有玩家离开，请重新建立对局";
            Broadcast(new WirePacket{Kind="error",Message=reason});Fail(reason);return;
        }
        if(World is null){Members.Remove(member);UpdateLobby();return;}
        member.Connected=false;
        if(!finishing)
        {
            Queue(new GameCommand{Tick=World.Tick+InputDelay,Player=member.Slot,Sequence=int.MaxValue,Type=CommandType.Surrender});
            BeginWhenReady();Status=$"玩家 {member.Slot+1} 掉线，按投降处理";
        }
        else CompleteWhenAcknowledged();
    }
    public ReplayFile Replay()=>new(){Settings=World!.Settings,Commands=ReplayCommands,FinalTick=World.Tick,FinalHash=World.StateHash().ToString("x16")};
    void Fail(string message)
    {
        if(Error.Length>0)return;Error=message;Running=false;Status=message;
        if(!IsHost && !disposed)Peer.Close();
        Failed?.Invoke(message);
    }
    public void Dispose(){if(disposed)return;disposed=true;Chat=null;ChatError="";Peer.Close();Peer.Dispose();}
}
