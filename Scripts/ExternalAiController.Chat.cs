using System.Globalization;
using System.Net;
using System.Text.Json;

namespace RedAlert;

public sealed partial class ExternalAiController
{
    public BattleChat Chat { get; }
    readonly Dictionary<(int Player, string Request), (string Channel, string Text, BattleChatMessage Message)> chatReceipts = [];
    ExternalAiHttpResponse ReadChat(int player, string query)
    {
        long after = 0; var limit = 50; var names = new HashSet<string>();
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2); var name = WebUtility.UrlDecode(parts[0]); var value = parts.Length > 1 ? WebUtility.UrlDecode(parts[1]) : "";
            if (!names.Add(name)) return Error(400, "invalid_cursor", "Duplicate query parameter");
            if (name == "after")
            { if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out after)) return Error(400, "invalid_cursor", "after must be a nonnegative integer message ID"); }
            else if (name == "limit")
            { if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > 100) return Error(400, "invalid_limit", "limit must be 1-100"); }
            else if (name == "matchId")
            { if (value != MatchId) return Error(409, "stale_match", "Message cursors belong to a single match"); }
            else return Error(400, "invalid_query", "Only after, limit and matchId are supported");
        }
        var page = Chat.Read(player, after, limit);
        return Json(200, new { matchId = MatchId, playerId = player, tick = world.Tick, page.Messages, page.NextCursor, page.HasMore, page.HistoryTruncated });
    }
    ExternalAiHttpResponse SubmitChat(int player, byte[] body)
    {
        ChatRequest? request;
        try { request = JsonSerializer.Deserialize<ChatRequest>(body, ExternalAiJson.Options); }
        catch (JsonException e) { return Error(400, "invalid_json", "Invalid message JSON: " + e.Message); }
        if (request is null || request.MatchId != MatchId) return Error(409, "stale_match", "Fetch the current matchId before sending a message");
        if (!ValidRequestId(request.RequestId)) return Error(400, "invalid_request_id", "requestId must be 1-64 ASCII letters, digits, hyphens or underscores");
        if (request.Channel is not ("all" or "team")) return Error(400, "invalid_channel", "channel must be all or team");
        if (!BattleChat.NormalizeText(request.Text, out var text, out var error)) return Error(400, error, "Message must contain 1-280 Unicode characters after filtering");
        if (chatReceipts.TryGetValue((player, request.RequestId!), out var previous))
            return previous.Channel == request.Channel && previous.Text == text ? Json(200, ChatReceipt(player, request.RequestId!, previous.Message)) : Error(409, "request_id_conflict", "This message requestId was used with different content");
        if (chatReceipts.Count >= MaxReceipts) return Error(429, "capacity", "Per-match message receipt capacity reached");
        if (!Chat.TrySend(player, request.Channel, text, world.Tick, out var message, out error))
            return Error(error == "rate_limited" ? 429 : error == "player_inactive" ? 409 : 400, error, "Message rejected: " + error);
        chatReceipts.Add((player, request.RequestId!), (request.Channel, text, message!));
        return Json(201, ChatReceipt(player, request.RequestId!, message!));
    }
    object ChatReceipt(int player, string requestId, BattleChatMessage message) => new { matchId = MatchId, playerId = player, requestId, message };
    sealed class ChatRequest
    {
        public string? MatchId { get; init; }
        public string? RequestId { get; init; }
        public string? Channel { get; init; }
        public string? Text { get; init; }
    }
}
