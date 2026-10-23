using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RedAlert.Simulation;

namespace RedAlert;

public sealed partial class NetworkSession
{
    public const string DefaultMapId="mp23t4";
    public string GameName {get;private set;}="局域网游戏";
    public MatchSettings LobbySettings {get;private set;}=new(20261018,[],MapId:DefaultMapId);
    public int LobbyCredits=>LobbySettings.StartingCredits;
    public uint LobbySeed=>LobbySettings.Seed;
    public string LobbyMapId=>LobbySettings.MapId;
    public int LobbyMaxPlayers=>LobbyMaps.Value.Find(LobbyMapId)?.MaxPlayers??0;
    public int LobbyMinPlayers=>LobbyMaps.Value.Find(LobbyMapId)?.MinPlayers??2;
    public string LobbyMapContent=>LobbyMaps.Value.Find(LobbyMapId)?.SourceSha256??"";
    public int LobbyRevision {get;private set;}
    public string LobbySettingsHash {get;private set;}="";
    public bool[] LobbyAcceptances {get;private set;}=[];
    public IReadOnlyList<string> LobbyChat=>lobbyChat;
    readonly List<string> lobbyChat=[];
    public bool LocalAccepted=>MyPlayer>=0 && MyPlayer<LobbyAcceptances.Length && LobbyAcceptances[MyPlayer];
    public bool AllGuestsAccepted=>LobbyAcceptances.Length==LobbyPlayers.Length && LobbyAcceptances.All(value=>value);

