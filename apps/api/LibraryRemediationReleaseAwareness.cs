using System.Text.Json;

namespace api;

public sealed record LibraryRemediationHistoryCandidate(
    long? HistoryRecordId,
    string EventType,
    string SourceTitle,
    string DownloadId,
    string DataJson,
    DateTimeOffset? EventTimeUtc,
    int? TargetExternalItemId = null,
    string Protocol = "");

public sealed record LibraryRemediationReleaseContext(
    string ServiceKey,
    int? ExternalItemId,
    string FilePath,
    string FileName,
    string QualityProfile,
    string SourceTitle,
    bool HasHistoryMatch,
    long? HistoryRecordId,
    string HistoryEventType,
    string HistorySourceTitle,
    string HistoryDownloadId,
    string ReleaseSummary,
    string RawContextJson);

public sealed record LibraryRemediationBlacklistPlan(
    bool ShouldAttempt,
    long? HistoryRecordId,
    string EndpointPath,
    string Reason);

public static class LibraryRemediationReleaseAwareness
{
    public static LibraryRemediationHistoryCandidate? PickBestHistoryCandidate(IEnumerable<LibraryRemediationHistoryCandidate> candidates)
    {
        return candidates
            .OrderByDescending(GetHistoryPriority)
            .ThenByDescending(x => x.EventTimeUtc ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
    }

    public static LibraryRemediationReleaseContext BuildContext(
        string serviceKey,
        int? externalItemId,
        string? filePath,
        string? qualityProfile,
        string? sourceTitle,
        IReadOnlyList<LibraryRemediationHistoryCandidate> historyCandidates)
    {
        var normalizedFilePath = (filePath ?? string.Empty).Trim();
        var fileName = string.IsNullOrWhiteSpace(normalizedFilePath)
            ? string.Empty
            : Path.GetFileName(normalizedFilePath);
        var chosen = PickBestHistoryCandidate(historyCandidates);
        var displayRelease = FirstNonEmpty(chosen?.SourceTitle, fileName, sourceTitle, "Unknown release");
        var quality = string.IsNullOrWhiteSpace(qualityProfile) ? "Unknown quality" : qualityProfile.Trim();
        var summary = $"{displayRelease} · {quality}";
        if (!string.IsNullOrWhiteSpace(chosen?.EventType))
        {
            summary += $" · {chosen.EventType}";
        }

        var raw = JsonSerializer.Serialize(new
        {
            serviceKey,
            externalItemId,
            filePath = normalizedFilePath,
            fileName,
            qualityProfile = qualityProfile ?? string.Empty,
            sourceTitle = sourceTitle ?? string.Empty,
            chosenHistory = chosen,
            historyCandidates
        });

        return new LibraryRemediationReleaseContext(
            serviceKey,
            externalItemId,
            normalizedFilePath,
            fileName,
            qualityProfile ?? string.Empty,
            sourceTitle ?? string.Empty,
            chosen is not null,
            chosen?.HistoryRecordId,
            chosen?.EventType ?? string.Empty,
            chosen?.SourceTitle ?? string.Empty,
            chosen?.DownloadId ?? string.Empty,
            summary,
            raw);
    }

    private static int GetHistoryPriority(LibraryRemediationHistoryCandidate candidate)
    {
        var normalized = (candidate.EventType ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "downloadfolderimported" => 4,
            "downloadimported" => 4,
            "imported" => 4,
            "grabbed" => 3,
            "downloadfailed" => 2,
            _ => 1
        };
    }

    internal static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;
}

public static class LibraryRemediationBlacklistPlanner
{
    public static LibraryRemediationBlacklistPlan BuildPlan(LibraryRemediationIntent intent, LibraryRemediationReleaseContext context)
    {
        if (!intent.ShouldBlacklistCurrentRelease)
        {
            return new LibraryRemediationBlacklistPlan(false, null, string.Empty, "Policy does not require blacklist.");
        }

        if (!context.HistoryRecordId.HasValue || context.HistoryRecordId.Value <= 0)
        {
            return new LibraryRemediationBlacklistPlan(false, null, string.Empty, "No history record available to blacklist.");
        }

        var endpoint = context.ServiceKey.Trim().ToLowerInvariant() switch
        {
            "radarr" => $"/api/v3/history/failed/{context.HistoryRecordId.Value}",
            "sonarr" => $"/api/v3/history/failed/{context.HistoryRecordId.Value}",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return new LibraryRemediationBlacklistPlan(false, null, string.Empty, $"Blacklist is not supported for service '{context.ServiceKey}'.");
        }

        var reason = $"Blacklist current release '{LibraryRemediationReleaseAwareness.FirstNonEmpty(context.HistorySourceTitle, context.ReleaseSummary, "unknown release")}' before replacement search.";
        return new LibraryRemediationBlacklistPlan(true, context.HistoryRecordId, endpoint, reason);
    }
}
