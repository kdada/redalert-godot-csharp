using System.Security.Cryptography;
using System.Text.Json;
using RedAlert.Simulation;

namespace RedAlert;

// Accessed only by the simulation thread. HTTP workers receive serialized copies.
public sealed partial class ExternalAiController
{
    public const int DefaultPort = 19844;
    public const int ExternalDifficulty = 3;
    const int MaxBatch = 32, MaxActors = 256, MaxPending = 256, MaxReceipts = 16384;
    readonly World world;
    readonly HashSet<int> controlled;
    readonly string[] controllers;
    readonly int[] setupSlots;
    readonly Dictionary<(int Player, string Id), Receipt> receipts = [];
    readonly List<(Receipt Receipt, GameCommand[] Commands)> pending = [];
    readonly Dictionary<(int Player, int Sequence), (Receipt Receipt, int Index)> applying = [];
    int sequence, pendingCount;
    public string MatchId => Chat.MatchId;
    public double GameSpeedMultiplier { get; set; } = 1;
    public bool IsControlled(int player) => controlled.Contains(player);

    public ExternalAiController(World world, IEnumerable<int> players, string[]? controllers = null, int[]? setupSlots = null, BattleChat? chat = null)
    {
        this.world = world; Chat = chat ?? new BattleChat(world);
        if (!ReferenceEquals(Chat.World, world)) throw new ArgumentException("Chat must belong to the same match world");
        controlled = players.ToHashSet();
        if (controlled.Count == 0 || controlled.Any(p => p < 0 || p >= world.Players.Length))
            throw new ArgumentException("External AI player IDs must belong to the match");
        this.controllers = controllers ?? world.Players.Select(p => controlled.Contains(p.Id) ? "external" : p.Id == 0 ? "human" : "normal").ToArray();
        this.setupSlots = setupSlots ?? Enumerable.Range(0, world.Players.Length).ToArray();
        if (this.controllers.Length != world.Players.Length || this.setupSlots.Length != world.Players.Length)
            throw new ArgumentException("Roster metadata length differs from player count");
    }

