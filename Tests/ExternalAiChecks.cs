using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using RedAlert.Simulation;

namespace RedAlert.Tests;

public static class ExternalAiChecks
{
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    static int FreePort() { using var tcp = new TcpListener(IPAddress.Loopback, 0); tcp.Start(); return ((IPEndPoint)tcp.LocalEndpoint).Port; }
    public static int Run(string[] args)
    {
        var checks = new List<string>(); var error = ""; var watch = Stopwatch.StartNew();
        var world = new World(MatchSettings.Practice(3)); var commands = new List<GameCommand>();
        var controller = new ExternalAiController(world, [1, 2], setupSlots: [0, 2, 5]);
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(12) };
        var port = FreePort();
        try
        {
            using (var server = new ExternalAiHttpServer(port))
            {
                var ownerThread = Environment.CurrentManagedThreadId;
                ExternalAiHttpResponse Handler(ExternalAiHttpRequest request)
                { Require(Environment.CurrentManagedThreadId == ownerThread, "HTTP worker accessed simulation thread state"); return controller.Handle(request); }
                T Await<T>(Task<T> task)
                {
                    var timer = Stopwatch.StartNew();
                    while (!task.IsCompleted && timer.Elapsed.TotalSeconds < 12) { server.Pump(Handler); Thread.Sleep(1); }
                    return task.GetAwaiter().GetResult();
                }
                (int Status, JsonElement Json) Send(string path, object? body = null)
                {
                    using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, server.BaseUrl + "api/v1/" + path);
                    if (body is not null) request.Content = JsonContent.Create(body, options: ExternalAiJson.Options);
                    using var response = Await(client.SendAsync(request));
                    var bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                    using var document = JsonDocument.Parse(bytes); return ((int)response.StatusCode, document.RootElement.Clone());
                }
                object Batch(string id, params object[] list) => new { matchId = controller.MatchId, requestId = id, commands = list };
                void Step() { var next = controller.DrainCommands(world.Tick); commands.AddRange(next); world.Step(next, controller.CommandApplied); }
                var match = Send("match"); Require(match.Status == 200, "match endpoint");
                Require(match.Json.GetProperty("players")[1].GetProperty("id").GetInt32() == 1 && match.Json.GetProperty("players")[1].GetProperty("setupSlot").GetInt32() == 3, "compacted IDs/source setup slots");
                Require(match.Json.GetProperty("players")[2].GetProperty("controller").GetString() == "external", "external roster");
                var rules = Send("rules"); Require(rules.Status == 200 && rules.Json.GetProperty("units").GetArrayLength() == Rules.Units.Length && rules.Json.GetProperty("commands").GetArrayLength() == 12, "rule content");
                checks.Add("real loopback HTTP; stable match/player IDs, compacted setup slots, complete rule catalog; handlers run on simulation thread");

                var state = Send("players/1/state"); Require(state.Status == 200, "state endpoint");
                Require(state.Json.GetProperty("units").EnumerateArray().All(u => u.GetProperty("owner").GetInt32() == 1), "hidden enemy unit leak");
                Require(state.Json.GetProperty("buildings").EnumerateArray().All(b => b.GetProperty("owner").GetInt32() == 1), "hidden enemy building leak");
                Require(state.Json.GetProperty("players").EnumerateArray().All(p => !p.TryGetProperty("credits", out _)), "enemy economic leak");
                var map = Send("players/1/map"); Require(map.Status == 200, "map endpoint"); var unknown = 0;
                for (var y = 0; y < world.Map.Height; y++) for (var x = 0; x < world.Map.Width; x++)
                {
                    var cell = map.Json.GetProperty("terrainRows")[y].GetString()![x];
                    Require((cell == '?') == !world.Explored(1, x, y), "hidden terrain leak"); if (cell == '?') unknown++;
                }
                Require(unknown > 0 && map.Json.GetProperty("visibleOre").EnumerateArray().All(o => world.Visible(1, o.GetProperty("x").GetInt32(), o.GetProperty("y").GetInt32())), "hidden resource leak");
                Require(Send("players/0/state").Status == 403, "human state/control should be unavailable");
                checks.Add("per-player fog filters entities, terrain and ore; enemy economy omitted; human seat is not API-controlled");

                var foreign = world.Units.First(u => u.Owner == 2).Id;
                Require(Send("players/1/commands", Batch("foreign", new { type = "Stop", actors = new[] { foreign } })).Status == 403, "foreign actor accepted");
                Require(Send("players/0/commands", Batch("human", new { type = "Surrender" })).Status == 403, "human control accepted");
                Require(Send("players/1/commands", Batch("hidden", new { type = "Attack", actors = new[] { world.Units.First(u => u.Owner == 1).Id }, target = foreign })).Status == 403, "hidden target accepted");
                Require(Send("players/1/commands", new { matchId = "old-match", requestId = "old", commands = new[] { new { type = "Surrender" } } }).Status == 409, "stale match accepted");
                Require(Send("players/1/commands", Batch("numeric", new { type = 1 })).Status == 400, "numeric enum accepted");
                Require(Send("players/1/commands", new { matchId = controller.MatchId, requestId = "spoof", player = 0, commands = new[] { new { type = "Surrender" } } }).Status == 400, "unknown player injection accepted");
                Require(Send("players/1/commands", Batch("atomic", new { type = "Stop", actors = new[] { world.Units.First(u => u.Owner == 1).Id } }, new { type = "Move", actors = new[] { -1 }, x = 2, y = 2 })).Status == 400, "malformed batch accepted");
                Require(controller.DrainCommands(world.Tick).Length == 0, "rejected batch partially queued");
                checks.Add("ownership, hidden targets, stale match, numeric enums, unknown fields and malformed batches rejected before mutation");

                var barracks = world.Buildings.First(b => b.Owner == 1 && b.Type == BuildingType.Barracks);
                var train = Batch("train-1", new { type = "Train", actors = new[] { barracks.Id }, unit = "Conscript" }, new { type = "Train", actors = new[] { barracks.Id }, unit = "Gi" });
                var unit = world.Units.First(u => u.Owner == 2 && !u.Definition.Miner);
                Require(Send("players/1/commands", train).Status == 202, "valid train was not queued");
                Require(Send("players/1/commands", train).Status == 202, "queued idempotent retry failed");
                Require(Send("players/1/commands", Batch("train-1", new { type = "Stop", actors = new[] { barracks.Id } })).Status == 409, "conflicting request ID accepted");
                Require(Send("players/2/commands", Batch("move-2", new { type = "Move", actors = new[] { unit.Id }, x = unit.CellX + 2, y = unit.CellY + 2 })).Status == 202, "second AI command not queued");
                Require(Send("players/1/commands/train-1").Json.GetProperty("status").GetString() == "queued", "queued receipt misreported");
                var initialCredits = world.Players[1].Credits; Step();
                Require(commands.Count == 3 && commands.Count(c => c.Player == 1) == 2 && commands.Count(c => c.Player == 2) == 1, "duplicate or misrouted commands");
                Require(world.Players[1].Credits == initialCredits - Rules.Unit(UnitType.Conscript).Cost && barracks.Queue.SequenceEqual([UnitType.Conscript]), "production cost/faction rules bypassed");
                var receipt = Send("players/1/commands/train-1").Json;
                Require(receipt.GetProperty("status").GetString() == "applied" && receipt.GetProperty("results")[0].GetProperty("accepted").GetBoolean() && !receipt.GetProperty("results")[1].GetProperty("accepted").GetBoolean(), "per-command actual rules results");
                Require(Send("players/1/commands", train).Status == 200 && controller.DrainCommands(world.Tick).Length == 0, "applied retry duplicated commands");
                Require(unit.Order == UnitOrder.Move, "second AI did not receive move");
                checks.Add("two external players independently submit commands; real costs/faction validation; queued/applied receipts and requestId deduplication");

                var building = world.Buildings.First(b => b.Owner == 2 && b.Type == BuildingType.Barracks);
                var sell = Batch("sell-once", new { type = "Sell", actors = new[] { building.Id } });
                Require(Send("players/2/commands", sell).Status == 202, "sell queue"); Step();
                Require(world.Find(building.Id) is null && Send("players/2/commands", sell).Status == 200, "retry after actor removal failed");
                for (var i = 0; i < 100; i++) Step();
                Require(world.Units.Count(u => u.Owner == 1 && u.Type == UnitType.Conscript) == 4, "queued infantry never produced");
                var replay = new ReplayFile { Settings = world.Settings, Commands = commands, FinalTick = world.Tick, FinalHash = world.StateHash().ToString("x16") };
                Require(replay.Play().StateHash() == world.StateHash(), "HTTP command replay mismatch");
                var reportArg = args.FirstOrDefault(a => a.StartsWith("--report=")); if (reportArg is not null) replay.Save(Path.ChangeExtension(Path.GetFullPath(reportArg[9..]), "replay.json"));
                checks.Add("idempotent retry survives removed actors; commands produce units and replay to identical state");

                using (var origin = new HttpRequestMessage(HttpMethod.Get, server.BaseUrl + "api/v1/match"))
                { origin.Headers.Add("Origin", "https://example.invalid"); using var response = Await(client.SendAsync(origin)); Require(response.StatusCode == HttpStatusCode.Forbidden, "browser Origin accepted"); }
                using (var wrong = new HttpRequestMessage(HttpMethod.Post, server.BaseUrl + "api/v1/players/1/commands") { Content = new StringContent("{}", Encoding.UTF8, "text/plain") })
                { using var response = Await(client.SendAsync(wrong)); Require((int)response.StatusCode == 415, "incorrect media type accepted"); }
                using (var large = new HttpRequestMessage(HttpMethod.Post, server.BaseUrl + "api/v1/players/1/commands") { Content = new StringContent(new string('x', 65537), Encoding.UTF8, "application/json") })
                { using var response = Await(client.SendAsync(large)); Require((int)response.StatusCode == 413, "oversized body accepted"); }
                checks.Add("HTTP JSON media type, browser Origin rejection and 64KiB body bound");
            }
            using (var rebound = new ExternalAiHttpServer(port))
            {
                var request = client.GetAsync(rebound.BaseUrl + "api/v1/match");
                Thread.Sleep(50); rebound.Dispose();
                try { using var ignored = request.GetAwaiter().GetResult(); } catch (HttpRequestException) { } catch (TaskCanceledException) { }
                Require(rebound.Pump(_ => throw new Exception("disposed request executed")) == 0, "request ran after disposal");
            }
            using (var reused = new ExternalAiHttpServer(port)) { }
            checks.Add("shutdown cancels pending requests without execution and releases port for reuse");
            EightPlayers(client); checks.Add("seven simultaneous external AI callers route to separate players in an eight-player simulation");
        }
        catch (Exception exception) { error = exception.ToString(); Console.Error.WriteLine(error); }
        var result = new { passed = error.Length == 0, externalAiHttpTest = true, checks, tick = world.Tick, hash = world.StateHash().ToString("x16"), accepted = world.AcceptedCommands, rejected = world.RejectedCommands, content = World.ContentFingerprint(), elapsedSeconds = watch.Elapsed.TotalSeconds, error };
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }); Console.WriteLine(json);
        var output = args.FirstOrDefault(a => a.StartsWith("--report="));
        if (output is not null) { var path = Path.GetFullPath(output[9..]); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, json); }
        return error.Length == 0 ? 0 : 1;
    }
    static void EightPlayers(HttpClient client)
    {
        var world = new World(MatchSettings.Practice(8)); var api = new ExternalAiController(world, Enumerable.Range(1, 7));
        using var server = new ExternalAiHttpServer(FreePort());
        var tasks = Enumerable.Range(1, 7).Select(player =>
        {
            var barracks = world.Buildings.First(b => b.Owner == player && b.Type == BuildingType.Barracks);
            return client.PostAsJsonAsync(server.BaseUrl + $"api/v1/players/{player}/commands", new { matchId = api.MatchId, requestId = "parallel", commands = new[] { new { type = "Train", actors = new[] { barracks.Id }, unit = Rules.BasicInfantry(world.Players[player].Setup.Country).ToString() } } }, ExternalAiJson.Options);
        }).ToArray();
        var all = Task.WhenAll(tasks); var timer = Stopwatch.StartNew();
        while (!all.IsCompleted && timer.Elapsed.TotalSeconds < 12) { server.Pump(api.Handle); Thread.Sleep(1); }
        foreach (var response in all.GetAwaiter().GetResult()) { using (response) Require((int)response.StatusCode == 202, "parallel caller failed"); }
        var commands = api.DrainCommands(world.Tick); Require(commands.Length == 7 && commands.Select(c => c.Player).Distinct().Count() == 7, "parallel routing");
        world.Step(commands, api.CommandApplied); Require(world.AcceptedCommands == 7 && world.RejectedCommands == 0, "parallel commands rejected or lost");
    }
}
