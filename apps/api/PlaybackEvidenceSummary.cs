using api.Models;

namespace api;

public sealed record PlaybackEvidenceSummary(
    long LibraryItemId,
    int TotalCount,
    DateTimeOffset? LastImportedAtUtc,
    DateTimeOffset? LastOccurredAtUtc,
    int DirectPlayCount,
    int FullTranscodeCount,
    int AudioTranscodeCount,
    int SubtitleIssueCount,
    int FailureCount,
    int RepeatedIssueKindCount,
    bool HasRepeatedIssues,
    string EvidenceState,
    string Summary);

public static class PlaybackEvidenceSummaryBuilder
{
    private const int RepeatedIssueThreshold = 2;

    public static PlaybackEvidenceSummary Build(long libraryItemId, IEnumerable<PlaybackDiagnosticEntry> diagnostics)
    {
        var rows = (diagnostics ?? []).ToList();
        if (rows.Count == 0)
        {
            return new PlaybackEvidenceSummary(
                libraryItemId,
                0,
                null,
                null,
                0,
                0,
                0,
                0,
                0,
                0,
                false,
                "NoEvidence",
                "No Plex or Tautulli playback evidence has been captured for this item yet.");
        }

        var directPlayCount = rows.Count(IsDirectPlay);
        var fullTranscodeCount = rows.Count(IsFullTranscode);
        var audioTranscodeCount = rows.Count(HasAudioIssue);
        var subtitleIssueCount = rows.Count(HasSubtitleIssue);
        var failureCount = rows.Count(HasFailure);
        var repeatedIssueKindCount = new[]
        {
            fullTranscodeCount,
            audioTranscodeCount,
            subtitleIssueCount,
            failureCount
        }.Count(x => x >= RepeatedIssueThreshold);
        var hasRepeatedIssues = repeatedIssueKindCount > 0;

        var evidenceState = hasRepeatedIssues
            ? "RepeatedIssues"
            : fullTranscodeCount > 0 || audioTranscodeCount > 0 || subtitleIssueCount > 0 || failureCount > 0
                ? "IssuesObserved"
                : "Healthy";

        var summary = evidenceState switch
        {
            "RepeatedIssues" => BuildRepeatedIssuesSummary(fullTranscodeCount, audioTranscodeCount, subtitleIssueCount, failureCount),
            "IssuesObserved" => BuildSingleIssueSummary(fullTranscodeCount, audioTranscodeCount, subtitleIssueCount, failureCount),
            _ => "Captured playback evidence is healthy so far, with direct play observed and no repeated issue patterns yet."
        };

        return new PlaybackEvidenceSummary(
            libraryItemId,
            rows.Count,
            rows.Max(x => x.ImportedAtUtc),
            rows.Max(x => x.OccurredAtUtc),
            directPlayCount,
            fullTranscodeCount,
            audioTranscodeCount,
            subtitleIssueCount,
            failureCount,
            repeatedIssueKindCount,
            hasRepeatedIssues,
            evidenceState,
            summary);
    }

    private static string BuildRepeatedIssuesSummary(int fullTranscodeCount, int audioTranscodeCount, int subtitleIssueCount, int failureCount)
    {
        var segments = new List<string>();
        if (fullTranscodeCount >= RepeatedIssueThreshold) segments.Add($"repeated full transcodes ({fullTranscodeCount})");
        if (audioTranscodeCount >= RepeatedIssueThreshold) segments.Add($"repeated audio transcodes ({audioTranscodeCount})");
        if (subtitleIssueCount >= RepeatedIssueThreshold) segments.Add($"repeated subtitle-driven playback issues ({subtitleIssueCount})");
        if (failureCount >= RepeatedIssueThreshold) segments.Add($"repeated playback failures ({failureCount})");

        return segments.Count == 0
            ? "Playback evidence shows repeated issue patterns."
            : $"Playback evidence shows {string.Join(", ", segments)}.";
    }

    private static string BuildSingleIssueSummary(int fullTranscodeCount, int audioTranscodeCount, int subtitleIssueCount, int failureCount)
    {
        var segments = new List<string>();
        if (fullTranscodeCount > 0) segments.Add($"full transcodes ({fullTranscodeCount})");
        if (audioTranscodeCount > 0) segments.Add($"audio transcodes ({audioTranscodeCount})");
        if (subtitleIssueCount > 0) segments.Add($"subtitle-driven playback issues ({subtitleIssueCount})");
        if (failureCount > 0) segments.Add($"playback failures ({failureCount})");

        return segments.Count == 0
            ? "Playback evidence shows a minor issue pattern to watch."
            : $"Playback evidence has shown {string.Join(", ", segments)}.";
    }

    private static bool IsDirectPlay(PlaybackDiagnosticEntry row)
        => !IsFullTranscode(row)
           && !HasAudioIssue(row)
           && !HasSubtitleIssue(row)
           && !HasFailure(row)
           && ContainsToken(row.Decision, "direct");

    private static bool IsFullTranscode(PlaybackDiagnosticEntry row)
        => ContainsToken(row.TranscodeDecision, "transcode")
           || ContainsToken(row.Decision, "transcode")
           || (ContainsToken(row.VideoDecision, "transcode") && ContainsToken(row.AudioDecision, "transcode"));

    private static bool HasAudioIssue(PlaybackDiagnosticEntry row)
        => ContainsToken(row.AudioDecision, "transcode")
           || ContainsToken(row.AudioDecision, "copy not supported")
           || ContainsToken(row.SuspectedCause, "audio")
           || row.AudioCodec.Trim().ToLowerInvariant() is "dts" or "dca" or "truehd" or "flac";

    private static bool HasSubtitleIssue(PlaybackDiagnosticEntry row)
    {
        var codec = row.SubtitleCodec.Trim().ToLowerInvariant();
        return ContainsToken(row.SubtitleDecision, "transcode")
            || ContainsToken(row.SuspectedCause, "subtitle")
            || codec is "hdmv_pgs_subtitle" or "pgs" or "dvd_subtitle";
    }

    private static bool HasFailure(PlaybackDiagnosticEntry row)
        => string.Equals(row.HealthLabel, "Error", StringComparison.OrdinalIgnoreCase)
           || !string.IsNullOrWhiteSpace(row.ErrorMessage)
           || ContainsToken(row.LogSnippet, "error")
           || ContainsToken(row.LogSnippet, "failed");

    private static bool ContainsToken(string? value, string token)
        => !string.IsNullOrWhiteSpace(value)
           && value.Contains(token, StringComparison.OrdinalIgnoreCase);
}
