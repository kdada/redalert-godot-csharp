using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using RedAlert.Simulation;

namespace RedAlert.Tests;

public static class BattleChatChecks
{
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static int Run(string[] args)
    {
        var checks = new List<string>(); var error = "";
        try
        {
            var settings = MatchSettings.Practice(3);
            settings.Players[1] = settings.Players[1] with { Team = 0 };
            var world = new World(settings); var chat = new BattleChat(world); var api = new ExternalAiController(world, [1, 2], chat: chat);
            var initial = world.StateHash();
            using var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start(); var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
            using var server = new ExternalAiHttpServer(port);
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(10) };
            (int Status, JsonElement Json) Send(int player, string suffix = "", object? body = null)
            {
                var url = server.BaseUrl + $"api/v1/players/{player}/messages" + suffix;
                var task = body is null ? client.GetAsync(url) : client.PostAsJsonAsync(url, body, ExternalAiJson.Options);
                var timer = Stopwatch.StartNew();
                while (!task.IsCompleted && timer.Elapsed.TotalSeconds < 10) { server.Pump(api.Handle); Thread.Sleep(1); }
                using var response = task.GetAwaiter().GetResult(); using var document = JsonDocument.Parse(response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult());
                return ((int)response.StatusCode, document.RootElement.Clone());
            }
            object Message(string id, string channel, string text) => new { matchId = api.MatchId, requestId = id, channel, text };
            var team = Send(1, body: Message("team-1", "team", "请求队友支援")); Require(team.Status == 201, "team POST");
            var shared = Send(1, body: Message("all-1", "all", "公开消息 [url=evil]text[/url]")); Require(shared.Status == 201, "all POST");
            var privateEnemy = Send(2, body: Message("enemy-team", "team", "敌人秘密")); Require(privateEnemy.Status == 201, "opponent team POST");
            Require(chat.TrySend(0, "team", "人类请求协作", world.Tick, out _, out _), "human shared store send");
            Require(chat.Read(0).Messages.Any(m => m.Text == "请求队友支援") && !chat.Read(0).Messages.Any(m => m.Text == "敌人秘密"), "host UI filtering");
            var ally = Send(1).Json.GetProperty("messages").EnumerateArray().ToArray(); var enemy = Send(2).Json.GetProperty("messages").EnumerateArray().ToArray();
            Require(ally.Length == 3 && ally.All(m => m.GetProperty("text").GetString() != "敌人秘密"), "AI allied query privacy");
            Require(enemy.Length == 2 && enemy.All(m => m.GetProperty("text").GetString() is not ("请求队友支援" or "人类请求协作")), "enemy AI query privacy");
            Require(Send(0).Status == 403 && Send(0, body: Message("spoof-human", "all", "spoof")).Status == 403, "human seat takeover");
            Require(world.StateHash() == initial && api.DrainCommands(world.Tick).Length == 0, "chat changed simulation/hash/command stream");
            checks.Add("real HTTP all/team messaging; human/AI shared store; same-team visibility and opponent filtering; simulation unchanged");

            var page1 = Send(1, "?after=0&limit=1"); Require(page1.Status == 200 && page1.Json.GetProperty("messages").GetArrayLength() == 1 && page1.Json.GetProperty("hasMore").GetBoolean(), "first page");
            var cursor = page1.Json.GetProperty("nextCursor").GetInt64();
            var page2 = Send(1, $"?after={cursor}&limit=100&matchId={api.MatchId}");
            Require(page2.Status == 200 && page2.Json.GetProperty("messages").GetArrayLength() == 2 && page2.Json.GetProperty("messages").EnumerateArray().All(m => m.GetProperty("id").GetInt64() > cursor), "HTTP query cursor ignored or duplicated");
            var next = page2.Json.GetProperty("nextCursor").GetInt64(); Require(Send(1, $"?after={next}").Json.GetProperty("messages").GetArrayLength() == 0, "cursor no-new page");
            Require(Send(1, "?after=-1").Status == 400 && Send(1, "?limit=101").Status == 400 && Send(1, "?after=0&after=1").Status == 400 && Send(1, "?matchId=old").Status == 409, "invalid query accepted");
            checks.Add("HTTP query preservation; filtered pagination/cursors, no-new pages, duplicate/negative/out-of-range cursors and stale match handling");

            var retry = Send(1, body: Message("team-1", "team", "请求队友支援")); Require(retry.Status == 200 && retry.Json.GetProperty("message").GetProperty("id").GetInt64() == team.Json.GetProperty("message").GetProperty("id").GetInt64(), "deduplication");
            Require(Send(1, body: Message("team-1", "all", "不同内容")).Status == 409, "request ID collision accepted");
            Require(Send(1, body: new { matchId = api.MatchId, requestId = "forged", channel = "all", text = "x", senderId = 2 }).Status == 400, "sender injection accepted");
            Require(Send(1, body: new { matchId = "old", requestId = "stale", channel = "all", text = "x" }).Status == 409, "stale match accepted");
            Require(Send(1, body: Message("invalid", "private", "x")).Status == 400 && Send(1, body: Message("empty", "all", "\u0001\u202e")).Status == 400, "invalid channel/empty control text accepted");
            Require(Send(1, body: Message("burst-3", "all", "third")).Status == 201 && Send(1, body: Message("burst-4", "all", "fourth")).Status == 201 && Send(1, body: Message("burst-5", "all", "fifth")).Status == 429, "sliding rate limit");
            Require(Send(1, body: Message("team-1", "team", "请求队友支援")).Status == 200, "rate limit broke idempotent retry");
            for (var i = 0; i < Rules.TicksPerSecond; i++) world.Step();
            var emoji = string.Concat(Enumerable.Repeat("😀", 280));
            Require(Send(1, body: Message("unicode", "all", emoji)).Status == 201 && Send(1, body: Message("too-long", "all", emoji + "😀")).Status == 400, "Unicode scalar boundary");
            Require(Send(1, body: Message("clean", "all", " a\nb\u202e ")).Json.GetProperty("message").GetProperty("text").GetString() == "a b", "control normalization");
            checks.Add("requestId deduplication/conflict; identity spoofing and stale match rejected; sliding rate limit, Unicode length and text normalization");

            var receiver = new BattleChat(world, chat.MatchId);
            var enemyPrivate = new BattleChatMessage(999, 0, 2, settings.Players[2].Name, settings.Players[2].Team, "team", "private");
            Require(!receiver.Receive(enemyPrivate, 1), "receiver accepted enemy private text");
            Require(!receiver.Receive(enemyPrivate with { SenderTeam = 0 }, 1), "receiver accepted spoofed team");
            var publicMessage = chat.Read(2).Messages.First(m => m.Channel == "all");
            Require(receiver.Receive(publicMessage, 1) && !receiver.Receive(publicMessage, 1), "receiver duplicate validation");
            Require(!receiver.Read(1).HistoryTruncated, "private ID gaps falsely marked history truncated");
            var retainedWorld = new World(settings); var retained = new BattleChat(retainedWorld);
            for (var i = 0; i < 520; i++)
            {
                if (i % 4 == 0) for (var t = 0; t < Rules.TicksPerSecond; t++) retainedWorld.Step();
                Require(retained.TrySend(1, "all", "message " + i, retainedWorld.Tick, out _, out _), "retention send");
            }
            var firstRetained = retained.Read(1, 0, 100); Require(firstRetained.HistoryTruncated && firstRetained.Messages[0].Id == 9 && retained.Recent(1, 1000).Length == 512, "retention bound/cursor");
            Require(new BattleChat(world).MatchId != chat.MatchId, "new match retained old message scope");
            checks.Add("receiver validates team and deduplicates; bounded 512-message history, truncation detection and fresh-match isolation");
        }
        catch (Exception exception) { error = exception.ToString(); Console.Error.WriteLine(error); }
        var json = JsonSerializer.Serialize(new { passed = error.Length == 0, battleChatHttp = true, checks, error }, new JsonSerializerOptions { WriteIndented = true }); Console.WriteLine(json);
        var output = args.FirstOrDefault(a => a.StartsWith("--report="));
        if (output is not null) { var path = Path.GetFullPath(output[9..]); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, json); }
        return error.Length == 0 ? 0 : 1;
    }
}
