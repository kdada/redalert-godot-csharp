using Godot;
using RedAlert.Simulation;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace RedAlert;

public partial class Main
{
    bool BattleChatTest => options.ContainsKey("battle-chat-test");
    async void RunBattleChatChecks()
    {
        var checks = new List<string>(); var error = ""; NetworkSession? host = null, ally = null, enemy = null;
        void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        async Task Frame() { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        void Poll() { host?.Poll(); ally?.Poll(); enemy?.Poll(); }
        async Task Wait(Func<bool> condition, string label)
        {
            var deadline = Time.GetTicksMsec() + 8000;
            while (!condition() && Time.GetTicksMsec() < deadline) { Poll(); await ToSignal(GetTree().CreateTimer(.01), SceneTreeTimer.SignalName.Timeout); }
            Poll(); Require(condition(), "timeout " + label);
        }
        async Task KeyPress(Key key, bool shift = false, uint unicode = 0)
        {
            using var down = new InputEventKey { Keycode = key, PhysicalKeycode = key, ShiftPressed = shift, Unicode = unicode, Pressed = true };
            using var up = new InputEventKey { Keycode = key, PhysicalKeycode = key, ShiftPressed = shift, Pressed = false };
            GetViewport().PushInput(down, true); GetViewport().PushInput(up, true); await Frame(); await Frame(); Poll();
        }
        void Raw(NetworkSession sender, long peer, WirePacket packet)
        {
            sender.Peer.SetTargetPeer((int)peer);
            Require(sender.Peer.PutPacket(JsonSerializer.SerializeToUtf8Bytes(packet, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })) == Godot.Error.Ok, "raw chat packet send");
        }
        try
        {
            using var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start(); var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
            host = new NetworkSession(); ally = new NetworkSession(); enemy = new NetworkSession();
            host.Host(port, "聊天房主", Country.America, team: 0, expected: 3);
            ally.Join("127.0.0.1", port, "聊天队友", Country.Russia, team: 0);
            await Wait(() => host.Members.Count == 2 && ally.MyPlayer == 1, "first teammate joins");
            enemy.Join("127.0.0.1", port, "聊天对手", Country.France, team: 1);
            await Wait(() => host.Members.Count == 3 && enemy.MyPlayer == 2, "opponent joins");
            Require(host.StartMatch(), "three-player match start");
            await Wait(() => host.Running && ally.Running && enemy.Running, "three real ENet peers running");
            Require(host.Chat!.MatchId == ally.Chat!.MatchId && host.Chat!.MatchId == enemy.Chat!.MatchId, "shared match scope");
            var initialHash = host.World!.StateHash(); var enemyBytes = enemy.BytesReceived;
            Require(ally.SendBattleChat("team", "队友秘密"), "ally team send");
            await Wait(() => host.Chat!.Read(0).Messages.Any(m => m.Text == "队友秘密") && ally.Chat!.Read(1).Messages.Any(m => m.Text == "队友秘密"), "team delivery");
            Require(enemy.BytesReceived == enemyBytes && !enemy.Chat!.Read(2).Messages.Any(), "team message reached opponent transport");
            Require(enemy.SendBattleChat("team", "敌方秘密"), "enemy team send");
            await Wait(() => enemy.Chat!.Read(2).Messages.Any(m => m.Text == "敌方秘密"), "opponent own team echo");
            Require(!host.Chat!.Read(0).Messages.Any(m => m.Text == "敌方秘密") && !ally.Chat!.Read(1).Messages.Any(m => m.Text == "敌方秘密"), "private enemy chat leaked to host UI/ally");
            Require(ally.SendBattleChat("all", "来宾公开") && host.SendBattleChat("all", "房主公开"), "all send");
            await Wait(() => new[] { host.Chat, ally.Chat, enemy.Chat }.All(chat => chat!.Read(chat == enemy.Chat ? 2 : chat == ally.Chat ? 1 : 0).Messages.Any(m => m.Text == "来宾公开") && chat!.Read(chat == enemy.Chat ? 2 : chat == ally.Chat ? 1 : 0).Messages.Any(m => m.Text == "房主公开")), "all delivered");
            checks.Add("three actual ENet peers; team traffic never reaches opponent wire; host authority log filtered for local UI; all messages delivered to all players");

            Raw(ally, 1, new WirePacket { Kind = "battle-chat-send", ChatMatchId = host.Chat!.MatchId, ChatChannel = "team", Message = "身份伪造检查", Slot = 2, Tick = 999999, Name = "伪造名字", Team = 1, ChatMessage = new BattleChatMessage(5000, 999999, 2, "伪造名字", 1, "all", "伪造内容") });
            await Wait(() => ally.Chat!.Read(1).Messages.Any(m => m.Text == "身份伪造检查"), "forged fields ignored");
            var authorized = host.Chat!.Read(0).Messages.Single(m => m.Text == "身份伪造检查");
            Require(authorized.SenderId == 1 && authorized.SenderName == "聊天队友" && authorized.SenderTeam == 0 && authorized.Tick == 0 && authorized.Id != 5000 && !enemy.Chat!.Read(2).Messages.Any(m => m.Text == authorized.Text), "host trusted forged identity");
            Raw(ally, 1, new WirePacket { Kind = "battle-chat-send", ChatMatchId = "old", ChatChannel = "all", Message = "过期对局" });
            await Wait(() => ally.ChatError.Length > 0, "stale match error");
            var count = ally.Chat!.Read(1).Messages.Length;
            Raw(host, host.Members.Single(m => m.Slot == 1).Peer, new WirePacket { Kind = "battle-chat", ChatMatchId = host.Chat!.MatchId, ChatMessage = authorized });
            await Wait(() => ally.RejectedPackets > 0, "duplicate authorized packet rejected");
            Require(ally.Chat!.Read(1).Messages.Length == count && host.Error.Length == 0 && ally.Error.Length == 0 && enemy.Error.Length == 0, "chat error failed match or duplicated text");
            Require(host.World.StateHash() == initialHash && ally.World!.StateHash() == initialHash && enemy.World!.StateHash() == initialHash && host.ReplayCommands.Count == 0, "chat changed canonical simulation/replay");
            checks.Add("host assigns authenticated sender/team/name/tick/ID; stale match and duplicate packets rejected without failing gameplay; chat leaves hashes and command replay unchanged");

            network = host; ShowBattle(); await Frame(); await Frame(); RefreshBattleChat();
            Require(!battleChatLog!.GetParsedText().Contains("敌方秘密", StringComparison.Ordinal), "UI rendered enemy team text");
            var tank = Battle!.Units.First(u => u.Owner == 0 && !u.Definition.Miner); battlefield!.Selection.Add(tank.Id);
            await KeyPress(Key.Enter, shift: true); Require(ChatTyping && battleChatChannel!.Text.Contains("队") && battleChatInput!.HasFocus(), "Shift+Enter team composer/focus");
            await KeyPress(Key.A, unicode: 'a'); Require(!battlefield.AttackMode && battleChatInput!.Text.Contains('a'), "chat typing activated attack shortcut or lost input");
            battleChatInput!.Text = "界面队友消息"; await KeyPress(Key.Enter);
            Require(!ChatTyping, "Enter did not submit/close composer");
            await Wait(() => ally.Chat!.Read(1).Messages.Any(m => m.Text == "界面队友消息"), "UI team reached teammate");
            Require(!enemy.Chat!.Read(2).Messages.Any(m => m.Text == "界面队友消息"), "UI team reached enemy");
            await KeyPress(Key.Enter); battleChatInput!.Text = "应取消"; await KeyPress(Key.Escape);
            Require(!ChatTyping && battlefield.Selection.Contains(tank.Id) && !host.Chat!.Read(0).Messages.Any(m => m.Text == "应取消"), "Escape leaked to battlefield or sent draft");
            chatArrivedAt = unchecked(Time.GetTicksMsec() - 13000); RefreshBattleChat();
            Require(!battleChatPanel!.Visible, "old chat did not fade");
            await KeyPress(Key.Enter);
            Require(ChatTyping && battleChatChannel!.Text.Contains("所有人"), "all chat keyboard entry");
            battleChatInput!.Text = "界面公开 [url=evil]字面文本[/url]";
            await KeyPress(Key.Enter);
            await Wait(() => enemy.Chat!.Read(2).Messages.Any(m => m.Text.StartsWith("界面公开", StringComparison.Ordinal)), "UI all reached opponent");
            RefreshBattleChat(); Require(battleChatPanel!.Visible, "fresh outgoing message stayed faded"); Require(!battleChatLog.BbcodeEnabled && battleChatLog.GetParsedText().Contains("[url=evil]", StringComparison.Ordinal), "chat interpreted text markup");
            Require(battlefield.GetGlobalRect().Encloses(battleChatPanel!.GetGlobalRect()), "chat overlay outside battlefield");
            checks.Add("real keyboard/mouse: Enter/Shift+Enter, focus, A shortcut isolation, Escape keeps selection and cancels draft, keyboard-only composer, team/all routing, literal markup and overlay bounds");

            var previousMatch = host.Chat!.MatchId; ReturnToDeployment(); ally.Dispose(); enemy.Dispose();
            Require(host.Chat is null && CurrentBattleChat is null && !ChatTyping, "chat state survived return");
            StartPractice(Country.America, MatchSettings.Practice(2)); await Frame(); await Frame();
            Require(CurrentBattleChat is not null && CurrentBattleChat.MatchId != previousMatch && CurrentBattleChat.Read(0).Messages.Length == 0, "new match retained old history");
            checks.Add("return/dispose clears history and input state; new single-player match receives fresh empty chat scope");
        }
        catch (Exception exception) { error = exception.ToString(); GD.PrintErr(error); }
        finally { network = null; host?.Dispose(); ally?.Dispose(); enemy?.Dispose(); StopExternalAi(); localWorld = null; }
        var json = JsonSerializer.Serialize(new { passed = error.Length == 0, battleChatNetworkUi = true, checks, error }, new JsonSerializerOptions { WriteIndented = true }); GD.Print(json);
        if (reportPath.Length > 0) { var path = System.IO.Path.GetFullPath(reportPath); System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!); System.IO.File.WriteAllText(path, json); }
        ended = true; reportWritten = true; GetTree().Quit(error.Length == 0 ? 0 : 2);
    }
}
