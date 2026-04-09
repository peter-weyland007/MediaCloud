namespace api;

public sealed record LibraryRemediationSourceLink(
    string ServiceKey,
    long IntegrationId,
    string ExternalId,
    string ExternalType,
    bool IsDeletedAtSource);

public sealed record LibraryRemediationPlan(
    bool IsSupported,
    string ServiceKey,
    string DisplayName,
    long? IntegrationId,
    string CommandName,
    int? ExternalItemId,
    bool RequiresRemoteLookup,
    string Message);

public sealed record LibraryRemediationIntent(
    string IssueType,
    string RequestedAction,
    string ReasonCategory,
    string Confidence,
    bool ShouldSearchNow,
    bool ShouldBlacklistCurrentRelease,
    bool NeedsManualReview,
    bool NotesRecordedOnly,
    string PolicySummary,
    string NotesHandling,
    string ProfileDecision,
    string ProfileSummary);

public static class LibraryRemediationPlanner
{
    public static LibraryRemediationIntent BuildIntent(string? issueType, string? notes)
    {
        var normalizedIssueType = string.IsNullOrWhiteSpace(issueType)
            ? "other"
            : issueType.Trim().ToLowerInvariant();

        return normalizedIssueType switch
        {
            "corrupt_file" => new LibraryRemediationIntent(
                normalizedIssueType,
                "search_replacement",
                "file_problem",
                "high",
                true,
                true,
                false,
                true,
                "High-confidence file problem. MediaCloud will send a queued replacement search now.",
                "Issue notes are recorded for audit and operator context; they do not change the outgoing arr command yet.",
                "current_profile_ok",
                "Current acquisition profile does not block replacement for this issue type."),
            "wrong_version" => new LibraryRemediationIntent(
                normalizedIssueType,
                "search_replacement",
                "release_mismatch",
                "high",
                true,
                true,
                false,
                true,
                "Wrong version/cut is a strong replacement candidate. MediaCloud will queue a replacement search now.",
                "Issue notes are recorded for audit and operator context; they do not change the outgoing arr command yet.",
                "current_profile_ok",
                "Current acquisition profile does not block replacement for this issue type."),
            "audio_language_mismatch" or "subtitle_missing" or "subtitle_language_mismatch" or "audio_out_of_sync" or "quality_issue" or "runtime_mismatch" or "playback_failure" or "wrong_language" => new LibraryRemediationIntent(
                normalizedIssueType,
                "search_replacement",
                "quality_or_playback",
                "medium",
                true,
                false,
                false,
                true,
                "MediaCloud will queue a replacement search for this issue type and record the reason for follow-up.",
                "Issue notes are recorded for audit and operator context; they do not change the outgoing arr command yet.",
                "current_profile_ok",
                "Current acquisition profile looks compatible with a replacement search, so MediaCloud can proceed."),
            "metadata_issue" => new LibraryRemediationIntent(
                normalizedIssueType,
                "manual_review",
                "metadata_or_mapping",
                "low",
                false,
                false,
                true,
                true,
                "Metadata/mapping issues stay in manual review. MediaCloud should not queue a replacement search for this issue type.",
                "Issue notes are recorded for audit and operator context only.",
                "manual_review_only",
                "This issue is not a file-acquisition problem, so profile-aware remediation does not apply."),
            _ => new LibraryRemediationIntent(
                normalizedIssueType,
                "search_replacement",
                "other",
                "low",
                true,
                false,
                false,
                true,
                "Fallback remediation intent: MediaCloud can queue a generic replacement search, but the issue needs human judgment.",
                "Issue notes are recorded for audit and operator context; they do not change the outgoing arr command yet.",
                "current_profile_ok",
                "No profile-specific blocker was detected, but this fallback path still needs human judgment.")
        };
    }
    public static LibraryRemediationPlan PlanSearchReplacement(string? mediaType, IReadOnlyList<LibraryRemediationSourceLink> sourceLinks)
    {
        var normalizedMediaType = (mediaType ?? string.Empty).Trim().ToLowerInvariant();
        return normalizedMediaType switch
        {
            "movie" => BuildMoviePlan(sourceLinks),
            "episode" => BuildEpisodePlan(sourceLinks),
            "series" => BuildSeriesPlan(sourceLinks),
            "album" => BuildAlbumPlan(sourceLinks),
            _ => new LibraryRemediationPlan(false, string.Empty, string.Empty, null, string.Empty, null, false, $"Unsupported media type '{mediaType}'.")
        };
    }