    public ExternalAiHttpResponse Handle(ExternalAiHttpRequest request)
    {
        var parts = request.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts[0] != "api" || parts[1] != "v1") return Error(404, "not_found", "Use /api/v1");
        if (request.Method == "GET")
        {
            if (parts.Length == 2 || parts is ["api", "v1", "health"])
                return Json(200, new { apiVersion = 1, service = "redalert-external-ai", matchId = MatchId, tick = world.Tick, endpoints = new[] { "GET /api/v1/match", "GET /api/v1/players", "GET /api/v1/rules", "GET /api/v1/players/{id}/state", "GET /api/v1/players/{id}/map", "POST /api/v1/players/{id}/commands", "GET /api/v1/players/{id}/commands/{requestId}", "GET /api/v1/players/{id}/messages?after=0&limit=50", "POST /api/v1/players/{id}/messages" } });
            if (parts is ["api", "v1", "match"]) return Json(200, Match());
            if (parts is ["api", "v1", "players"]) return Json(200, new { matchId = MatchId, players = Roster() });
            if (parts is ["api", "v1", "rules"]) return Json(200, RuleBook());
        }
        if (parts.Length < 5 || parts[2] != "players" || !int.TryParse(parts[3], out var player) || player < 0 || player >= world.Players.Length)
            return Error(404, "not_found", "Unknown endpoint or player ID");
        if (!IsControlled(player)) return Error(403, "not_external_ai", "Only external AI seats are available through this API");
        if (request.Method == "GET")
        {
            if (parts.Length == 5 && parts[4] == "state") return Json(200, State(player));
            if (parts.Length == 5 && parts[4] == "map") return Json(200, Map(player));
            if (parts.Length == 5 && parts[4] == "messages") return ReadChat(player, request.Query);
            if (parts.Length == 6 && parts[4] == "commands")
                return receipts.TryGetValue((player, parts[5]), out var receipt) ? Json(200, ReceiptView(receipt)) : Error(404, "unknown_request", "Unknown requestId in this match/player");
        }
        if (request.Method == "POST" && parts.Length == 5 && parts[4] == "commands") return Submit(player, request.Body);
        if (request.Method == "POST" && parts.Length == 5 && parts[4] == "messages") return SubmitChat(player, request.Body);
        return Error(404, "not_found", "Unknown endpoint");
    }

    ExternalAiHttpResponse Submit(int player, byte[] body)
    {
        Batch? batch;
        try { batch = JsonSerializer.Deserialize<Batch>(body, ExternalAiJson.Options); }
        catch (JsonException e) { return Error(400, "invalid_json", "Invalid command JSON: " + e.Message); }
        if (batch is null || batch.MatchId != MatchId) return Error(409, "stale_match", "Fetch the current matchId before submitting commands");
        if (!ValidRequestId(batch.RequestId)) return Error(400, "invalid_request_id", "requestId must be 1-64 ASCII letters, digits, hyphens or underscores");
        if (batch.Commands is null || batch.Commands.Length is < 1 or > MaxBatch) return Error(400, "invalid_batch", $"commands must contain 1-{MaxBatch} items");
        var commands = new List<GameCommand>();
        foreach (var command in batch.Commands)
        {
            if (command is null || command.Type is null) return Error(400, "invalid_command", "Each command requires type");
            var actors = command.Actors ?? [];
            if (actors.Length > MaxActors || actors.Any(id => id <= 0)) return Error(400, "invalid_actors", $"actors must contain at most {MaxActors} positive entity IDs");
            var type = command.Type.Value;
            var needsActors = type is CommandType.Move or CommandType.Attack or CommandType.AttackMove or CommandType.Stop or CommandType.Train or CommandType.CancelQueue or CommandType.Rally or CommandType.Sell or CommandType.Repair;
            if (needsActors && actors.Length == 0) return Error(400, "missing_actors", "This command requires actors");
            if (type is CommandType.Train or CommandType.CancelQueue or CommandType.Rally or CommandType.Sell or CommandType.Repair && actors.Length != 1)
                return Error(400, "invalid_actors", "This command requires exactly one building actor");
            var positional = type is CommandType.Move or CommandType.AttackMove or CommandType.Build or CommandType.Rally or CommandType.Support;
            if (positional && (command.X is null || command.Y is null || !world.Map.Inside(command.X.Value, command.Y.Value)))
                return Error(400, "invalid_position", "x and y must be in-bounds, zero-based map cells");
            if (type == CommandType.Train && command.Unit is null) return Error(400, "missing_unit", "Train requires unit");
            if (type is CommandType.Build or CommandType.QueueBuilding && command.Building is null) return Error(400, "missing_building", "Build requires building");
            if (type == CommandType.Attack && command.Target is not > 0) return Error(400, "missing_target", "Attack requires target entity ID");
            commands.Add(new GameCommand { Player = player, Type = type, Actors = actors, X = command.X ?? 0, Y = command.Y ?? 0, Target = command.Target ?? 0, Unit = command.Unit ?? default, Building = command.Building ?? default });
        }
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(commands, ExternalAiJson.Options)));
        if (receipts.TryGetValue((player, batch.RequestId!), out var previous))
            return previous.Fingerprint == fingerprint ? Json(previous.Status == "queued" ? 202 : 200, ReceiptView(previous)) : Error(409, "request_id_conflict", "This requestId was already used for different commands");
        if (world.Finished || world.Players[player].Defeated) return Error(409, "player_inactive", "The match is finished or this player is defeated");
        foreach (var command in commands)
        {
            if (command.Actors.Any(id => world.Find(id) is not { } entity || entity.Owner != player))
                return Error(403, "actor_not_owned", "Every actor must be a living entity owned by the selected AI player");
            if (command.Type == CommandType.Attack && (world.Find(command.Target) is not { } target || !world.Visible(player, target.CellX, target.CellY)))
                return Error(403, "target_not_visible", "Attack target must currently be visible to this player");
        }
        if (pendingCount + commands.Count > MaxPending || receipts.Count >= MaxReceipts)
            return Error(429, "capacity", "Pending command or per-match receipt capacity reached");
        var receipt = new Receipt { Id = batch.RequestId!, Player = player, Fingerprint = fingerprint, QueuedTick = world.Tick, Results = new CommandResult?[commands.Count] };
        var scheduled = commands.Select(c => c with { Sequence = ++sequence }).ToArray();
        receipts.Add((player, receipt.Id), receipt); pending.Add((receipt, scheduled)); pendingCount += scheduled.Length;
        return Json(202, ReceiptView(receipt));
    }

    // Called immediately before World.Step; an HTTP worker never mutates World.
    public GameCommand[] DrainCommands(int tick)
    {
        var commands = new List<GameCommand>(pendingCount);
        foreach (var batch in pending)
            for (var i = 0; i < batch.Commands.Length; i++)
            {
                var command = batch.Commands[i] with { Tick = tick };
                applying.Add((command.Player, command.Sequence), (batch.Receipt, i)); commands.Add(command);
            }
        pending.Clear(); pendingCount = 0; return commands.ToArray();
    }
    public void CommandApplied(GameCommand command, bool accepted)
    {
        if (!applying.Remove((command.Player, command.Sequence), out var entry)) return;
        entry.Receipt.Results[entry.Index] = new CommandResult(entry.Index, command.Type, command.Sequence, command.Tick, accepted, accepted ? null : "game_rules_rejected");
        if (entry.Receipt.Results.All(r => r is not null)) entry.Receipt.Status = "applied";
    }
    public void CancelPending()
    {
        foreach (var batch in pending) batch.Receipt.Status = "cancelled";
        foreach (var entry in applying.Values) entry.Receipt.Status = "cancelled";
        pending.Clear(); applying.Clear(); pendingCount = 0;
    }
    object ReceiptView(Receipt receipt) => new { matchId = MatchId, playerId = receipt.Player, requestId = receipt.Id, status = receipt.Status, queuedAtTick = receipt.QueuedTick, commandCount = receipt.Results.Length, results = receipt.Results, receiptUrl = $"/api/v1/players/{receipt.Player}/commands/{receipt.Id}" };
    object Roster() => world.Players.Select(p => new { id = p.Id, setupSlot = setupSlots[p.Id] + 1, p.Setup.Name, p.Setup.Country, p.Setup.Team, controller = controllers[p.Id], p.Defeated }).ToArray();
    object Match() => new { apiVersion = 1, matchId = MatchId, tick = world.Tick, ticksPerSecond = Rules.TicksPerSecond, gameSpeedMultiplier = GameSpeedMultiplier, simulatedSeconds = (double)world.Tick / Rules.TicksPerSecond, world.Finished, winningTeam = world.Finished ? (int?)world.WinningTeam : null, map = new { width = world.Map.Width, height = world.Map.Height }, players = Roster(), coordinates = "zero-based map cells; x right, y down" };
    bool Observable(int player, Entity entity) => entity.Alive && (entity.Owner == player || world.Players[player].Defeated || world.Finished || world.Visible(player, entity.CellX, entity.CellY));
    object State(int player)
    {
        var p = world.Players[player];
        return new
        {
            matchId = MatchId, tick = world.Tick, ticksPerSecond = Rules.TicksPerSecond, gameSpeedMultiplier = GameSpeedMultiplier, world.Finished,
            player = new { id = player, p.Setup.Name, p.Setup.Country, p.Setup.Team, controller = "external", p.Credits, p.GeneratedPower, p.UsedPower, p.Powered, p.Defeated, spectating = p.Defeated || world.Finished, p.BuildingProduction, p.BuildingProductionProgress, p.BuildingReady, p.SupportReadyTick, supportAvailable = p.Setup.Country == Country.America && world.Tick >= p.SupportReadyTick && world.HasBuilding(player, BuildingType.Barracks) },
            players = Roster(), map = new { width = world.Map.Width, height = world.Map.Height, endpoint = $"/api/v1/players/{player}/map" },
            units = world.Units.Where(u => Observable(player, u)).Select(u => new { id = u.Id, owner = u.Owner, u.Type, x = u.CellX, y = u.CellY, u.Health, maxHealth = u.MaxHealth, own = u.Owner == player, allied = world.Allied(player, u.Owner), order = u.Owner == player ? u.Order.ToString() : null, goal = u.Owner == player ? new { x = u.GoalX, y = u.GoalY } : null, target = u.Owner == player && world.Find(u.TargetId) is { } target && Observable(player, target) ? (int?)u.TargetId : null, cargo = u.Owner == player ? (int?)u.Cargo : null }).ToArray(),
            buildings = world.Buildings.Where(b => Observable(player, b)).Select(b => new { id = b.Id, owner = b.Owner, b.Type, x = b.CellLeft, y = b.CellTop, width = b.Definition.Width, height = b.Definition.Height, b.Health, maxHealth = b.MaxHealth, b.Complete, own = b.Owner == player, allied = world.Allied(player, b.Owner), remainingBuildTicks = b.Owner == player ? (int?)b.RemainingBuildTicks : null, productionProgress = b.Owner == player ? (int?)b.ProductionProgress : null, queue = b.Owner == player ? b.Queue.ToArray() : null, repairing = b.Owner == player ? (bool?)b.Repairing : null, rally = b.Owner == player ? new { x = b.RallyX, y = b.RallyY } : null }).ToArray(),
            observation = p.Defeated || world.Finished ? "Global spectator view after defeat or match end; commands are disabled" : "Own entities plus currently visible entities; enemy production/economy and hidden map resources are omitted"
        };
    }
    object Map(int player)
    {
        var terrain = new string[world.Map.Height]; var visibility = new string[world.Map.Height]; var ore = new List<object>();
        for (var y = 0; y < world.Map.Height; y++)
        {
            var t = new char[world.Map.Width]; var v = new char[world.Map.Width];
            for (var x = 0; x < world.Map.Width; x++)
            {
                var index = world.Map.Index(x, y); var seen = world.Players[player].Defeated || world.Finished || world.Visible(player, x, y); var explored = seen || world.Explored(player, x, y);
                t[x] = !explored ? '?' : world.Map.Ground[index] switch { Terrain.Grass => '.', Terrain.Dirt => ',', Terrain.Rock => '#', _ => '~' };
                v[x] = seen ? 'v' : explored ? 'e' : '?';
                if (seen && world.Map.Ore[index] > 0) ore.Add(new { x, y, amount = world.Map.Ore[index] });
            }
            terrain[y] = new string(t); visibility[y] = new string(v);
        }
        return new { matchId = MatchId, playerId = player, tick = world.Tick, width = world.Map.Width, height = world.Map.Height, terrainLegend = new Dictionary<string, string> { ["?"] = "unknown", ["."] = "Grass", [","] = "Dirt", ["#"] = "Rock", ["~"] = "Water" }, visibilityLegend = new { v = "visible", e = "explored but not visible", unknown = "?" }, terrainRows = terrain, visibilityRows = visibility, visibleOre = ore };
    }
    static object RuleBook() => new
    {
        apiVersion = 1, revision = Rules.Revision, contentFingerprint = World.ContentFingerprint(), ticksPerSecond = Rules.TicksPerSecond,
        buildingPlacementTicks = Rules.BuildingPlacementTicks, maxPlayers = Rules.MaxPlayers, maxEntities = Rules.MaxEntities, countries = Enum.GetNames<Country>(),
        units = Rules.Units.Select(d => new { definition = d, producer = d.Infantry ? BuildingType.Barracks : BuildingType.WarFactory, prerequisites = d.Advanced ? new[] { BuildingType.BattleLab } : [], availableCountries = Enum.GetValues<Country>().Where(c => Rules.Allowed(d, c)).ToArray() }).ToArray(),
        buildings = Rules.Buildings.Select(d => new { definition = d, buildable = d.Type != BuildingType.ConstructionYard, prerequisites = BuildingPrerequisites(d.Type), availableCountries = Enum.GetValues<Country>().Where(c => Rules.Allowed(d, c)).ToArray() }).ToArray(),
        commands = new object[]
        {
            new { type = CommandType.Move, required = new[] { "actors", "x", "y" } }, new { type = CommandType.AttackMove, required = new[] { "actors", "x", "y" } },
            new { type = CommandType.Attack, required = new[] { "actors", "target" } }, new { type = CommandType.Stop, required = new[] { "actors" } },
            new { type = CommandType.QueueBuilding, required = new[] { "building" } }, new { type = CommandType.CancelBuilding, required = Array.Empty<string>() },
            new { type = CommandType.Build, required = new[] { "building", "x", "y" } }, new { type = CommandType.Train, required = new[] { "actors", "unit" } },
            new { type = CommandType.CancelQueue, required = new[] { "actors" } }, new { type = CommandType.Rally, required = new[] { "actors", "x", "y" } },
            new { type = CommandType.Sell, required = new[] { "actors" } }, new { type = CommandType.Repair, required = new[] { "actors" } },
            new { type = CommandType.Support, required = new[] { "x", "y" } }, new { type = CommandType.Surrender, required = Array.Empty<string>() }
        },
        chat = new { channels = new[] { "all", "team" }, maxTextLength = BattleChat.MaxTextLength, historyLimit = BattleChat.MaxHistory, maxMessagesPer20Ticks = BattleChat.MaxPerSecond, send = "POST /api/v1/players/{id}/messages", read = "GET /api/v1/players/{id}/messages?after=0&limit=50", independentOfSimulation = true },
        limits = new { maxCommandsPerBatch = MaxBatch, maxActorsPerCommand = MaxActors, maxPendingCommands = MaxPending, maxReceiptsPerMatch = MaxReceipts, maxBodyBytes = 65536 },
        execution = "Commands are queued for the next simulation step. Poll the receipt for per-command accepted results; applied does not imply accepted. Use a unique requestId and current matchId. QueueBuilding prepays and prepares one building; poll player.buildingReady, then Build places it without a second charge and erects it in 20 ticks. CancelBuilding refunds preparation, including a ready item. Coordinates are zero-based cells; Build coordinates are the top-left footprint cell. Units and buildings use PascalCase enum names."
    };
    static BuildingType[] BuildingPrerequisites(BuildingType type) => type switch
    {
        BuildingType.ConstructionYard => [],
        BuildingType.BattleLab => [BuildingType.ConstructionYard, BuildingType.Radar],
        BuildingType.Radar or BuildingType.GrandCannon or BuildingType.TeslaCoil => [BuildingType.ConstructionYard, BuildingType.WarFactory],
        _ => [BuildingType.ConstructionYard]
    };
    static bool ValidRequestId(string? id) => id is { Length: >= 1 and <= 64 } && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    static ExternalAiHttpResponse Json(int status, object data) => ExternalAiHttpResponse.Json(status, data);
    static ExternalAiHttpResponse Error(int status, string code, string message) => Json(status, new { error = new { code, message } });
    sealed class Batch { public string? MatchId { get; init; } public string? RequestId { get; init; } public ApiCommand?[]? Commands { get; init; } }
    sealed class ApiCommand
    {
        public CommandType? Type { get; init; }
        public int[]? Actors { get; init; }
        public int? X { get; init; }
        public int? Y { get; init; }
        public int? Target { get; init; }
        public UnitType? Unit { get; init; }
        public BuildingType? Building { get; init; }
    }
    sealed class Receipt
    {
        public required string Id { get; init; }
        public int Player { get; init; }
        public required string Fingerprint { get; init; }
        public int QueuedTick { get; init; }
        public string Status { get; set; } = "queued";
        public required CommandResult?[] Results { get; init; }
    }
    sealed record CommandResult(int Index, CommandType Type, int Sequence, int Tick, bool Accepted, string? Error);
}
