namespace web.Components.Shared;

public record MediaPlaybackSmokeTestRequest(int SampleSeconds = 45, int SeekPercent = 35, bool RunSample = true);

public record MediaPlaybackSmokeTestResponse(
    long LibraryItemId,
    string TargetProfileName,
    string RecommendationKey,
    string RecommendationTitle,
    string LikelyDecision,
    string VerdictSeverity,
    string OperatorSummary,
    string WhySummary,
    IReadOnlyList<string> Reasons,
    bool SamplePlanned,
    string SampleCommandPreview,
    string SampleOutputPath,
    int SampleSeconds,
    int SeekPercent,
    int SeekSeconds,
    bool SampleAttempted,
    bool SampleSucceeded,
    string SampleSummary,
    string SampleProbeSummary,
    int? ExitCode,
    string ErrorMessage,
    DateTimeOffset TestedAtUtc);
