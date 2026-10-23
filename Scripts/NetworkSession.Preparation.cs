using RedAlert.Simulation;

namespace RedAlert;

public sealed partial class NetworkSession
{
    Task<World>? preparingWorld;
    string preparingChatId = "";
    public bool Preparing => preparingWorld is not null;
    void BeginWorldPreparation(MatchSettings settings, string chatId = "")
    {
        settings.Validate();
        if(!ValidateLobbySnapshot(settings,true,out var error) || LobbySnapshotHash(settings)!=LobbySettingsHash)
            throw new InvalidDataException(error.Length>0?error:"开局设置与房间设置不一致");
        var snapshot = settings with { Players = settings.Players.ToArray() };
        preparingChatId = chatId;
        preparingWorld = Task.Run(() => new World(snapshot));
        Status = "正在生成地图…";
    }
    void CompleteWorldPreparation()
    {
        if (disposed || Error.Length>0 || preparingWorld is not { IsCompleted: true } task) return;
        preparingWorld = null;
        try
        {
            World = task.GetAwaiter().GetResult();
            if(!ValidateLobbySnapshot(World.Settings,true,out var settingsError) || LobbySnapshotHash(World.Settings)!=LobbySettingsHash)
                throw new InvalidDataException(settingsError.Length>0?settingsError:"加载后的世界设置与房间设置不一致");
            Chat = new BattleChat(World, IsHost ? null : preparingChatId); ChatError = "";
            if (IsHost)
            {
                foreach (var member in Members) { member.Ready = member.Peer == 1; member.AckTick = 0; }
                foreach (var member in Members.Where(m => m.Peer != 1 && m.Connected))
                    Send(member.Peer, new WirePacket { Kind = "start", Slot = member.Slot, Settings = World.Settings, ChatMatchId = Chat.MatchId,
                        LobbyRevision=LobbyRevision,SettingsHash=LobbySettingsHash,MapContent=LobbyMapContent });
                Status = "等待所有玩家加载地图"; MatchStarted?.Invoke(); BeginWhenReady();
            }
            else
            {
                MatchStarted?.Invoke(); Status = "等待其他玩家加载地图";
                Send(1, new WirePacket { Kind = "ready", Hash = World.StateHash().ToString("x16"),
                    LobbyRevision=LobbyRevision,SettingsHash=LobbySettingsHash,MapContent=LobbyMapContent });
            }
        }
        catch (Exception error)
        {
            if (IsHost) Broadcast(new WirePacket { Kind = "error", Message = "地图生成失败：" + error.Message });
            Fail("地图生成失败：" + error.Message);
        }
    }
}
