using api.Models;

namespace api;

public sealed record StoredWatchHistorySummary(
    long LibraryItemId,
    int TotalCount,
    int DistinctUserCount,
    int DirectPlayCount,
    int TranscodeCount,
    int ErrorCount,
    DateTimeOffset? LastImportedAtUtc,
    DateTimeOffset? LastOccurredAtUtc,
    string LastWatchedBy,
    string TopClient,
    string State,
    string Summary);

public static class StoredWatchHistorySummaryBuilder
{
    public static StoredWatchHistorySummary Build(long libraryItemId, IEnumerable<WatchHistoryEntry> rows)
    {
        var items = (rows ?? []).ToList();
        if (items.Count == 0)
        {
            return new StoredWatchHistorySummary(
                libraryItemId,
                0,
                0,
                0,
                0,
                0,
                null,
                null,
                string.Empty,
                string.Empty,
                "NoEvidence",
                "No internal watch history has been captured for this item yet.");
        }

        var directPlayCount = items.Count(IsDirectPlay);
        var transcodeCount = items.Count(IsTranscode);
        var errorCount = items.Count(HasError);
        var distinctUserCount = items
            .Select(x => x.UserName?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var latest = items.OrderByDescending(x => x.OccurredAtUtc).First();
        var topClient = items
            .Where(x => !string.IsNullOrWhiteSpace(x.ClientName))
            .GroupBy(x => x.ClientName.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Key)
            .FirstOrDefault() ?? string.Empty;

        var state = errorCount > 0 || transcodeCount > 0
            ? "IssuesObserved"
            : "Healthy";

        var summary = state == "Healthy"
            ? $"Stored {items.Count} watch event(s) with direct play observed and no known playback errors."
            : $"Stored {items.Count} watch event(s), including {transcodeCount} transcode event(s) and {errorCount} error event(s).";

        return new StoredWatchHistorySummary(
            libraryItemId,
            items.Count,
            distinctUserCount,
            directPlayCount,
            transcodeCount,
            errorCount,
            items.Max(x => x.ImportedAtUtc),
            latest.OccurredAtUtc,
            latest.UserName,
            topClient,
            state,
            summary);
    }

    private static bool IsDirectPlay(WatchHistoryEntry row)
        => !HasError(row)
           && !IsTranscode(row)
           && ContainsToken(row.Decision, "direct");

    private static bool IsTranscode(WatchHistoryEntry row)
        => ContainsToken(row.TranscodeDecision, "transcode")
           || ContainsToken(row.Decision, "transcode")
           || (ContainsToken(row.VideoDecision, "transcode") && ContainsToken(row.AudioDecision, "transcode"));

    private static bool HasError(WatchHistoryEntry row)
        => !string.IsNullOrWhiteSpace(row.ErrorMessage);

    private static bool ContainsToken(string? value, string token)
        => !string.IsNullOrWhiteSpace(value)
           && value.Contains(token, StringComparison.OrdinalIgnoreCase);
}
