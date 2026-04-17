namespace web.Components.Shared;

public record WatchHistoryMetricsDto(
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
