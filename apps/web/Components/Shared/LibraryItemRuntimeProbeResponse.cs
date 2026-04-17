namespace web.Components.Shared;

public sealed record LibraryItemRuntimeProbeResponse(
    long Id,
    string MediaType,
    string Title,
    string PrimaryFilePath,
    bool FileExists,
    bool Success,
    double? ActualRuntimeMinutes,
    string Message,
    int? ProbeExitCode,
    string ProbeError,
    string PlayabilityScore,
    int PlayabilityNumericScore,
    string PlayabilitySummary,
    DateTimeOffset? PlayabilityCheckedAtUtc);
