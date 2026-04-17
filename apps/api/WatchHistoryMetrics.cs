namespace api;

public sealed record WatchHistoryEvent(
    DateTimeOffset OccurredAtUtc,
    string UserName,
    string ClientLabel,
    string ItemLabel,
    bool IsTranscode,
    bool HasError);

public sealed record WatchHistorySummary(
    string ScopeLabel,
    int TotalPlays,
    int DistinctUsers,
    int DirectPlayCount,
    int TranscodeCount,
    int ErrorCount,
    DateTimeOffset? LastWatchedAtUtc,
    string LastWatchedBy,
    string LastWatchedTitle,
    string TopClient,
    string TopWatchedTitle);

public static class WatchHistoryMetrics
{
    public static WatchHistorySummary BuildSummary(IEnumerable<WatchHistoryEvent> events, string scopeLabel)
    {
        var list = (events ?? []).OrderByDescending(x => x.OccurredAtUtc).ToList();
        var last = list.FirstOrDefault();
        var totalPlays = list.Count;
        var transcodeCount = list.Count(x => x.IsTranscode);
        var errorCount = list.Count(x => x.HasError);
        var topClient = list
            .GroupBy(x => Normalize(x.ClientLabel))
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.First().ClientLabel)
            .FirstOrDefault();
        var topWatchedTitle = list
            .GroupBy(x => Normalize(x.ItemLabel))
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.First().ItemLabel)
            .FirstOrDefault();

        return new WatchHistorySummary(
            string.IsNullOrWhiteSpace(scopeLabel) ? "item" : scopeLabel.Trim(),
            totalPlays,
            list.Select(x => Normalize(x.UserName)).Where(x => x.Length > 0).Distinct().Count(),
            totalPlays - transcodeCount,
            transcodeCount,
            errorCount,
            last?.OccurredAtUtc,
            DisplayOrDash(last?.UserName),
            DisplayOrDash(last?.ItemLabel),
            DisplayOrDash(topClient),
            DisplayOrDash(topWatchedTitle));
    }

    private static string Normalize(string? value)
        => (value ?? string.Empty).Trim();

    private static string DisplayOrDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
}
