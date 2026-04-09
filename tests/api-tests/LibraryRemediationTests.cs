using api;
using Xunit;

public sealed class LibraryRemediationTests
{
    [Fact]
    public void BuildIntent_marks_corrupt_file_for_immediate_search()
    {
        var intent = LibraryRemediationPlanner.BuildIntent("corrupt_file", "file will not play");

        Assert.Equal("corrupt_file", intent.IssueType);
        Assert.Equal("search_replacement", intent.RequestedAction);
        Assert.Equal("high", intent.Confidence);
        Assert.True(intent.ShouldSearchNow);
        Assert.True(intent.ShouldBlacklistCurrentRelease);
        Assert.False(intent.NeedsManualReview);
        Assert.Contains("queued replacement search", intent.PolicySummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildIntent_keeps_metadata_issue_in_manual_review()
    {
        var intent = LibraryRemediationPlanner.BuildIntent("metadata_issue", "mapped wrong");

        Assert.Equal("metadata_issue", intent.IssueType);
        Assert.Equal("manual_review", intent.RequestedAction);
        Assert.Equal("low", intent.Confidence);
        Assert.False(intent.ShouldSearchNow);
        Assert.True(intent.NeedsManualReview);
        Assert.True(intent.NotesRecordedOnly);
    }

    [Fact]
    public void BuildIntent_treats_notes_as_audit_context_not_execution_logic()
    {
        var intent = LibraryRemediationPlanner.BuildIntent("audio_language_mismatch", "russian only");

        Assert.True(intent.NotesRecordedOnly);
        Assert.Contains("notes are recorded for audit", intent.NotesHandling, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateJob_captures_policy_and_successful_queue_result()
    {
        var now = new DateTimeOffset(2026, 4, 9, 15, 0, 0, TimeSpan.Zero);
        var intent = LibraryRemediationPlanner.BuildIntent("corrupt_file", "crc errors");
        var result = new LibraryItemRemediationResponse(41, true, "radarr", "Radarr", "MoviesSearch", 9001, false, "corrupt_file", "crc errors", "Queued Radarr replacement search for movie ID 9001.");

        var releaseContext = LibraryRemediationReleaseAwareness.BuildContext("radarr", 9001, "/data/movies/Movie.mkv", "HD-1080p", "Movie", []);
        var job = LibraryRemediationJobFactory.Create(
            libraryItemId: 41,
            libraryIssueId: 77,
            intent,
            result,
            releaseContext,
            requestedBy: "mark",
            requestedAtUtc: now,
            blacklistSucceeded: true);

        Assert.Equal(41, job.LibraryItemId);
        Assert.Equal(77, job.LibraryIssueId);
        Assert.Equal("BlacklistedAndQueued", job.Status);
        Assert.Equal("search_replacement", job.RequestedAction);
        Assert.Equal("high", job.Confidence);
        Assert.Equal("mark", job.RequestedBy);
        Assert.Equal("Queued Radarr replacement search for movie ID 9001.", job.ResultMessage);
        Assert.Contains("Movie.mkv", job.ReleaseSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Null(job.FinishedAtUtc);
    }

    [Fact]
    public void CreateJob_marks_failed_queue_as_failed_status()
    {
        var now = new DateTimeOffset(2026, 4, 9, 15, 5, 0, TimeSpan.Zero);
        var intent = LibraryRemediationPlanner.BuildIntent("wrong_version", "extended cut");
        var result = new LibraryItemRemediationResponse(41, false, "radarr", "Radarr", "MoviesSearch", 9001, false, "wrong_version", "extended cut", "Radarr returned 500.");

        var releaseContext = LibraryRemediationReleaseAwareness.BuildContext("radarr", 9001, "/data/movies/Movie.mkv", "HD-1080p", "Movie", []);
        var job = LibraryRemediationJobFactory.Create(
            libraryItemId: 41,
            libraryIssueId: null,
            intent,
            result,
            releaseContext,
            requestedBy: "mark",
            requestedAtUtc: now,
            blacklistSucceeded: false);

        Assert.Equal("Failed", job.Status);
        Assert.Equal("Radarr returned 500.", job.ResultMessage);
        Assert.Equal(now, job.RequestedAtUtc);
        Assert.Equal(now, job.FinishedAtUtc);
    }

    [Fact]
    public void PickBestHistoryCandidate_prefers_imported_event_over_grabbed()
    {
        var candidates = new[]
        {
            new LibraryRemediationHistoryCandidate(10, "grabbed", "Release A", "dl-1", string.Empty, new DateTimeOffset(2026, 4, 9, 10, 0, 0, TimeSpan.Zero)),
            new LibraryRemediationHistoryCandidate(11, "downloadFolderImported", "Release B", "dl-1", string.Empty, new DateTimeOffset(2026, 4, 9, 11, 0, 0, TimeSpan.Zero))
        };

        var chosen = LibraryRemediationReleaseAwareness.PickBestHistoryCandidate(candidates);

        Assert.NotNull(chosen);
        Assert.Equal("Release B", chosen!.SourceTitle);
        Assert.Equal("downloadFolderImported", chosen.EventType);
    }

    [Fact]
    public void BuildBlacklistPlan_uses_history_match_for_high_confidence_issue()
    {
        var intent = LibraryRemediationPlanner.BuildIntent("corrupt_file", "crc errors");
        var context = LibraryRemediationReleaseAwareness.BuildContext(
            serviceKey: "radarr",
            externalItemId: 9001,
            filePath: "/data/movies/Movie.mkv",
            qualityProfile: "HD-1080p",
            sourceTitle: "Movie",
            historyCandidates:
            [
                new LibraryRemediationHistoryCandidate(42, "downloadFolderImported", "Movie.2026.1080p-GROUP", "dl-1", string.Empty, new DateTimeOffset(2026, 4, 9, 11, 0, 0, TimeSpan.Zero))
            ]);

        var plan = LibraryRemediationBlacklistPlanner.BuildPlan(intent, context);

        Assert.True(plan.ShouldAttempt);
        Assert.Equal(42, plan.HistoryRecordId);
        Assert.Contains("Movie.2026.1080p-GROUP", plan.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildBlacklistPlan_skips_when_no_history_match_exists()
    {
        var intent = LibraryRemediationPlanner.BuildIntent("corrupt_file", "crc errors");
        var context = LibraryRemediationReleaseAwareness.BuildContext(
            serviceKey: "radarr",
            externalItemId: 9001,
            filePath: "/data/movies/Movie.mkv",
            qualityProfile: "HD-1080p",
            sourceTitle: "Movie",
            historyCandidates: []);

        var plan = LibraryRemediationBlacklistPlanner.BuildPlan(intent, context);

        Assert.False(plan.ShouldAttempt);
        Assert.Null(plan.HistoryRecordId);
    }

    [Fact]
    public void ApplyLatestDiagnostic_downgrades_playback_failure_to_manual_review_for_client_limitations()
    {
        var intent = LibraryRemediationPlanner.BuildIntent("playback_failure", "tv app failed");
        var diagnostic = new api.Models.PlaybackDiagnosticEntry
        {
            HealthLabel = "Problematic",
            SuspectedCause = "Client limitation: PGS subtitles force transcode on this TV app.",
            ErrorMessage = string.Empty,
            SubtitleDecision = "transcode",
            AudioDecision = "directplay",
            VideoDecision = "directplay"
        };

        var decided = LibraryRemediationDiagnosticsDecisioning.ApplyLatestDiagnostic(intent, diagnostic);

        Assert.False(decided.ShouldSearchNow);
        Assert.False(decided.ShouldBlacklistCurrentRelease);
        Assert.True(decided.NeedsManualReview);
        Assert.Contains("client/profile tuning", decided.PolicySummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyLatestDiagnostic_keeps_search_for_playback_failure_with_real_media_errors()
    {
        var intent = LibraryRemediationPlanner.BuildIntent("playback_failure", "tv app failed");
        var diagnostic = new api.Models.PlaybackDiagnosticEntry
        {
            HealthLabel = "Problematic",
            SuspectedCause = "File appears corrupt during playback.",
            ErrorMessage = "Input/output error while reading media file",
            SubtitleDecision = "copy",
            AudioDecision = "copy",
            VideoDecision = "copy"
        };

        var decided = LibraryRemediationDiagnosticsDecisioning.ApplyLatestDiagnostic(intent, diagnostic);

        Assert.True(decided.ShouldSearchNow);
        Assert.True(decided.ShouldBlacklistCurrentRelease);
        Assert.False(decided.NeedsManualReview);
    }

    [Fact]
    public void ApplyProfilePolicy_requires_profile_review_for_wrong_language_when_profile_is_generic()
    {
        var intent = LibraryRemediationPlanner.BuildIntent("wrong_language", "russian only");
        var item = new api.Models.LibraryItem
        {
            MediaType = "Movie",
            Title = "Arrival",
            QualityProfile = "Any",
            AudioLanguagesJson = "[\"Russian\"]",
            SubtitleLanguagesJson = "[]"
        };

        var decided = LibraryRemediationProfileDecisioning.Apply(intent, item);

        Assert.False(decided.ShouldSearchNow);
        Assert.True(decided.NeedsManualReview);
        Assert.Equal("review_language_profile", decided.ProfileDecision);
        Assert.Contains("quality profile", decided.ProfileSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyProfilePolicy_requires_quality_profile_review_for_low_ceiling_quality_issue()
    {
        var intent = LibraryRemediationPlanner.BuildIntent("quality_issue", "looks soft");
        var item = new api.Models.LibraryItem
        {
            MediaType = "Movie",
            Title = "Heat",
            QualityProfile = "SD",
            AudioLanguagesJson = "[\"English\"]",
            SubtitleLanguagesJson = "[]"
        };

        var decided = LibraryRemediationProfileDecisioning.Apply(intent, item);

        Assert.False(decided.ShouldSearchNow);
        Assert.True(decided.NeedsManualReview);
        Assert.Equal("review_quality_profile", decided.ProfileDecision);
        Assert.Contains("caps upgrades", decided.ProfileSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyProfilePolicy_keeps_wrong_version_on_search_path()
    {
        var intent = LibraryRemediationPlanner.BuildIntent("wrong_version", "need theatrical");
        var item = new api.Models.LibraryItem
        {
            MediaType = "Movie",
            Title = "Blade Runner",
            QualityProfile = "Any",
            AudioLanguagesJson = "[\"English\"]",
            SubtitleLanguagesJson = "[\"English\"]"
        };

        var decided = LibraryRemediationProfileDecisioning.Apply(intent, item);

        Assert.True(decided.ShouldSearchNow);
        Assert.False(decided.NeedsManualReview);
        Assert.Equal("current_profile_ok", decided.ProfileDecision);
    }

    [Fact]
    public void CreateJob_marks_profile_blocked_attempt_as_blocked_profile_review()
    {
        var now = new DateTimeOffset(2026, 4, 9, 16, 30, 0, TimeSpan.Zero);
        var intent = LibraryRemediationPlanner.BuildIntent("wrong_language", "russian only") with
        {
            RequestedAction = "manual_review",
            ShouldSearchNow = false,
            NeedsManualReview = true,
            ProfileDecision = "review_language_profile",
            ProfileSummary = "Review the quality profile before asking Radarr to search again."
        };

        var result = LibraryRemediationExecution.BuildBlockedResult(41, "radarr", "Radarr", "MoviesSearch", 9001, "wrong_language", "russian only", intent.ProfileSummary, intent);
        var job = LibraryRemediationJobFactory.Create(41, 77, intent, result, null, "mark", now, null);

        Assert.Equal("BlockedProfileReview", job.Status);
        Assert.Equal("Blocked", job.SearchStatus);
        Assert.Equal("NotNeeded", job.BlacklistStatus);
    }

    [Fact]
    public void EvaluateLifecycle_marks_job_imported_when_release_changes_after_queue()
    {
        var requestedAt = new DateTimeOffset(2026, 4, 9, 15, 0, 0, TimeSpan.Zero);
        var job = new api.Models.LibraryRemediationJob
        {
            Status = "SearchQueued",
            SearchStatus = "Queued",
            BlacklistStatus = "Succeeded",
            ReleaseSummary = "Old.Release.1080p-GROUP",
            RequestedAtUtc = requestedAt
        };
        var item = new api.Models.LibraryItem
        {
            UpdatedAtUtc = requestedAt.AddHours(2),
            SourceUpdatedAtUtc = requestedAt.AddHours(2)
        };
        var latestContext = LibraryRemediationReleaseAwareness.BuildContext("radarr", 9001, "/movies/New.Release.4K-GROUP.mkv", "UHD", "Movie", []);

        var snapshot = LibraryRemediationLifecycleTracker.Evaluate(job, item, null, latestContext);

        Assert.Equal("ImportedReplacement", snapshot.Status);
        Assert.Contains("waiting", snapshot.OutcomeSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateLifecycle_marks_job_resolved_when_related_issue_is_closed()
    {
        var requestedAt = new DateTimeOffset(2026, 4, 9, 15, 0, 0, TimeSpan.Zero);
        var job = new api.Models.LibraryRemediationJob
        {
            Status = "SearchQueued",
            SearchStatus = "Queued",
            BlacklistStatus = "Skipped",
            RequestedAtUtc = requestedAt
        };
        var issue = new api.Models.LibraryIssue
        {
            Status = "Resolved",
            ResolvedAtUtc = requestedAt.AddHours(3)
        };

        var snapshot = LibraryRemediationLifecycleTracker.Evaluate(job, new api.Models.LibraryItem(), issue, null);

        Assert.Equal("Resolved", snapshot.Status);
        Assert.Equal("Completed", snapshot.SearchStatus);
        Assert.Contains("resolved", snapshot.OutcomeSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateLifecycle_auto_resolves_wrong_language_issue_when_new_release_has_english_audio()
    {
        var requestedAt = new DateTimeOffset(2026, 4, 9, 15, 0, 0, TimeSpan.Zero);
        var job = new api.Models.LibraryRemediationJob
        {
            Status = "SearchQueued",
            SearchStatus = "Queued",
            BlacklistStatus = "Succeeded",
            ReleaseSummary = "Old.Release.1080p-GROUP",
            RequestedAtUtc = requestedAt,
            IssueType = "wrong_language"
        };
        var item = new api.Models.LibraryItem
        {
            UpdatedAtUtc = requestedAt.AddHours(2),
            SourceUpdatedAtUtc = requestedAt.AddHours(2),
            AudioLanguagesJson = "[\"English\"]",
            SubtitleLanguagesJson = "[]"
        };
        var issue = new api.Models.LibraryIssue
        {
            Status = "Open",
            IssueType = "wrong_language"
        };
        var latestContext = LibraryRemediationReleaseAwareness.BuildContext("radarr", 9001, "/movies/New.Release.4K-GROUP.mkv", "UHD", "Movie", []);

        var snapshot = LibraryRemediationLifecycleTracker.Evaluate(job, item, issue, latestContext);

        Assert.Equal("Resolved", snapshot.Status);
        Assert.Equal("Completed", snapshot.SearchStatus);
        Assert.Contains("verification passed", snapshot.OutcomeSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateLifecycle_marks_verification_failed_when_issue_persists_after_imported_replacement()
    {
        var requestedAt = new DateTimeOffset(2026, 4, 9, 15, 0, 0, TimeSpan.Zero);
        var job = new api.Models.LibraryRemediationJob
        {
            Status = "SearchQueued",
            SearchStatus = "Queued",
            BlacklistStatus = "Succeeded",
            ReleaseSummary = "Old.Release.1080p-GROUP",
            RequestedAtUtc = requestedAt,
            IssueType = "wrong_language"
        };
        var item = new api.Models.LibraryItem
        {
            UpdatedAtUtc = requestedAt.AddHours(2),
            SourceUpdatedAtUtc = requestedAt.AddHours(2),
            AudioLanguagesJson = "[\"Russian\"]",
            SubtitleLanguagesJson = "[]"
        };
        var issue = new api.Models.LibraryIssue
        {
            Status = "Open",
            IssueType = "wrong_language"
        };
        var latestContext = LibraryRemediationReleaseAwareness.BuildContext("radarr", 9001, "/movies/New.Release.4K-GROUP.mkv", "UHD", "Movie", []);

        var snapshot = LibraryRemediationLifecycleTracker.Evaluate(job, item, issue, latestContext);

        Assert.Equal("VerificationFailed", snapshot.Status);
        Assert.Equal("Completed", snapshot.SearchStatus);
        Assert.Contains("still present", snapshot.OutcomeSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateLifecycle_keeps_imported_replacement_when_language_metadata_is_still_missing()
    {
        var requestedAt = new DateTimeOffset(2026, 4, 9, 15, 0, 0, TimeSpan.Zero);
        var job = new api.Models.LibraryRemediationJob
        {
            Status = "SearchQueued",
            SearchStatus = "Queued",
            BlacklistStatus = "Succeeded",
            ReleaseSummary = "Old.Release.1080p-GROUP",
            RequestedAtUtc = requestedAt,
            IssueType = "wrong_language"
        };
        var item = new api.Models.LibraryItem
        {
            UpdatedAtUtc = requestedAt.AddHours(2),
            SourceUpdatedAtUtc = requestedAt.AddHours(2),
            AudioLanguagesJson = "[]",
            SubtitleLanguagesJson = "[]"
        };
        var issue = new api.Models.LibraryIssue
        {
            Status = "Open",
            IssueType = "wrong_language"
        };
        var latestContext = LibraryRemediationReleaseAwareness.BuildContext("radarr", 9001, "/movies/New.Release.4K-GROUP.mkv", "UHD", "Movie", []);

        var snapshot = LibraryRemediationLifecycleTracker.Evaluate(job, item, issue, latestContext);

        Assert.Equal("ImportedReplacement", snapshot.Status);
        Assert.Contains("waiting", snapshot.OutcomeSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateLifecycle_waits_for_fresh_playability_probe_before_resolving_corrupt_file_issue()
    {
        var requestedAt = new DateTimeOffset(2026, 4, 9, 15, 0, 0, TimeSpan.Zero);
        var job = new api.Models.LibraryRemediationJob
        {
            Status = "SearchQueued",
            SearchStatus = "Queued",
            BlacklistStatus = "Succeeded",
            ReleaseSummary = "Old.Release.1080p-GROUP",
            RequestedAtUtc = requestedAt,
            IssueType = "corrupt_file"
        };
        var item = new api.Models.LibraryItem
        {
            UpdatedAtUtc = requestedAt.AddHours(2),
            SourceUpdatedAtUtc = requestedAt.AddHours(2),
            PlayabilityScore = "good",
            PlayabilityCheckedAtUtc = requestedAt.AddMinutes(-10)
        };
        var issue = new api.Models.LibraryIssue
        {
            Status = "Open",
            IssueType = "corrupt_file"
        };
        var latestContext = LibraryRemediationReleaseAwareness.BuildContext("radarr", 9001, "/movies/New.Release.4K-GROUP.mkv", "UHD", "Movie", []);

        var snapshot = LibraryRemediationLifecycleTracker.Evaluate(job, item, issue, latestContext);

        Assert.Equal("ImportedReplacement", snapshot.Status);
        Assert.Contains("waiting", snapshot.OutcomeSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildContext_falls_back_to_file_name_when_history_missing()
    {
        var context = LibraryRemediationReleaseAwareness.BuildContext(
            serviceKey: "radarr",
            externalItemId: 9001,
            filePath: "/data/movies/The Movie (2026)/The Movie (2026).mkv",
            qualityProfile: "HD-1080p",
            sourceTitle: "The Movie",
            historyCandidates: []);

        Assert.Contains("The Movie (2026).mkv", context.ReleaseSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("HD-1080p", context.ReleaseSummary, StringComparison.OrdinalIgnoreCase);
        Assert.False(context.HasHistoryMatch);
    }

    [Fact]
    public void PlanSearchReplacement_prefers_radarr_link_for_movies()
    {
        var plan = LibraryRemediationPlanner.PlanSearchReplacement(
            mediaType: "Movie",
            sourceLinks:
            [
                new LibraryRemediationSourceLink("plex", 10, "999", "movie", false),
                new LibraryRemediationSourceLink("radarr", 22, "123", "movie", false)
            ]);

        Assert.Equal("radarr", plan.ServiceKey);
        Assert.Equal(22, plan.IntegrationId);
        Assert.Equal("MoviesSearch", plan.CommandName);
        Assert.Equal(123, plan.ExternalItemId);
        Assert.False(plan.RequiresRemoteLookup);
    }

    [Fact]
    public void PlanSearchReplacement_falls_back_to_radarr_for_movies_without_link()
    {
        var plan = LibraryRemediationPlanner.PlanSearchReplacement(
            mediaType: "Movie",
            sourceLinks:
            [
                new LibraryRemediationSourceLink("plex", 10, "999", "movie", false)
            ]);

        Assert.Equal("radarr", plan.ServiceKey);
        Assert.Equal("MoviesSearch", plan.CommandName);
        Assert.Null(plan.ExternalItemId);
        Assert.True(plan.RequiresRemoteLookup);
    }

    [Fact]
    public void PlanSearchReplacement_prefers_episode_level_sonarr_link_for_episodes()
    {
        var plan = LibraryRemediationPlanner.PlanSearchReplacement(
            mediaType: "Episode",
            sourceLinks:
            [
                new LibraryRemediationSourceLink("sonarr", 44, "21", "series", false),
                new LibraryRemediationSourceLink("sonarr", 44, "1429", "episode", false)
            ]);

        Assert.Equal("sonarr", plan.ServiceKey);
        Assert.Equal(44, plan.IntegrationId);
        Assert.Equal("EpisodeSearch", plan.CommandName);
        Assert.Equal(1429, plan.ExternalItemId);
        Assert.False(plan.RequiresRemoteLookup);
    }

    [Fact]
    public void PlanSearchReplacement_uses_series_search_for_series_items()
    {
        var plan = LibraryRemediationPlanner.PlanSearchReplacement(
            mediaType: "Series",
            sourceLinks:
            [
                new LibraryRemediationSourceLink("sonarr", 44, "21", "series", false)
            ]);

        Assert.Equal("sonarr", plan.ServiceKey);
        Assert.Equal("SeriesSearch", plan.CommandName);
        Assert.Equal(21, plan.ExternalItemId);
    }

    [Fact]
    public void PlanSearchReplacement_returns_unknown_for_unsupported_media_type()
    {
        var plan = LibraryRemediationPlanner.PlanSearchReplacement(
            mediaType: "Photo",
            sourceLinks: []);

        Assert.False(plan.IsSupported);
        Assert.Contains("Unsupported", plan.Message, StringComparison.OrdinalIgnoreCase);
    }
}