    private static LibraryRemediationPlan BuildMoviePlan(IReadOnlyList<LibraryRemediationSourceLink> sourceLinks)
    {
        var radarrLink = sourceLinks
            .Where(x => !x.IsDeletedAtSource && string.Equals(x.ServiceKey, "radarr", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => string.Equals(x.ExternalType, "movie", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

        return new LibraryRemediationPlan(
            true,
            "radarr",
            "Radarr",
            radarrLink?.IntegrationId,
            "MoviesSearch",
            ParseExternalId(radarrLink?.ExternalId),
            radarrLink is null || ParseExternalId(radarrLink.ExternalId) is null,
            radarrLink is null
                ? "Search replacement via Radarr. Existing Radarr link missing, so MediaCloud will look the movie up remotely first."
                : "Search replacement via Radarr.");
    }

    private static LibraryRemediationPlan BuildEpisodePlan(IReadOnlyList<LibraryRemediationSourceLink> sourceLinks)
    {
        var sonarrLink = sourceLinks
            .Where(x => !x.IsDeletedAtSource && string.Equals(x.ServiceKey, "sonarr", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => string.Equals(x.ExternalType, "episode", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

        var externalItemId = string.Equals(sonarrLink?.ExternalType, "episode", StringComparison.OrdinalIgnoreCase)
            ? ParseExternalId(sonarrLink?.ExternalId)
            : null;

        return new LibraryRemediationPlan(
            true,
            "sonarr",
            "Sonarr",
            sonarrLink?.IntegrationId,
            "EpisodeSearch",
            externalItemId,
            externalItemId is null,
            externalItemId is null
                ? "Search replacement via Sonarr. An episode-level Sonarr link is required."
                : "Search replacement via Sonarr.");
    }

    private static LibraryRemediationPlan BuildSeriesPlan(IReadOnlyList<LibraryRemediationSourceLink> sourceLinks)
    {
        var sonarrLink = sourceLinks
            .Where(x => !x.IsDeletedAtSource && string.Equals(x.ServiceKey, "sonarr", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => string.Equals(x.ExternalType, "series", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

        var externalItemId = ParseExternalId(sonarrLink?.ExternalId);
        return new LibraryRemediationPlan(
            true,
            "sonarr",
            "Sonarr",
            sonarrLink?.IntegrationId,
            "SeriesSearch",
            externalItemId,
            externalItemId is null,
            externalItemId is null
                ? "Search replacement via Sonarr. A series-level Sonarr link is required."
                : "Search replacement via Sonarr.");
    }

    private static LibraryRemediationPlan BuildAlbumPlan(IReadOnlyList<LibraryRemediationSourceLink> sourceLinks)
    {
        var lidarrLink = sourceLinks
            .Where(x => !x.IsDeletedAtSource && string.Equals(x.ServiceKey, "lidarr", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => string.Equals(x.ExternalType, "album", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

        var externalItemId = ParseExternalId(lidarrLink?.ExternalId);
        return new LibraryRemediationPlan(
            true,
            "lidarr",
            "Lidarr",
            lidarrLink?.IntegrationId,
            "AlbumSearch",
            externalItemId,
            externalItemId is null,
            externalItemId is null
                ? "Search replacement via Lidarr. An album-level Lidarr link is required."
                : "Search replacement via Lidarr.");
    }

    private static int? ParseExternalId(string? externalId)
        => int.TryParse((externalId ?? string.Empty).Trim(), out var value) && value > 0 ? value : null;
}
