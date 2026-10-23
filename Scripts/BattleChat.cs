using System.Globalization;
using System.Text;
using RedAlert.Simulation;

namespace RedAlert;

public sealed record BattleChatMessage(long Id, int Tick, int SenderId, string SenderName, int SenderTeam, string Channel, string Text);
public sealed record BattleChatPage(BattleChatMessage[] Messages, long NextCursor, bool HasMore, bool HistoryTruncated);

// Match-local communication, deliberately outside simulation commands, hashes and replays.
// All access is on the game thread, including requests delivered by the HTTP pump.
public sealed class BattleChat
{
    public const int MaxTextLength = 280, MaxHistory = 512, MaxPerSecond = 4;
    readonly List<BattleChatMessage> messages = [];
    readonly Dictionary<int, Queue<int>> sentTicks = [];
    long lastId, evictedThrough;
    public World World { get; }
    public string MatchId { get; }
    public BattleChat(World world, string? matchId = null)
    {
        World = world; MatchId = string.IsNullOrEmpty(matchId) ? Guid.NewGuid().ToString("N") : matchId;
    }
    public bool TrySend(int sender, string channel, string text, int tick, out BattleChatMessage? message, out string error)
    {
        message = null;
        if (sender < 0 || sender >= World.Players.Length) { error = "invalid_sender"; return false; }
        if (World.Finished || World.Players[sender].Defeated) { error = "player_inactive"; return false; }
        if (channel is not ("all" or "team")) { error = "invalid_channel"; return false; }
        if (!NormalizeText(text, out var normalized, out error)) return false;
        if (!sentTicks.TryGetValue(sender, out var ticks)) sentTicks[sender] = ticks = new Queue<int>();
        while (ticks.TryPeek(out var previous) && previous <= tick - Rules.TicksPerSecond) ticks.Dequeue();
        if (ticks.Count >= MaxPerSecond) { error = "rate_limited"; return false; }
        var player = World.Players[sender];
        message = new BattleChatMessage(++lastId, tick, sender, DisplayName(player.Setup.Name), player.Setup.Team, channel, normalized);
        ticks.Enqueue(tick); Append(message); error = ""; return true;
    }
    public bool CanRead(int player, BattleChatMessage message) => player >= 0 && player < World.Players.Length
        && message.SenderId >= 0 && message.SenderId < World.Players.Length
        && (message.Channel == "all" || message.Channel == "team" && message.SenderTeam == World.Players[player].Setup.Team);
    public bool Receive(BattleChatMessage message, int recipient)
    {
        if (message is null || message.Id <= lastId || message.Tick < 0 || !CanRead(recipient, message)) return false;
        var sender = World.Players[message.SenderId];
        if (message.SenderTeam != sender.Setup.Team || message.SenderName != DisplayName(sender.Setup.Name)
            || !NormalizeText(message.Text, out var normalized, out _) || normalized != message.Text) return false;
        lastId = message.Id; Append(message); return true;
    }
    void Append(BattleChatMessage message)
    {
        messages.Add(message);
        if (messages.Count > MaxHistory) { evictedThrough = messages[0].Id; messages.RemoveAt(0); }
    }
    public BattleChatPage Read(int player, long after = 0, int limit = 50)
    {
        if (player < 0 || player >= World.Players.Length) throw new ArgumentOutOfRangeException(nameof(player));
        if (after < 0 || limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(after));
        var visible = messages.Where(m => m.Id > after && CanRead(player, m)).Take(limit + 1).ToArray();
        var page = visible.Take(limit).ToArray();
        return new BattleChatPage(page, page.Length == 0 ? after : page[^1].Id, visible.Length > limit,
            after < evictedThrough);
    }
    public BattleChatMessage[] Recent(int player, int count = 100) => messages.Where(m => CanRead(player, m)).TakeLast(count).ToArray();
    public static bool NormalizeText(string? text, out string normalized, out string error)
    {
        var value = new StringBuilder(); var count = 0;
        foreach (var rune in (text ?? "").Trim().EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (rune.Value is '\n' or '\r' or '\t' || category is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
            { value.Append(' '); count++; }
            else if (category is UnicodeCategory.Control or UnicodeCategory.Surrogate || category == UnicodeCategory.Format && rune.Value is not (0x200c or 0x200d)) continue;
            else { value.Append(rune.ToString()); count++; }
            if (count > MaxTextLength) { normalized = ""; error = "text_too_long"; return false; }
        }
        normalized = value.ToString().Trim(); error = normalized.Length == 0 ? "empty_text" : ""; return error.Length == 0;
    }
    static string DisplayName(string name) => new(name.Where(c => !char.IsControl(c) && char.GetUnicodeCategory(c) != UnicodeCategory.Format).Take(64).ToArray());
}
