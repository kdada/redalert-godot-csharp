using Godot;
using RedAlert.Simulation;

namespace RedAlert;

public partial class Main
{
    ExternalAiHttpServer? externalAiHttp;
    ExternalAiController? externalAi;
    int[] practiceSlots = Enumerable.Range(0, 8).ToArray();
    int ExternalAiPort => Number("ai-api-port", ExternalAiController.DefaultPort, 1024, 65535);

    bool StartExternalAi(World world)
    {
        StopExternalAi();localBattleChat = new BattleChat(world);
        var players = Enumerable.Range(0, world.Players.Length).Where(p => practiceDifficulties.ElementAtOrDefault(p) == ExternalAiController.ExternalDifficulty).ToArray();
        if (players.Length == 0) return true;
        try
        {
            var controllers = world.Players.Select(p => players.Contains(p.Id) ? "external" : p.Id == 0 && !(headless && bot) ? "human" : practiceDifficulties.ElementAtOrDefault(p.Id) switch { 0 => "simple", 2 => "hard", _ => "normal" }).ToArray();
            var controller = new ExternalAiController(world, players, controllers, practiceSlots.Take(world.Players.Length).ToArray(), localBattleChat)
            { GameSpeedMultiplier = headless ? 1 : Math.Pow(1.25, clientPreferences.GameSpeed - 3) };
            var server = new ExternalAiHttpServer(ExternalAiPort);
            externalAi = controller; externalAiHttp = server;
            notice = $"AI 接口 {server.BaseUrl} · 玩家 ID {string.Join(",", players)}";
            GD.Print($"EXTERNAL AI HTTP {server.BaseUrl} matchId={controller.MatchId} players={string.Join(',', players)}");
            return true;
        }
        catch (Exception exception)
        {
            StopExternalAi(); SetFrontNotice($"无法启动 AI HTTP 接口（端口 {ExternalAiPort}）：{exception.Message}"); return false;
        }
    }
    void ConfigureHeadlessAi(World world)
    {
        localBattleChat = new BattleChat(world);
        practiceDifficulties = Enumerable.Repeat(1, world.Players.Length).ToArray();
        practiceSlots = Enumerable.Range(0, world.Players.Length).ToArray();
        if (!options.TryGetValue("external-ai", out var text)) return;
        var values = text.Split(',', StringSplitOptions.TrimEntries);
        if (values.Length == 0 || values.Any(v => !int.TryParse(v, out var id) || id < 0 || id >= world.Players.Length))
            throw new ArgumentException("--external-ai requires comma-separated player IDs belonging to the match");
        foreach (var value in values) practiceDifficulties[int.Parse(value)] = ExternalAiController.ExternalDifficulty;
        if (!StartExternalAi(world)) throw new InvalidOperationException(notice);
    }
    void PumpExternalAi()
    {
        if (externalAi is { } controller && localWorld is not null) externalAiHttp?.Pump(controller.Handle);
    }
    void StopExternalAi()
    {
        externalAi?.CancelPending(); externalAiHttp?.Dispose(); externalAiHttp = null; externalAi = null; localBattleChat = null;
    }
}
