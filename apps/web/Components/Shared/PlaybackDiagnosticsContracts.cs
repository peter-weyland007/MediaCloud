namespace web.Components.Shared;

public record PlaybackDiagnosticDto(
    long Id,
    long LibraryItemId,
    string SourceService,
    string SourceDisplayName,
    string ExternalId,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset ImportedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? StoppedAtUtc,
    string UserName,
    string ClientName,
    string Player,
    string Product,
    string Platform,
    string Decision,
    string TranscodeDecision,
    string VideoDecision,
    string AudioDecision,
    string SubtitleDecision,
    string Container,
    string VideoCodec,
    string AudioCodec,
    string SubtitleCodec,
    string QualityProfile,
    string HealthLabel,
    string Summary,
    string SuspectedCause,
    string ErrorMessage,
    string LogSnippet);

public record PullPlaybackDiagnosticsRequest(int HoursBack = 48, int MaxItems = 10, bool IncludeServerLogs = true);

public record PullPlaybackDiagnosticsResponse(long LibraryItemId, int ImportedCount, int UpdatedCount, int TotalCount, bool UsedTautulli, bool UsedPlex, string Message);

public record PlaybackDiagnosticsSummaryDto(
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
