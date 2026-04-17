using System.Globalization;
using System.Text.Json;

public static class TautulliPlaybackImport
{
    public static Dictionary<string, string?> BuildHistoryQuery(string ratingKey, string mediaType, int hoursBack, int maxItems, DateTimeOffset? now = null)
    {
        var after = (now ?? DateTimeOffset.UtcNow)
            .AddHours(-Math.Max(1, hoursBack))
            .ToUnixTimeSeconds()
            .ToString(CultureInfo.InvariantCulture);

        return new Dictionary<string, string?>
        {
            ["rating_key"] = ratingKey,
            ["media_type"] = mediaType,
            ["length"] = Math.Max(1, maxItems).ToString(CultureInfo.InvariantCulture),
            ["order_column"] = "date",
            ["order_dir"] = "desc",
            ["after"] = after
        };
    }

    public static Dictionary<string, string?> BuildFallbackSearchQuery(string expectedTitle, string mediaType, int hoursBack, int maxItems, DateTimeOffset? now = null)
    {
        var query = BuildHistoryQuery(string.Empty, mediaType, hoursBack, Math.Max(1, maxItems) * 3, now);
        query.Remove("rating_key");
        query["search"] = expectedTitle;
        return query;
    }

    public static string? ReadStringLike(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Null) return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => value.ToString()
        };
    }

    public static string BuildSourceMessage(bool usedTautulli, bool usedPlex, int imported, int updated)
        => usedTautulli
            ? (imported > 0 || updated > 0
                ? "Tautulli returned matching playback diagnostics for this item."
                : usedPlex
                    ? "Tautulli has no matching playback diagnostics for this item yet. MediaCloud also checked active Plex sessions and found nothing current to import."
                    : "Tautulli has no matching playback diagnostics for this item yet.")
            : usedPlex
                ? (imported > 0 || updated > 0
                    ? "MediaCloud captured matching playback diagnostics from an active Plex session."
                    : "No Tautulli integration is configured, so MediaCloud checked active Plex sessions only and found nothing current to import.")
                : "No Plex or Tautulli playback integration is configured for diagnostics.";
}
