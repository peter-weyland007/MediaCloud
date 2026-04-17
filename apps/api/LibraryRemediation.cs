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
    int? RemoteLookupParentItemId,
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
    bool ApprovalRequired,
    string ApprovalReason,
    string PolicySummary,
    string NotesHandling,
    string ProfileDecision,
    string ProfileSummary,
    string PolicyState,
    string NextActionSummary);

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
                true,
                "Replacement changes the tracked file set and may blacklist the current release, so operator approval is required.",
                "High-confidence file problem. MediaCloud will send a queued replacement search now.",
                "Issue notes are recorded for audit and operator context; they do not change the outgoing arr command yet.",
                "current_profile_ok",
                "Current acquisition profile does not block replacement for this issue type.",
                "bad_media_file",
                "Blacklist the current release if possible, then queue a replacement search now."),
            "wrong_version" => new LibraryRemediationIntent(
                normalizedIssueType,
                "search_replacement",
                "release_mismatch",
                "high",
                true,
                true,
                false,
                true,
                true,
                "Replacement and blacklist actions can change the active release, so operator approval is required.",
                "Wrong version/cut is a strong replacement candidate. MediaCloud will queue a replacement search now.",
                "Issue notes are recorded for audit and operator context; they do not change the outgoing arr command yet.",
                "current_profile_ok",
                "Current acquisition profile does not block replacement for this issue type.",
                "replacement_recommended",
                "Queue a replacement search for the expected cut or version, and blacklist the current release when supported."),
            "audio_language_mismatch" or "subtitle_missing" or "subtitle_language_mismatch" or "audio_out_of_sync" or "quality_issue" or "runtime_mismatch" or "playback_failure" or "wrong_language" => new LibraryRemediationIntent(
                normalizedIssueType,
                "search_replacement",
                "quality_or_playback",
                "medium",
                true,
                false,
                false,
                true,
                true,
                "Replacement search changes the tracked file path and should be explicitly approved by an operator.",
                "MediaCloud will queue a replacement search for this issue type and record the reason for follow-up.",
                "Issue notes are recorded for audit and operator context; they do not change the outgoing arr command yet.",
                "current_profile_ok",
                "Current acquisition profile looks compatible with a replacement search, so MediaCloud can proceed.",
                "replacement_recommended",
                "Queue a replacement search, then verify the latest playback and metadata evidence before calling it fixed."),
            "subtitle_unusable" => new LibraryRemediationIntent(
                normalizedIssueType,
                "bazarr_subtitles",
                "subtitle_only",
                "medium",
                false,
                false,
                false,
                true,
                false,
                "Subtitle-only remediation does not replace the file, so operator approval is not required for planning.",
                "Subtitle-only pain should go through a subtitle workflow first before replacing the entire file.",
                "Issue notes are recorded for audit and operator context; they do not change the outbound subtitle workflow yet.",
                "subtitle_workflow_first",
                "Subtitle remediation should start with subtitle acquisition/replacement before broader file replacement.",
                "subtitle_workflow_recommended",
                "Start with subtitle remediation first, then verify whether playback improved before considering a replacement search."),
            "audio_wrong_during_playback" => new LibraryRemediationIntent(
                normalizedIssueType,
                "safe_audio_remediation",
                "audio_only",
                "medium",
                false,
                false,
                false,
                true,
                true,
                "Audio normalization or remux changes the playable artifact, so operator approval is required before execution.",
                "Audio-only playback pain should prefer a safe audio remediation candidate before replacing the whole file.",
                "Issue notes are recorded for audit and operator context; they do not change the outbound ffmpeg plan yet.",
                "audio_remediation_candidate",
                "The file looks like an audio-track problem first, so a safe audio remediation candidate is a better first step than full replacement.",
                "safe_audio_candidate",
                "Review and approve a safe audio remediation plan first, then verify playback before escalating to replacement."),
            "playback_stall" or "resume_starts_over" or "seeking_issue" => new LibraryRemediationIntent(
                normalizedIssueType,
                "manual_review",
                "playback_behavior",
                "medium",
                false,
                false,
                true,
                true,
                false,
                "Manual review does not alter files, so no execution approval applies.",
                "Behavior-only playback problems need evidence review before MediaCloud changes the file.",
                "Issue notes are recorded for audit and operator context; they do not change execution behavior yet.",
                "review_evidence_first",
                "Review playback evidence and reconciliation state first; do not jump straight to replacement for resume/seek/stall complaints.",
                "manual_review_required",
                "Review playback evidence first. Escalate to replacement only if repeated evidence shows a real file-wide problem."),
            "device_specific_issue" => new LibraryRemediationIntent(
                normalizedIssueType,
                "manual_review",
                "device_specific",
                "low",
                false,
                false,
                true,
                true,
                false,
                "Manual review does not alter files, so no execution approval applies.",
                "A device-specific issue should stay in manual review until broader evidence shows the file is actually at fault.",
                "Issue notes are recorded for audit and operator context only.",
                "device_review_first",
                "This report is scoped to one client/device, so MediaCloud should review the client path before changing the media file.",
                "manual_review_required",
                "Review the affected client/device first. Do not replace the file unless broader evidence points to a file-wide problem."),
            "metadata_issue" => new LibraryRemediationIntent(
                normalizedIssueType,
                "manual_review",
                "metadata_or_mapping",
                "low",
                false,
                false,
                true,
                true,
                false,
                "Manual review does not alter files, so no execution approval applies.",
                "Metadata/mapping issues stay in manual review. MediaCloud should not queue a replacement search for this issue type.",
                "Issue notes are recorded for audit and operator context only.",
                "manual_review_only",
                "This issue is not a file-acquisition problem, so profile-aware remediation does not apply.",
                "manual_review_required",
                "Do not queue replacement. Review IDs, mapping, and source coverage manually."),
            _ => new LibraryRemediationIntent(
                normalizedIssueType,
                "search_replacement",
                "other",
                "low",
                true,
                false,
                false,
                true,
                true,
                "Generic replacement search changes the active file path, so operator approval is required.",
                "Fallback remediation intent: MediaCloud can queue a generic replacement search, but the issue needs human judgment.",
                "Issue notes are recorded for audit and operator context; they do not change the outgoing arr command yet.",
                "current_profile_ok",
                "No profile-specific blocker was detected, but this fallback path still needs human judgment.",
                "insufficient_evidence",
                "Gather clearer evidence if possible, but a generic replacement search is still available if an operator wants to try it.")
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
            _ => new LibraryRemediationPlan(false, string.Empty, string.Empty, null, string.Empty, null, false, null, $"Unsupported media type '{mediaType}'.")
        };
    }

    private static LibraryRemediationPlan BuildMoviePlan(IReadOnlyList<LibraryRemediationSourceLink> sourceLinks)
    {
        var radarrLink = GetPreferredSourceLink(sourceLinks, "radarr", "movie");
        var externalItemId = ParseExternalId(radarrLink?.ExternalId);

        return new LibraryRemediationPlan(
            true,
            "radarr",
            "Radarr",
            radarrLink?.IntegrationId,
            "MoviesSearch",
            externalItemId,
            externalItemId is null,
            null,
            radarrLink is null
                ? "Search replacement via Radarr. Existing Radarr link missing, so MediaCloud will look the movie up remotely first."
                : "Search replacement via Radarr.");
    }

    private static LibraryRemediationPlan BuildEpisodePlan(IReadOnlyList<LibraryRemediationSourceLink> sourceLinks)
    {
        var episodeLink = GetSourceLink(sourceLinks, "sonarr", "episode");
        var seriesLink = GetSourceLink(sourceLinks, "sonarr", "series");

        var externalItemId = ParseExternalId(episodeLink?.ExternalId);
        var fallbackSeriesId = ParseExternalId(seriesLink?.ExternalId);
        var integrationId = episodeLink?.IntegrationId ?? seriesLink?.IntegrationId;
        var requiresRemoteLookup = externalItemId is null;

        return new LibraryRemediationPlan(
            true,
            "sonarr",
            "Sonarr",
            integrationId,
            "EpisodeSearch",
            externalItemId,
            requiresRemoteLookup,
            requiresRemoteLookup ? fallbackSeriesId : null,
            requiresRemoteLookup
                ? fallbackSeriesId.HasValue
                    ? "Search replacement via Sonarr. Episode-level link missing, so MediaCloud will resolve the episode from the linked series first."
                    : "Search replacement via Sonarr. An episode-level Sonarr link is required."
                : "Search replacement via Sonarr.");
    }

    private static LibraryRemediationPlan BuildSeriesPlan(IReadOnlyList<LibraryRemediationSourceLink> sourceLinks)
    {
        var sonarrLink = GetPreferredSourceLink(sourceLinks, "sonarr", "series");
        var externalItemId = ParseExternalId(sonarrLink?.ExternalId);
        return new LibraryRemediationPlan(
            true,
            "sonarr",
            "Sonarr",
            sonarrLink?.IntegrationId,
            "SeriesSearch",
            externalItemId,
            externalItemId is null,
            null,
            externalItemId is null
                ? "Search replacement via Sonarr. A series-level Sonarr link is required."
                : "Search replacement via Sonarr.");
    }

    private static LibraryRemediationPlan BuildAlbumPlan(IReadOnlyList<LibraryRemediationSourceLink> sourceLinks)
    {
        var albumLink = GetSourceLink(sourceLinks, "lidarr", "album");
        var artistLink = GetSourceLink(sourceLinks, "lidarr", "artist");

        var externalItemId = ParseExternalId(albumLink?.ExternalId);
        var fallbackArtistId = ParseExternalId(artistLink?.ExternalId);
        var integrationId = albumLink?.IntegrationId ?? artistLink?.IntegrationId;
        var requiresRemoteLookup = externalItemId is null;
        return new LibraryRemediationPlan(
            true,
            "lidarr",
            "Lidarr",
            integrationId,
            "AlbumSearch",
            externalItemId,
            requiresRemoteLookup,
            requiresRemoteLookup ? fallbackArtistId : null,
            requiresRemoteLookup
                ? fallbackArtistId.HasValue
                    ? "Search replacement via Lidarr. Album-level link missing, so MediaCloud will resolve the album from the linked artist first."
                    : "Search replacement via Lidarr. An album-level Lidarr link is required."
                : "Search replacement via Lidarr.");
    }

    private static LibraryRemediationSourceLink? GetPreferredSourceLink(
        IReadOnlyList<LibraryRemediationSourceLink> sourceLinks,
        string serviceKey,
        string preferredExternalType)
        => sourceLinks
            .Where(x => !x.IsDeletedAtSource && string.Equals(x.ServiceKey, serviceKey, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => string.Equals(x.ExternalType, preferredExternalType, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

    private static LibraryRemediationSourceLink? GetSourceLink(
        IReadOnlyList<LibraryRemediationSourceLink> sourceLinks,
        string serviceKey,
        string externalType)
        => sourceLinks
            .Where(x => !x.IsDeletedAtSource
                && string.Equals(x.ServiceKey, serviceKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.ExternalType, externalType, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

    private static int? ParseExternalId(string? externalId)
        => int.TryParse((externalId ?? string.Empty).Trim(), out var value) && value > 0 ? value : null;
}