    // The catalog is immutable match content. World loading remains owned by the
    // core repository; this cache only parses metadata and checks wire identity.
    static readonly Lazy<ClassicMapCatalog> LobbyMaps=new(()=>
    {
        if(!Godot.FileAccess.FileExists(ClassicMapCatalog.ResourcePath))throw new InvalidDataException("官方地图目录尚未就绪");
        var catalog=ClassicMapCatalog.Parse(Godot.FileAccess.GetFileAsString(ClassicMapCatalog.ResourcePath));
        if(catalog.Maps.Any(map=>map.SourceSha256.Length!=64 || map.SourceSha256.Any(c=>!Uri.IsHexDigit(c))))
            throw new InvalidDataException("官方地图来源校验无效");
        return catalog;
    });
    static readonly Lazy<string> LobbyMapManifest=new(()=>
    {
        var identity=string.Join("\n",LobbyMaps.Value.Maps.OrderBy(map=>map.Id,StringComparer.Ordinal)
            .Select(map=>$"{map.Id}|{map.SourceSha256.ToLowerInvariant()}|{map.Width}|{map.Height}|{map.MinPlayers}|{map.MaxPlayers}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    });
    public static string MapManifestContent=>LobbyMapManifest.Value;
    public static string LobbySnapshotHash(MatchSettings settings)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(settings,Json))).ToLowerInvariant();

    void InitializeLobbySettings(uint mapSeed)
    {
        var map=LobbyMaps.Value.Find(DefaultMapId)??throw new InvalidDataException("默认官方地图不存在");
        LobbySettings=new MatchSettings(mapSeed,[],Width:map.Width,Height:map.Height,MapId:map.Id);
    }
    public static bool ValidateLobbySnapshot(MatchSettings? settings,bool starting,out string error)
    {
        error="";
        if(settings is null || settings.Players is null){error="缺少房间设置";return false;}
        var map=LobbyMaps.Value.Find(settings.MapId);
        if(map is null){error="未知的官方地图";return false;}
        if(settings.Width!=map.Width || settings.Height!=map.Height){error="地图实际尺寸与本地内容不一致";return false;}
        if(settings.Players.Length>map.MaxPlayers || starting && settings.Players.Length<map.MinPlayers)
        {error=$"{map.Name} 支持 {map.MinPlayers}–{map.MaxPlayers} 位玩家";return false;}
        if(settings.StartingCredits is <0 or >1000000 || settings.StartingUnits is <0 or >10 || !settings.ClassicStart || settings.GameSpeed!=0)
        {error="遭遇战规则设置无效";return false;}
        if(settings.Players.Any(player=>player is null || string.IsNullOrWhiteSpace(player.Name) || player.Name.Length>32 || !Enum.IsDefined(player.Country) || player.Team is <0 or >7))
        {error="房间玩家设置无效";return false;}
        if(starting && settings.Players.Select(player=>player.Team).Distinct().Count()<2){error="至少需要两个不同队伍";return false;}
        return true;
    }
    bool ApplyLobbySettings(MatchSettings candidate)
    {
        if(!IsHost || Started)return false;
        candidate=candidate with{Players=Members.OrderBy(member=>member.Slot).Select(member=>member.Player).ToArray()};
        if(!ValidateLobbySnapshot(candidate,false,out var error)){Status=error;return false;}
        LobbySettings=candidate;seed=candidate.Seed;UpdateLobby();return true;
    }
    public bool SetLobbyMap(string id)
    {
        if(!IsHost || Started)return false;
        var map=LobbyMaps.Value.Find(id);
        if(map is null){Status="未知的官方地图";return false;}
        return ApplyLobbySettings(LobbySettings with{MapId=map.Id,Width=map.Width,Height=map.Height});
    }
    public void SetLobbyGameOptions(uint mapSeed,int credits)
        =>ApplyLobbySettings(LobbySettings with{Seed=mapSeed,StartingCredits=Math.Clamp(credits,0,1000000)});
    public bool SetLobbyRules(MatchSettings rules)=>ApplyLobbySettings(LobbySettings with
    {
        StartingCredits=rules.StartingCredits,StartingUnits=rules.StartingUnits,ShortGame=rules.ShortGame,
        MCVRepacks=rules.MCVRepacks,Crates=rules.Crates,SuperWeapons=rules.SuperWeapons,BuildNearAlly=rules.BuildNearAlly,
        ClassicStart=rules.ClassicStart,GameSpeed=rules.GameSpeed
    });
    bool ValidatePacketSettings(WirePacket packet,bool starting,out string error)
    {
        if(!ValidateLobbySnapshot(packet.Settings,starting,out error))return false;
        var settings=packet.Settings!;var map=LobbyMaps.Value.Find(settings.MapId)!;
        if(packet.MapContent!=map.SourceSha256 || packet.SettingsHash!=LobbySnapshotHash(settings) || packet.LobbyRevision<1)
        {error="地图内容或房间设置校验不一致";return false;}
        return true;
    }
    void ReceiveLobbySnapshot(WirePacket packet)
    {
        if(Started){Fail("对局开始后收到了房间变更");return;}
        if(!ValidatePacketSettings(packet,false,out var error)){Fail(error);return;}
        var settings=packet.Settings!;
        if(packet.Players is null || !packet.Players.SequenceEqual(settings.Players) || packet.Acceptances is null || packet.Acceptances.Length!=settings.Players.Length
            || packet.LobbyRevision<LobbyRevision || packet.LobbyRevision==LobbyRevision && LobbySettingsHash.Length>0 && packet.SettingsHash!=LobbySettingsHash)
        {Fail("房间玩家、接受状态或设置版本不一致");return;}
        LobbySettings=settings with{Players=settings.Players.ToArray()};seed=settings.Seed;
        LobbyPlayers=LobbySettings.Players;LobbyAcceptances=packet.Acceptances.ToArray();LobbyRevision=packet.LobbyRevision;LobbySettingsHash=packet.SettingsHash;
        if(packet.Name.Length>0)GameName=CleanName(packet.Name);
        Status=$"{LobbyMaps.Value.Find(LobbyMapId)!.Name} · {LobbyPlayers.Length}/{LobbyMaxPlayers} 人";
    }
    public void SetGameName(string name)
    {
        if(!IsHost || Started)return;
        GameName=CleanName(name);UpdateLobby();
    }
    public void SetLobbyAccepted(bool accepted)
    {
        if(Started || MyPlayer<0 || IsHost || LobbyRevision<1)return;
        Send(1,new WirePacket{Kind="lobby-accept",Accepted=accepted,LobbyRevision=LobbyRevision,SettingsHash=LobbySettingsHash,MapContent=LobbyMapContent});
    }
    public void SetLobbyIdentity(Country country,int team)
    {
        if(Started || MyPlayer<0 || !Enum.IsDefined(country) || team is <-1 or >7)return;
        if(IsHost)
        {
            var member=Members.First(m=>m.Peer==1);
            member.Player=member.Player with{Country=country,Team=team<0?member.Slot:team};UpdateLobby();
        }
        else Send(1,new WirePacket{Kind="lobby-identity",Country=country,Team=team});
    }
    void ResetLobbyAcceptance(){foreach(var member in Members)member.Accepted=member.Peer==1;}
    public void SendLobbyChat(string message)
    {
        message=CleanChat(message);if(message.Length==0 || Started || MyPlayer<0)return;
        if(IsHost)RelayLobbyChat(Members.First(m=>m.Peer==1).Player.Name,message);
        else Send(1,new WirePacket{Kind="lobby-chat",Message=message});
    }
    static string CleanChat(string message)=>new(message.Trim().Where(c=>!char.IsControl(c)).Take(128).ToArray());
    void RelayLobbyChat(string name,string message)
    {
        message=CleanChat(message);if(message.Length==0)return;
        var line=CleanName(name)+"："+message;AppendLobbyChat(line);Broadcast(new WirePacket{Kind="lobby-chat",Message=line});
    }
    void AppendLobbyChat(string message)
    {
        var line=new string(message.Where(c=>!char.IsControl(c)).Take(164).ToArray());
        if(line.Length==0)return;lobbyChat.Add(line);if(lobbyChat.Count>48)lobbyChat.RemoveAt(0);
    }
}
