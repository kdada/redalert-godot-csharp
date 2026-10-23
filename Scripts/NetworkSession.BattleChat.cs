using System.Text.Json;

namespace RedAlert;

public sealed partial class NetworkSession
{
    public BattleChat? Chat {get;private set;}
    public string ChatError {get;private set;}="";

    // A client succeeds when its request is sent; the host echoes the authorized message or a chat-error.
    public bool SendBattleChat(string channel,string text)
    {
        ChatError="";
        if(!BattleChatSenderAvailable(MyPlayer,out var error)){ChatError=error;return false;}
        if(channel is not ("all" or "team")){ChatError="聊天频道无效";return false;}
        if(string.IsNullOrWhiteSpace(text)){ChatError="聊天消息不能为空";return false;}
        if(IsHost)
        {
            var member=Members.FirstOrDefault(m=>m.Peer==1 && m.Slot==MyPlayer && m.Connected);
            if(member is null){ChatError="当前玩家未连接";return false;}
            if(!AuthorizeBattleChat(member,Chat!.MatchId,channel,text,out var message,out error))
            {ChatError=error;return false;}
            RelayBattleChat(message!);return true;
        }
        var packet=new WirePacket{Kind="battle-chat-send",ChatMatchId=Chat!.MatchId,ChatChannel=channel,Message=text};
        if(SendBattleChatPacket(1,packet,out error))return true;
        ChatError=error;return false;
    }

    bool BattleChatSenderAvailable(int player,out string error)
    {
        error="";
        if(disposed || !Started || !Running || finishing || Completed || Error.Length>0 || World is null || Chat is null || World.Finished)
        {error="当前无法发送对局聊天";return false;}
        if(player<0 || player>=World.Players.Length)
        {error="当前玩家不能发送对局聊天";return false;}
        if(World.Players[player].Defeated)
        {error="已战败的玩家不能发送对局聊天";return false;}
        return true;
    }

    bool AuthorizeBattleChat(LobbyMember member,string matchId,string channel,string text,out BattleChatMessage? message,out string error)
    {
        message=null;
        if(!member.Connected){error="当前玩家未连接";return false;}
        if(!BattleChatSenderAvailable(member.Slot,out error))return false;
        if(!string.Equals(matchId,Chat!.MatchId,StringComparison.Ordinal))
        {error="聊天对局标识不匹配";return false;}
        if(channel is not ("all" or "team")){error="聊天频道无效";return false;}
        if(text is null){error="聊天消息不能为空";return false;}
        // Sender identity, team, name, message ID and tick come exclusively from the host's store/world.
        return Chat.TrySend(member.Slot,channel,text,World!.Tick,out message,out error);
    }

    void ReceiveBattleChatSend(LobbyMember member,WirePacket packet)
    {
        if(!AuthorizeBattleChat(member,packet.ChatMatchId,packet.ChatChannel,packet.Message,out var message,out var error))
        {
            RejectedPackets++;
            SendBattleChatPacket(member.Peer,new WirePacket{Kind="chat-error",ChatMatchId=Chat?.MatchId??"",Message=error},out _);
            return;
        }
        RelayBattleChat(message!);
    }

    void RelayBattleChat(BattleChatMessage message)
    {
        if(World is null || Chat is null)return;
        var packet=new WirePacket{Kind="battle-chat",ChatMatchId=Chat.MatchId,ChatMessage=message};
        foreach(var member in Members)
        {
            if(member.Peer==1 || !member.Connected || member.Slot<0 || member.Slot>=World.Players.Length)continue;
            // Never put a team message on an opponent's wire, even when the host keeps all messages.
            if(message.Channel=="team" && World.Players[member.Slot].Setup.Team!=message.SenderTeam)continue;
            SendBattleChatPacket(member.Peer,packet,out _);
        }
    }

    void ReceiveBattleChat(WirePacket packet)
    {
        if(Chat is null || !string.Equals(packet.ChatMatchId,Chat.MatchId,StringComparison.Ordinal) || packet.ChatMessage is null || !Chat.Receive(packet.ChatMessage,MyPlayer))
        {RejectedPackets++;return;}
        if(packet.ChatMessage.SenderId==MyPlayer)ChatError="";
    }

    void ReceiveBattleChatError(WirePacket packet)
    {
        if(Chat is null || !string.Equals(packet.ChatMatchId,Chat.MatchId,StringComparison.Ordinal))
        {RejectedPackets++;return;}
        ChatError=string.IsNullOrWhiteSpace(packet.Message)?"聊天发送失败":packet.Message;
    }

    // Chat failures are separate from canonical tick transport and must not fail the match.
    bool SendBattleChatPacket(long target,WirePacket packet,out string error)
    {
        error="";
        if(disposed){error="网络会话已关闭";return false;}
        var bytes=JsonSerializer.SerializeToUtf8Bytes(packet,Json);
        if(bytes.Length>60000){error="聊天消息过长";return false;}
        Peer.SetTargetPeer((int)target);
        var result=Peer.PutPacket(bytes);
        if(result!=Godot.Error.Ok){error=$"聊天发送失败：{result}";return false;}
        BytesSent+=bytes.Length;return true;
    }
}
