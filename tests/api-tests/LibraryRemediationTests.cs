using System.Net;
using System.Net.Http;
using api;
using api.Data;
using api.Models;
using Microsoft.EntityFrameworkCore;
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
    public void BuildIntent_maps_subtitle_unusable_to_subtitle_workflow_without_approval_requirement()
    {
        var intent = LibraryRemediationPlanner.BuildIntent("subtitle_unusable", "subs are burned in and wrong");

        Assert.Equal("bazarr_subtitles", intent.RequestedAction);
        Assert.False(intent.ShouldSearchNow);
        Assert.False(intent.ApprovalRequired);
        Assert.Contains("subtitle", intent.PolicySummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildIntent_maps_audio_wrong_during_playback_to_safe_audio_remediation_with_approval()
    {
        var intent = LibraryRemediationPlanner.BuildIntent("audio_wrong_during_playback", "commentary track keeps defaulting");

        Assert.Equal("safe_audio_remediation", intent.RequestedAction);
        Assert.False(intent.ShouldSearchNow);
        Assert.True(intent.ApprovalRequired);
        Assert.Contains("approval", intent.ApprovalReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildIntent_keeps_device_specific_issue_in_manual_review()
    {
        var intent = LibraryRemediationPlanner.BuildIntent("device_specific_issue", "only fails on bedroom roku");

        Assert.Equal("manual_review", intent.RequestedAction);
        Assert.True(intent.NeedsManualReview);
        Assert.False(intent.ShouldSearchNow);
        Assert.False(intent.ApprovalRequired);
    }

    [Fact]
    public void CreateJob_captures_policy_and_successful_queue_result()
    {
        var now = new DateTimeOffset(2026, 4, 9, 15, 0, 0, TimeSpan.Zero);
        var intent = LibraryRemediationPlanner.BuildIntent("corrupt_file", "crc errors");
        var result = new LibraryItemRemediationResponse(41, true, "radarr", "Radarr", "MoviesSearch", 9001, false, "corrupt_file", "crc errors", "Queued Radarr replacement search for movie ID 9001.", null, string.Empty, 314, "Queued", "Provider MoviesSearch command 314 is queued.");

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
        Assert.Equal(314, job.ProviderCommandId);
        Assert.Equal("Queued", job.ProviderCommandStatus);
        Assert.Contains("314", job.ProviderCommandSummary, StringComparison.Ordinal);
        Assert.Contains("Movie.mkv", job.ReleaseSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Null(job.FinishedAtUtc);
    }

    [Fact]
    public void BuildQueuedResult_parses_provider_command_id_and_state()
    {
        var result = LibraryRemediationProviderCommandTracker.BuildQueuedResult(
            success: true,
            message: "Queued Radarr replacement search for movie ID 9001.",
            responseBody: "{\"id\":314,\"name\":\"MoviesSearch\",\"status\":\"queued\"}");

        Assert.True(result.Success);
        Assert.Equal(314, result.ProviderCommandId);
        Assert.Equal("Queued", result.ProviderCommandStatus);
        Assert.Contains("MoviesSearch", result.ProviderCommandSummary, StringComparison.Ordinal);
        Assert.Contains("314", result.ProviderCommandSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateJob_marks_no_results_search_as_no_replacement_found()
    {
        var now = new DateTimeOffset(2026, 4, 9, 15, 4, 0, TimeSpan.Zero);
        var intent = LibraryRemediationPlanner.BuildIntent("corrupt_file", "bad file");
        var result = new LibraryItemRemediationResponse(
            41,
            true,
            "sonarr",
            "Sonarr",
            "EpisodeSearch",
            1155,
            false,
            "corrupt_file",
            "bad file",
            "Episode search completed. 0 reports downloaded.",
            null,
            "NoResults");

        var job = LibraryRemediationJobFactory.Create(
            libraryItemId: 41,
            libraryIssueId: 88,
            intent,
            result,
            releaseContext: null,
            requestedBy: "mark",
            requestedAtUtc: now,
            blacklistSucceeded: true);

        Assert.Equal("NoReplacementFound", job.Status);
        Assert.Equal("NoResults", job.SearchStatus);
        Assert.Equal("Succeeded", job.BlacklistStatus);
        Assert.Contains("no downloadable results", job.OutcomeSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("NoMatch", job.VerificationStatus);
        Assert.Contains("no replacement", job.VerificationSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Recommended", job.LoopbackStatus);
        Assert.Contains("manual review", job.LoopbackSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(now, job.FinishedAtUtc);
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
        Assert.Equal("client_limitation", decided.PolicyState);
        Assert.Contains("client/profile tuning", decided.PolicySummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("client", decided.NextActionSummary, StringComparison.OrdinalIgnoreCase);
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
        Assert.Equal("bad_media_file", decided.PolicyState);
        Assert.Contains("blacklist", decided.NextActionSummary, StringComparison.OrdinalIgnoreCase);
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
        Assert.Equal("profile_policy_block", decided.PolicyState);
        Assert.Contains("quality profile", decided.ProfileSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("review the acquisition profile", decided.NextActionSummary, StringComparison.OrdinalIgnoreCase);
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
    public void BuildRecommendation_prefers_open_issue_context_and_recommends_replacement_for_bad_media_file()
    {
        var item = new api.Models.LibraryItem
        {
            Id = 41,
            MediaType = "Movie",
            Title = "Blade Runner",
            QualityProfile = "Any",
            AudioLanguagesJson = "[\"English\"]",
            SubtitleLanguagesJson = "[\"English\"]"
        };
        var issue = new api.Models.LibraryIssue
        {
            LibraryItemId = 41,
            IssueType = "playback_failure",
            Status = "Open",
            Summary = "Playback keeps failing."
        };
        var diagnostic = new api.Models.PlaybackDiagnosticEntry
        {
            LibraryItemId = 41,
            HealthLabel = "Problematic",
            SuspectedCause = "File appears corrupt during playback.",
            ErrorMessage = "Input/output error while reading media file",
            Summary = "Playback failed because the file could not be read."
        };

        var recommendation = LibraryRemediationRecommendationEngine.Build(item, issue, diagnostic);

        Assert.True(recommendation.HasRecommendation);
        Assert.Equal("playback_failure", recommendation.IssueType);
        Assert.Equal("bad_media_file", recommendation.PolicyState);
        Assert.True(recommendation.ShouldSearchNow);
        Assert.True(recommendation.ShouldBlacklistCurrentRelease);
    }

    [Fact]
    public void BuildRecommendation_falls_back_to_playback_diagnostic_when_issue_context_is_missing()
    {
        var item = new api.Models.LibraryItem
        {
            Id = 41,
            MediaType = "Episode",
            Title = "The Curse of Oak Island — S13E21 — A Sacred Symbol",
            QualityProfile = "Any",
            AudioLanguagesJson = "[\"English\"]",
            SubtitleLanguagesJson = "[]"
        };
        var diagnostic = new api.Models.PlaybackDiagnosticEntry
        {
            LibraryItemId = 41,
            HealthLabel = "Problematic",
            SuspectedCause = "Client limitation: PGS subtitles force transcode on this TV app.",
            Summary = "Playback degraded because of subtitle burn-in.",
            SubtitleDecision = "transcode"
        };

        var recommendation = LibraryRemediationRecommendationEngine.Build(item, null, diagnostic);

        Assert.True(recommendation.HasRecommendation);
        Assert.Equal("playback_failure", recommendation.IssueType);
        Assert.Equal("client_limitation", recommendation.PolicyState);
        Assert.False(recommendation.ShouldSearchNow);
        Assert.Contains("client", recommendation.NextActionSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildRecommendation_returns_no_action_when_issue_and_diagnostic_are_missing()
    {
        var item = new api.Models.LibraryItem
        {
            Id = 41,
            MediaType = "Movie",
            Title = "Blade Runner",
            QualityProfile = "Any"
        };

        var recommendation = LibraryRemediationRecommendationEngine.Build(item, null, null);

        Assert.False(recommendation.HasRecommendation);
        Assert.Equal("insufficient_evidence", recommendation.PolicyState);
        Assert.False(recommendation.ShouldSearchNow);
    }

    [Fact]
    public async Task UpsertFromPlaybackDiagnosticsAsync_creates_new_open_issue_for_bad_media_failure()
    {
        await using var db = CreateDb();
        var detectedAt = new DateTimeOffset(2026, 4, 9, 18, 0, 0, TimeSpan.Zero);
        var item = new api.Models.LibraryItem
        {
            CanonicalKey = "movie:blade-runner",
            MediaType = "Movie",
            Title = "Blade Runner",
            SortTitle = "Blade Runner",
            ImdbId = "tt0083658",
            PlexRatingKey = "123",
            Description = string.Empty,
            DescriptionSourceService = string.Empty,
            AudioLanguagesJson = "[]",
            SubtitleLanguagesJson = "[]",
            PlayabilityScore = string.Empty,
            PlayabilitySummary = string.Empty,
            PlayabilityDetailsJson = "{}",
            QualityProfile = "Any"
        };
        db.LibraryItems.Add(item);
        await db.SaveChangesAsync();

        var diagnostic = new api.Models.PlaybackDiagnosticEntry
        {
            LibraryItemId = item.Id,
            HealthLabel = "Error",
            SuspectedCause = "File appears corrupt during playback.",
            ErrorMessage = "Input/output error while reading media file",
            Summary = "Playback failed because the file could not be read.",
            OccurredAtUtc = detectedAt
        };

        await PlaybackDiagnosticIssueAutomation.UpsertFromPlaybackDiagnosticsAsync(db, item, diagnostic, detectedAt);
        await db.SaveChangesAsync();

        var issue = await db.LibraryIssues.SingleAsync();
        Assert.Equal("corrupt_file", issue.IssueType);
        Assert.Equal("Open", issue.Status);
        Assert.Equal("playback-diagnostics-v1", issue.PolicyVersion);
    }

    [Fact]
    public async Task UpsertFromPlaybackDiagnosticsAsync_reuses_existing_issue_instead_of_creating_duplicate()
    {
        await using var db = CreateDb();
        var detectedAt = new DateTimeOffset(2026, 4, 9, 18, 0, 0, TimeSpan.Zero);
        var item = new api.Models.LibraryItem
        {
            CanonicalKey = "movie:blade-runner-2",
            MediaType = "Movie",
            Title = "Blade Runner",
            SortTitle = "Blade Runner",
            ImdbId = "tt0083658",
            PlexRatingKey = "1234",
            Description = string.Empty,
            DescriptionSourceService = string.Empty,
            AudioLanguagesJson = "[]",
            SubtitleLanguagesJson = "[]",
            PlayabilityScore = string.Empty,
            PlayabilitySummary = string.Empty,
            PlayabilityDetailsJson = "{}",
            QualityProfile = "Any"
        };
        db.LibraryItems.Add(item);
        await db.SaveChangesAsync();

        db.LibraryIssues.Add(new api.Models.LibraryIssue
        {
            LibraryItemId = item.Id,
            IssueType = "playback_failure",
            Status = "Open",
            PolicyVersion = "playback-diagnostics-v1",
            Summary = "Old",
            SuggestedAction = "Old",
            DetailsJson = "{}",
            Severity = "Warning",
            FirstDetectedAtUtc = detectedAt.AddHours(-2),
            LastDetectedAtUtc = detectedAt.AddHours(-2)
        });
        await db.SaveChangesAsync();

        var diagnostic = new api.Models.PlaybackDiagnosticEntry
        {
            LibraryItemId = item.Id,
            HealthLabel = "Investigate",
            SuspectedCause = "Client limitation: PGS subtitles force transcode on this TV app.",
            Summary = "Playback degraded because of subtitle burn-in.",
            SubtitleDecision = "transcode",
            OccurredAtUtc = detectedAt
        };

        await PlaybackDiagnosticIssueAutomation.UpsertFromPlaybackDiagnosticsAsync(db, item, diagnostic, detectedAt);
        await db.SaveChangesAsync();

        Assert.Equal(1, await db.LibraryIssues.CountAsync());
        var issue = await db.LibraryIssues.SingleAsync();
        Assert.Equal("playback_failure", issue.IssueType);
        Assert.Equal(detectedAt, issue.LastDetectedAtUtc);
    }

    [Fact]
    public async Task UpsertFromPlaybackDiagnosticsAsync_reopens_resolved_issue_when_failure_returns()
    {
        await using var db = CreateDb();
        var detectedAt = new DateTimeOffset(2026, 4, 9, 18, 0, 0, TimeSpan.Zero);
        var item = new api.Models.LibraryItem
        {
            CanonicalKey = "movie:blade-runner-3",
            MediaType = "Movie",
            Title = "Blade Runner",
            SortTitle = "Blade Runner",
            ImdbId = "tt0083658",
            PlexRatingKey = "12345",
            Description = string.Empty,
            DescriptionSourceService = string.Empty,
            AudioLanguagesJson = "[]",
            SubtitleLanguagesJson = "[]",
            PlayabilityScore = string.Empty,
            PlayabilitySummary = string.Empty,
            PlayabilityDetailsJson = "{}",
            QualityProfile = "Any"
        };
        db.LibraryItems.Add(item);
        await db.SaveChangesAsync();

        db.LibraryIssues.Add(new api.Models.LibraryIssue
        {
            LibraryItemId = item.Id,
            IssueType = "corrupt_file",
            Status = "Resolved",
            PolicyVersion = "playback-diagnostics-v1",
            Summary = "Old",
            SuggestedAction = "Old",
            DetailsJson = "{}",
            Severity = "High",
            FirstDetectedAtUtc = detectedAt.AddHours(-4),
            LastDetectedAtUtc = detectedAt.AddHours(-4),
            ResolvedAtUtc = detectedAt.AddHours(-1)
        });
        await db.SaveChangesAsync();

        var diagnostic = new api.Models.PlaybackDiagnosticEntry
        {
            LibraryItemId = item.Id,
            HealthLabel = "Error",
            SuspectedCause = "File appears corrupt during playback.",
            ErrorMessage = "CRC error",
            Summary = "Playback failed.",
            OccurredAtUtc = detectedAt
        };

        await PlaybackDiagnosticIssueAutomation.UpsertFromPlaybackDiagnosticsAsync(db, item, diagnostic, detectedAt);
        await db.SaveChangesAsync();

        var issue = await db.LibraryIssues.SingleAsync();
        Assert.Equal("Open", issue.Status);
        Assert.Null(issue.ResolvedAtUtc);
    }

    [Fact]
    public async Task UpsertFromPlaybackDiagnosticsAsync_resolves_auto_managed_issue_when_latest_diagnostic_is_healthy()
    {
        await using var db = CreateDb();
        var detectedAt = new DateTimeOffset(2026, 4, 9, 18, 0, 0, TimeSpan.Zero);
        var item = new api.Models.LibraryItem
        {
            CanonicalKey = "movie:blade-runner-4",
            MediaType = "Movie",
            Title = "Blade Runner",
            SortTitle = "Blade Runner",
            ImdbId = "tt0083658",
            PlexRatingKey = "123456",
            Description = string.Empty,
            DescriptionSourceService = string.Empty,
            AudioLanguagesJson = "[]",
            SubtitleLanguagesJson = "[]",
            PlayabilityScore = string.Empty,
            PlayabilitySummary = string.Empty,
            PlayabilityDetailsJson = "{}",
            QualityProfile = "Any"
        };
        db.LibraryItems.Add(item);
        await db.SaveChangesAsync();

        db.LibraryIssues.Add(new api.Models.LibraryIssue
        {
            LibraryItemId = item.Id,
            IssueType = "playback_failure",
            Status = "Open",
            PolicyVersion = "playback-diagnostics-v1",
            Summary = "Old",
            SuggestedAction = "Old",
            DetailsJson = "{}",
            Severity = "Warning",
            FirstDetectedAtUtc = detectedAt.AddHours(-2),
            LastDetectedAtUtc = detectedAt.AddHours(-2)
        });
        await db.SaveChangesAsync();

        var diagnostic = new api.Models.PlaybackDiagnosticEntry
        {
            LibraryItemId = item.Id,
            HealthLabel = "Healthy",
            Summary = "Direct play succeeded.",
            OccurredAtUtc = detectedAt
        };

        await PlaybackDiagnosticIssueAutomation.UpsertFromPlaybackDiagnosticsAsync(db, item, diagnostic, detectedAt);
        await db.SaveChangesAsync();

        var issue = await db.LibraryIssues.SingleAsync();
        Assert.Equal("Resolved", issue.Status);
        Assert.Equal(detectedAt, issue.ResolvedAtUtc);
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
    public void BuildArrSearchProgressSnapshot_detects_downloading_from_queue_payload()
    {
        var requestedAt = new DateTimeOffset(2026, 4, 27, 20, 9, 33, TimeSpan.Zero);
        var queueJson = "{\"records\":[{\"title\":\"28 Years Later\",\"status\":\"downloading\",\"trackedDownloadState\":\"downloading\",\"protocol\":\"torrent\"}]}";
        var historyJson = "{\"records\":[{\"eventType\":\"grabbed\",\"date\":\"2026-04-27T20:10:00Z\",\"sourceTitle\":\"28.Years.Later.2025.2160p\"}]}";

        var snapshot = LibraryRemediationProviderCommandTracker.BuildArrSearchProgressSnapshot("radarr", 9001, queueJson, historyJson, requestedAt);

        Assert.NotNull(snapshot);
        Assert.Equal("Downloading", snapshot!.SearchStatus);
        Assert.Equal("Processing", snapshot.Status);
        Assert.Contains("downloading", snapshot.OutcomeSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("via Torrent", snapshot.OutcomeSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildArrSearchProgressSnapshot_detects_importing_from_queue_payload()
    {
        var requestedAt = new DateTimeOffset(2026, 4, 27, 20, 9, 33, TimeSpan.Zero);
        var queueJson = "{\"records\":[{\"title\":\"28 Years Later\",\"status\":\"completed\",\"trackedDownloadState\":\"importPending\"}]}";
        var historyJson = "{\"records\":[{\"eventType\":\"grabbed\",\"date\":\"2026-04-27T20:10:00Z\",\"sourceTitle\":\"28.Years.Later.2025.2160p\"}]}";

        var snapshot = LibraryRemediationProviderCommandTracker.BuildArrSearchProgressSnapshot("radarr", 9001, queueJson, historyJson, requestedAt);

        Assert.NotNull(snapshot);
        Assert.Equal("Importing", snapshot!.SearchStatus);
        Assert.Equal("Processing", snapshot.Status);
        Assert.Contains("import", snapshot.OutcomeSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildArrSearchProgressSnapshot_ignores_queue_records_for_other_movies()
    {
        var requestedAt = new DateTimeOffset(2026, 4, 27, 20, 9, 33, TimeSpan.Zero);
        var queueJson = "{\"records\":[{\"title\":\"Dave.1993.1080p.BluRay\",\"status\":\"downloading\",\"trackedDownloadState\":\"downloading\",\"movieId\":428}]}";
        var historyJson = "{\"records\":[{\"eventType\":\"grabbed\",\"date\":\"2026-04-27T20:10:00Z\",\"sourceTitle\":\"Dave.1993.1080p.BluRay\",\"movieId\":428}]}";

        var snapshot = LibraryRemediationProviderCommandTracker.BuildArrSearchProgressSnapshot("radarr", 9001, queueJson, historyJson, requestedAt);

        Assert.Null(snapshot);
    }

    [Fact]
    public void BuildArrSearchProgressSnapshot_detects_imported_from_history_payload()
    {
        var requestedAt = new DateTimeOffset(2026, 4, 27, 20, 9, 33, TimeSpan.Zero);
        var queueJson = "{\"records\":[]}";
        var historyJson = "{\"records\":[{\"eventType\":\"downloadFolderImported\",\"date\":\"2026-04-27T20:20:00Z\",\"sourceTitle\":\"28.Years.Later.2025.2160p\"},{\"eventType\":\"grabbed\",\"date\":\"2026-04-27T20:10:00Z\",\"sourceTitle\":\"28.Years.Later.2025.2160p\"}]}";

        var snapshot = LibraryRemediationProviderCommandTracker.BuildArrSearchProgressSnapshot("radarr", 9001, queueJson, historyJson, requestedAt);

        Assert.NotNull(snapshot);
        Assert.Equal("Imported", snapshot!.SearchStatus);
        Assert.Equal("Processing", snapshot.Status);
        Assert.Contains("imported", snapshot.OutcomeSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateLifecycle_keeps_imported_status_visible_while_waiting_for_metadata_refresh()
    {
        var requestedAt = new DateTimeOffset(2026, 4, 27, 20, 9, 33, TimeSpan.Zero);
        var job = new api.Models.LibraryRemediationJob
        {
            Status = "Processing",
            SearchStatus = "Imported",
            BlacklistStatus = "Succeeded",
            OutcomeSummary = "Radarr imported a replacement. MediaCloud is waiting for source refresh and verification.",
            RequestedAtUtc = requestedAt,
            IssueType = "wrong_language"
        };

        var snapshot = LibraryRemediationLifecycleTracker.Evaluate(job, new api.Models.LibraryItem(), new api.Models.LibraryIssue { Status = "Open", IssueType = "wrong_language" }, null);

        Assert.Equal("Processing", snapshot.Status);
        Assert.Equal("Imported", snapshot.SearchStatus);
        Assert.Contains("imported", snapshot.OutcomeSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("WaitingForEvidence", snapshot.VerificationStatus);
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
    public void RefreshLiveState_updates_job_when_current_release_verification_passes()
    {
        var requestedAt = new DateTimeOffset(2026, 4, 9, 15, 0, 0, TimeSpan.Zero);
        var checkedAt = requestedAt.AddHours(4);
        var job = new api.Models.LibraryRemediationJob
        {
            Status = "SearchQueued",
            SearchStatus = "Queued",
            BlacklistStatus = "Succeeded",
            ReleaseSummary = "Old.Release.1080p-GROUP · HD-1080p",
            RequestedAtUtc = requestedAt,
            IssueType = "wrong_language"
        };
        var item = new api.Models.LibraryItem
        {
            UpdatedAtUtc = requestedAt.AddHours(2),
            SourceUpdatedAtUtc = requestedAt.AddHours(2),
            AudioLanguagesJson = "[\"English\"]",
            SubtitleLanguagesJson = "[]",
            PrimaryFilePath = "/movies/New.Release.4K-GROUP.mkv",
            QualityProfile = "UHD"
        };
        var issue = new api.Models.LibraryIssue
        {
            Status = "Open",
            IssueType = "wrong_language"
        };

        var changed = LibraryRemediationJobLiveState.Refresh(job, item, issue, "New.Release.4K-GROUP", checkedAt);

        Assert.True(changed);
        Assert.Equal("Resolved", job.Status);
        Assert.Equal("Completed", job.SearchStatus);
        Assert.Equal("Verified", job.VerificationStatus);
        Assert.Contains("english audio", job.VerificationSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("NotNeeded", job.LoopbackStatus);
        Assert.Equal("Verification passed; no repeat remediation is recommended.", job.LoopbackSummary);
        Assert.Equal("Resolved", issue.Status);
        Assert.Equal(checkedAt, issue.ResolvedAtUtc);
        Assert.Equal(checkedAt, job.LastCheckedAtUtc);
        Assert.Equal(checkedAt, job.VerificationCheckedAtUtc);
        Assert.Equal(checkedAt, job.FinishedAtUtc);
        Assert.Contains("verification passed", job.OutcomeSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RefreshLiveState_keeps_existing_terminal_finished_time()
    {
        var requestedAt = new DateTimeOffset(2026, 4, 9, 15, 0, 0, TimeSpan.Zero);
        var existingFinishedAt = requestedAt.AddHours(5);
        var checkedAt = requestedAt.AddHours(6);
        var job = new api.Models.LibraryRemediationJob
        {
            Status = "Resolved",
            SearchStatus = "Completed",
            BlacklistStatus = "Succeeded",
            ReleaseSummary = "New.Release.4K-GROUP · UHD",
            RequestedAtUtc = requestedAt,
            FinishedAtUtc = existingFinishedAt,
            IssueType = "wrong_language"
        };
        var item = new api.Models.LibraryItem
        {
            UpdatedAtUtc = requestedAt.AddHours(2),
            SourceUpdatedAtUtc = requestedAt.AddHours(2),
            AudioLanguagesJson = "[\"English\"]",
            SubtitleLanguagesJson = "[]",
            PrimaryFilePath = "/movies/New.Release.4K-GROUP.mkv",
            QualityProfile = "UHD"
        };
        var issue = new api.Models.LibraryIssue
        {
            Status = "Resolved",
            ResolvedAtUtc = requestedAt.AddHours(3),
            IssueType = "wrong_language"
        };

        var changed = LibraryRemediationJobLiveState.Refresh(job, item, issue, "New.Release.4K-GROUP", checkedAt);

        Assert.True(changed);
        Assert.Equal(existingFinishedAt, job.FinishedAtUtc);
        Assert.Equal("Verified", job.VerificationStatus);
        Assert.Equal("NotNeeded", job.LoopbackStatus);
        Assert.Equal(checkedAt, job.LastCheckedAtUtc);
        Assert.Equal(checkedAt, job.VerificationCheckedAtUtc);
    }

    [Fact]
    public void RefreshLiveState_records_failed_verification_and_recommends_loopback_when_issue_persists()
    {
        var requestedAt = new DateTimeOffset(2026, 4, 9, 15, 0, 0, TimeSpan.Zero);
        var checkedAt = requestedAt.AddHours(4);
        var job = new api.Models.LibraryRemediationJob
        {
            Status = "SearchQueued",
            SearchStatus = "Queued",
            BlacklistStatus = "Succeeded",
            ReleaseSummary = "Old.Release.1080p-GROUP · HD-1080p",
            RequestedAtUtc = requestedAt,
            IssueType = "wrong_language"
        };
        var item = new api.Models.LibraryItem
        {
            UpdatedAtUtc = requestedAt.AddHours(2),
            SourceUpdatedAtUtc = requestedAt.AddHours(2),
            AudioLanguagesJson = "[\"Russian\"]",
            SubtitleLanguagesJson = "[]",
            PrimaryFilePath = "/movies/New.Release.4K-GROUP.mkv",
            QualityProfile = "UHD"
        };
        var issue = new api.Models.LibraryIssue
        {
            Status = "Open",
            IssueType = "wrong_language"
        };

        var changed = LibraryRemediationJobLiveState.Refresh(job, item, issue, "New.Release.4K-GROUP", checkedAt);

        Assert.True(changed);
        Assert.Equal("VerificationFailed", job.Status);
        Assert.Equal("Failed", job.VerificationStatus);
        Assert.Contains("still present", job.VerificationSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Recommended", job.LoopbackStatus);
        Assert.Contains("repeat remediation", job.LoopbackSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Open", issue.Status);
        Assert.Null(issue.ResolvedAtUtc);
        Assert.Equal(checkedAt, job.VerificationCheckedAtUtc);
        Assert.Equal(checkedAt, job.FinishedAtUtc);
    }

    [Fact]
    public void BuildFollowUpPlan_recommends_repeat_search_for_failed_replacement_verification()
    {
        var job = new api.Models.LibraryRemediationJob
        {
            Id = 42,
            LibraryItemId = 1474,
            RequestedAction = "search_replacement",
            IssueType = "wrong_language",
            Reason = "wrong_language",
            Notes = "replacement kept Russian audio",
            VerificationStatus = "Failed",
            VerificationSummary = "Replacement imported, but the language issue is still present after verification.",
            LoopbackStatus = "Recommended",
            LoopbackSummary = "Consider repeat remediation with a new replacement search or escalate to manual review if the same evidence keeps returning.",
            RequestedAtUtc = new DateTimeOffset(2026, 4, 16, 0, 0, 0, TimeSpan.Zero)
        };

        var plan = LibraryRemediationFollowUpPlanner.Build(job, []);

        Assert.True(plan.CanRepeatSearch);
        Assert.Equal("repeat_search_replacement", plan.ActionKey);
        Assert.Equal("wrong_language", plan.IssueType);
        Assert.Equal("wrong_language", plan.Reason);
        Assert.Contains("verification failed", plan.OperatorSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("language issue is still present", plan.RepeatNotes, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, plan.RetryAttemptCount);
    }

    [Fact]
    public void BuildFollowUpPlan_recommends_review_before_retry_when_no_replacement_was_found()
    {
        var job = new api.Models.LibraryRemediationJob
        {
            Id = 43,
            LibraryItemId = 1474,
            RequestedAction = "search_replacement",
            IssueType = "runtime_mismatch",
            Reason = "runtime_mismatch",
            Notes = "wrong cut suspected",
            VerificationStatus = "NoMatch",
            VerificationSummary = "No replacement was found, so verification cannot continue on a new release yet.",
            LoopbackStatus = "Recommended",
            LoopbackSummary = "No replacement was found; use profile tuning or manual review before repeating remediation.",
            RequestedAtUtc = new DateTimeOffset(2026, 4, 16, 0, 0, 0, TimeSpan.Zero)
        };

        var plan = LibraryRemediationFollowUpPlanner.Build(job, []);

        Assert.True(plan.CanRepeatSearch);
        Assert.Equal("review_then_retry_search", plan.ActionKey);
        Assert.Contains("review profiles", plan.OperatorSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No replacement was found", plan.RepeatNotes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildFollowUpPlan_returns_no_follow_up_when_loopback_is_not_recommended()
    {
        var job = new api.Models.LibraryRemediationJob
        {
            RequestedAction = "search_replacement",
            IssueType = "wrong_language",
            Reason = "wrong_language",
            VerificationStatus = "WaitingForEvidence",
            LoopbackStatus = "Standby",
            LoopbackSummary = "Wait for fresher metadata or probes before repeating remediation.",
            RequestedAtUtc = new DateTimeOffset(2026, 4, 16, 0, 0, 0, TimeSpan.Zero)
        };

        var plan = LibraryRemediationFollowUpPlanner.Build(job, []);

        Assert.False(plan.CanRepeatSearch);
        Assert.Equal("none", plan.ActionKey);
        Assert.Contains("wait", plan.OperatorSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildFollowUpPlan_escalates_to_manual_review_after_max_repeat_attempts()
    {
        var original = new api.Models.LibraryRemediationJob
        {
            Id = 80,
            LibraryItemId = 1474,
            RequestedAction = "search_replacement",
            IssueType = "wrong_language",
            Reason = "wrong_language",
            VerificationStatus = "Failed",
            VerificationSummary = "Replacement imported, but the language issue is still present after verification.",
            LoopbackStatus = "Recommended",
            LoopbackSummary = "Consider repeat remediation with a new replacement search or escalate to manual review if the same evidence keeps returning.",
            RequestedAtUtc = new DateTimeOffset(2026, 4, 16, 0, 0, 0, TimeSpan.Zero)
        };
        var retryOne = new api.Models.LibraryRemediationJob
        {
            Id = 81,
            LibraryItemId = 1474,
            RequestedAction = "search_replacement",
            IssueType = "wrong_language",
            Reason = "wrong_language",
            Status = "VerificationFailed",
            RequestedAtUtc = new DateTimeOffset(2026, 4, 16, 1, 0, 0, TimeSpan.Zero)
        };
        var retryTwo = new api.Models.LibraryRemediationJob
        {
            Id = 82,
            LibraryItemId = 1474,
            RequestedAction = "search_replacement",
            IssueType = "wrong_language",
            Reason = "wrong_language",
            Status = "VerificationFailed",
            RequestedAtUtc = new DateTimeOffset(2026, 4, 16, 2, 0, 0, TimeSpan.Zero)
        };

        var plan = LibraryRemediationFollowUpPlanner.Build(original, [retryOne, retryTwo]);

        Assert.False(plan.CanRepeatSearch);
        Assert.True(plan.ForceManualReview);
        Assert.Equal("manual_review", plan.ActionKey);
        Assert.Equal(2, plan.RetryAttemptCount);
        Assert.Contains("already attempted 2 replacement retries", plan.OperatorSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateRetryGuard_blocks_repeat_when_matching_retry_is_already_active()
    {
        var now = new DateTimeOffset(2026, 4, 16, 1, 0, 0, TimeSpan.Zero);
        var originalJob = new api.Models.LibraryRemediationJob
        {
            Id = 50,
            LibraryItemId = 1474,
            RequestedAction = "search_replacement",
            IssueType = "wrong_language",
            Reason = "wrong_language",
            VerificationStatus = "Failed",
            LoopbackStatus = "Recommended",
            RequestedAtUtc = now.AddHours(-2)
        };
        var activeRetry = new api.Models.LibraryRemediationJob
        {
            Id = 51,
            LibraryItemId = 1474,
            RequestedAction = "search_replacement",
            IssueType = "wrong_language",
            Reason = "wrong_language",
            Status = "SearchQueued",
            RequestedAtUtc = now.AddMinutes(-5)
        };

        var decision = LibraryRemediationRepeatGuard.Evaluate(originalJob, [originalJob, activeRetry], now);

        Assert.False(decision.Allowed);
        Assert.Equal(51, decision.BlockingJobId);
        Assert.Contains("already queued", decision.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateRetryGuard_blocks_repeat_during_cooldown_window()
    {
        var now = new DateTimeOffset(2026, 4, 16, 1, 0, 0, TimeSpan.Zero);
        var originalJob = new api.Models.LibraryRemediationJob
        {
            Id = 60,
            LibraryItemId = 1474,
            RequestedAction = "search_replacement",
            IssueType = "runtime_mismatch",
            Reason = "runtime_mismatch",
            VerificationStatus = "NoMatch",
            LoopbackStatus = "Recommended",
            RequestedAtUtc = now.AddHours(-4)
        };
        var recentRetry = new api.Models.LibraryRemediationJob
        {
            Id = 61,
            LibraryItemId = 1474,
            RequestedAction = "search_replacement",
            IssueType = "runtime_mismatch",
            Reason = "runtime_mismatch",
            Status = "NoReplacementFound",
            RequestedAtUtc = now.AddMinutes(-10)
        };

        var decision = LibraryRemediationRepeatGuard.Evaluate(originalJob, [originalJob, recentRetry], now);

        Assert.False(decision.Allowed);
        Assert.Equal(61, decision.BlockingJobId);
        Assert.True(decision.CooldownEndsAtUtc.HasValue);
        Assert.Contains("cooldown", decision.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateRetryGuard_allows_repeat_after_cooldown_and_without_active_duplicate()
    {
        var now = new DateTimeOffset(2026, 4, 16, 1, 0, 0, TimeSpan.Zero);
        var originalJob = new api.Models.LibraryRemediationJob
        {
            Id = 70,
            LibraryItemId = 1474,
            RequestedAction = "search_replacement",
            IssueType = "wrong_language",
            Reason = "wrong_language",
            VerificationStatus = "Failed",
            LoopbackStatus = "Recommended",
            RequestedAtUtc = now.AddHours(-5)
        };
        var olderRetry = new api.Models.LibraryRemediationJob
        {
            Id = 71,
            LibraryItemId = 1474,
            RequestedAction = "search_replacement",
            IssueType = "wrong_language",
            Reason = "wrong_language",
            Status = "VerificationFailed",
            RequestedAtUtc = now.AddHours(-1)
        };

        var decision = LibraryRemediationRepeatGuard.Evaluate(originalJob, [originalJob, olderRetry], now);

        Assert.True(decision.Allowed);
        Assert.Null(decision.BlockingJobId);
        Assert.Null(decision.CooldownEndsAtUtc);
    }

    [Fact]
    public void SearchReplacement_endpoint_checks_repeat_guard_before_executing_search()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var programPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var content = File.ReadAllText(programPath);
        var endpointStart = content.IndexOf("app.MapPost(\"/api/library/items/{id:long}/remediation/search-replacement\"", StringComparison.Ordinal);
        var endpointEnd = content.IndexOf("app.MapPost(\"/api/library/items/{id:long}/sources/{serviceKey}/sync\"", StringComparison.Ordinal);
        var endpointSlice = content[endpointStart..endpointEnd];

        Assert.Contains("LibraryRemediationRepeatGuard.Evaluate", endpointSlice);
        Assert.Contains("var relatedJobs = await db.LibraryRemediationJobs", endpointSlice);
        Assert.True(
            endpointSlice.IndexOf("LibraryRemediationRepeatGuard.Evaluate", StringComparison.Ordinal) < endpointSlice.IndexOf("ExecuteSearchReplacementAsync", StringComparison.Ordinal),
            "Repeat guard should run before executing a replacement search.");
    }

    [Fact]
    public void EvaluateRetryGuard_blocks_new_request_when_older_active_search_exists()
    {
        var now = new DateTimeOffset(2026, 4, 16, 1, 0, 0, TimeSpan.Zero);
        var newRequest = new api.Models.LibraryRemediationJob
        {
            Id = 80,
            LibraryItemId = 1474,
            RequestedAction = "search_replacement",
            IssueType = "media_compatibility",
            Reason = "request_better_file",
            RequestedAtUtc = now
        };
        var olderActiveJob = new api.Models.LibraryRemediationJob
        {
            Id = 81,
            LibraryItemId = 1474,
            RequestedAction = "search_replacement",
            IssueType = "media_compatibility",
            Reason = "request_better_file",
            Status = "SearchQueued",
            RequestedAtUtc = now.AddMinutes(-2)
        };

        var decision = LibraryRemediationRepeatGuard.Evaluate(newRequest, [newRequest, olderActiveJob], now);

        Assert.False(decision.Allowed);
        Assert.Equal(81, decision.BlockingJobId);
        Assert.Contains("already queued", decision.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateRetryGuard_allows_retry_when_older_active_job_is_stale_legacy_queue_without_provider_tracking()
    {
        var now = new DateTimeOffset(2026, 4, 28, 15, 0, 0, TimeSpan.Zero);
        var newRequest = new api.Models.LibraryRemediationJob
        {
            Id = 90,
            LibraryItemId = 1474,
            RequestedAction = "search_replacement",
            IssueType = "media_compatibility",
            Reason = "request_better_file",
            RequestedAtUtc = now
        };
        var staleLegacyQueuedJob = new api.Models.LibraryRemediationJob
        {
            Id = 91,
            LibraryItemId = 1474,
            RequestedAction = "search_replacement",
            IssueType = "media_compatibility",
            Reason = "request_better_file",
            Status = "SearchQueued",
            SearchStatus = "Queued",
            RequestedAtUtc = now.AddDays(-4),
            ProviderCommandId = null,
            ProviderCommandStatus = string.Empty,
            ProviderCommandCheckedAtUtc = null,
            LastCheckedAtUtc = now.AddMinutes(-1),
            VerificationCheckedAtUtc = now.AddMinutes(-1)
        };

        var decision = LibraryRemediationRepeatGuard.Evaluate(newRequest, [newRequest, staleLegacyQueuedJob], now);

        Assert.True(decision.Allowed);
        Assert.Null(decision.BlockingJobId);
        Assert.Null(decision.CooldownEndsAtUtc);
    }

    [Fact]
    public void EvaluateRetryGuard_still_blocks_processing_job_without_provider_tracking_even_when_old()
    {
        var now = new DateTimeOffset(2026, 4, 28, 15, 0, 0, TimeSpan.Zero);
        var newRequest = new api.Models.LibraryRemediationJob
        {
            Id = 92,
            LibraryItemId = 1474,
            RequestedAction = "search_replacement",
            IssueType = "media_compatibility",
            Reason = "request_better_file",
            RequestedAtUtc = now
        };
        var inProgressJob = new api.Models.LibraryRemediationJob
        {
            Id = 93,
            LibraryItemId = 1474,
            RequestedAction = "search_replacement",
            IssueType = "media_compatibility",
            Reason = "request_better_file",
            Status = "Processing",
            SearchStatus = "Queued",
            RequestedAtUtc = now.AddDays(-4),
            ProviderCommandId = null,
            ProviderCommandStatus = string.Empty,
            ProviderCommandCheckedAtUtc = null
        };

        var decision = LibraryRemediationRepeatGuard.Evaluate(newRequest, [newRequest, inProgressJob], now);

        Assert.False(decision.Allowed);
        Assert.Equal(93, decision.BlockingJobId);
        Assert.Contains("already queued", decision.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RemediationJobDto_exposes_lifecycle_and_verification_fields()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var programPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var content = File.ReadAllText(programPath);
        var dtoStart = content.IndexOf("public record LibraryRemediationJobDto(", StringComparison.Ordinal);
        var dtoEnd = content.IndexOf("public record SetDesiredMonitoringRequest", StringComparison.Ordinal);
        var dtoSlice = content[dtoStart..dtoEnd];
        var mapStart = content.IndexOf("static LibraryRemediationJobDto MapLibraryRemediationJobDto", StringComparison.Ordinal);
        var mapEnd = content.IndexOf("static void ApplyPreferredDescription", StringComparison.Ordinal);
        var mapSlice = content[mapStart..mapEnd];

        Assert.Contains("long? IntegrationId", dtoSlice);
        Assert.Contains("int? ProviderCommandId", dtoSlice);
        Assert.Contains("string ProviderCommandStatus", dtoSlice);
        Assert.Contains("string ProviderCommandSummary", dtoSlice);
        Assert.Contains("DateTimeOffset? ProviderCommandCheckedAtUtc", dtoSlice);
        Assert.Contains("string VerificationStatus", dtoSlice);
        Assert.Contains("string VerificationSummary", dtoSlice);
        Assert.Contains("DateTimeOffset? VerificationCheckedAtUtc", dtoSlice);
        Assert.Contains("string LoopbackStatus", dtoSlice);
        Assert.Contains("string LoopbackSummary", dtoSlice);
        Assert.Contains("row.IntegrationId", mapSlice);
        Assert.Contains("row.ProviderCommandId", mapSlice);
        Assert.Contains("row.ProviderCommandStatus", mapSlice);
        Assert.Contains("row.ProviderCommandSummary", mapSlice);
        Assert.Contains("row.ProviderCommandCheckedAtUtc", mapSlice);
        Assert.Contains("row.VerificationStatus", mapSlice);
        Assert.Contains("row.VerificationSummary", mapSlice);
        Assert.Contains("row.VerificationCheckedAtUtc", mapSlice);
        Assert.Contains("row.LoopbackStatus", mapSlice);
        Assert.Contains("row.LoopbackSummary", mapSlice);
    }

    [Fact]
    public void Legacy_sqlite_bootstrap_ensures_newer_remediation_lifecycle_columns_for_existing_databases()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var programPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var content = File.ReadAllText(programPath);

        Assert.Contains("EnsureSqliteColumn(db, \"LibraryRemediationJobs\", \"VerificationStatus\", \"TEXT NOT NULL DEFAULT ''\");", content);
        Assert.Contains("EnsureSqliteColumn(db, \"LibraryRemediationJobs\", \"VerificationSummary\", \"TEXT NOT NULL DEFAULT ''\");", content);
        Assert.Contains("EnsureSqliteColumn(db, \"LibraryRemediationJobs\", \"VerificationDetailsJson\", \"TEXT NOT NULL DEFAULT ''\");", content);
        Assert.Contains("EnsureSqliteColumn(db, \"LibraryRemediationJobs\", \"VerificationCheckedAtUtc\", \"TEXT NULL\");", content);
        Assert.Contains("EnsureSqliteColumn(db, \"LibraryRemediationJobs\", \"LoopbackStatus\", \"TEXT NOT NULL DEFAULT ''\");", content);
        Assert.Contains("EnsureSqliteColumn(db, \"LibraryRemediationJobs\", \"LoopbackSummary\", \"TEXT NOT NULL DEFAULT ''\");", content);
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
    public void PlanSearchReplacement_falls_back_to_series_link_for_episode_when_episode_link_missing()
    {
        var plan = LibraryRemediationPlanner.PlanSearchReplacement(
            mediaType: "Episode",
            sourceLinks:
            [
                new LibraryRemediationSourceLink("sonarr", 44, "21", "series", false)
            ]);

        Assert.Equal("sonarr", plan.ServiceKey);
        Assert.Equal(44, plan.IntegrationId);
        Assert.Equal("EpisodeSearch", plan.CommandName);
        Assert.Null(plan.ExternalItemId);
        Assert.True(plan.RequiresRemoteLookup);
        Assert.Equal(21, plan.RemoteLookupParentItemId);
        Assert.Contains("linked series", plan.Message, StringComparison.OrdinalIgnoreCase);
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
    public void PlanSearchReplacement_falls_back_to_artist_link_for_album_when_album_link_missing()
    {
        var plan = LibraryRemediationPlanner.PlanSearchReplacement(
            mediaType: "Album",
            sourceLinks:
            [
                new LibraryRemediationSourceLink("lidarr", 55, "88", "artist", false)
            ]);

        Assert.Equal("lidarr", plan.ServiceKey);
        Assert.Equal(55, plan.IntegrationId);
        Assert.Equal("AlbumSearch", plan.CommandName);
        Assert.Null(plan.ExternalItemId);
        Assert.True(plan.RequiresRemoteLookup);
        Assert.Equal(88, plan.RemoteLookupParentItemId);
        Assert.Contains("linked artist", plan.Message, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public void ApplyOperatorReview_records_profile_review_outcome()
    {
        var job = new LibraryRemediationJob
        {
            RequestedAction = "search_replacement",
            LoopbackStatus = "Recommended",
            UpdatedAtUtc = new DateTimeOffset(2026, 4, 16, 9, 0, 0, TimeSpan.Zero)
        };
        var reviewedAtUtc = new DateTimeOffset(2026, 4, 16, 9, 30, 0, TimeSpan.Zero);

        LibraryRemediationOperatorReviewTracker.Apply(job, "profile_reviewed", "mark", reviewedAtUtc);

        Assert.Equal("ProfileReviewed", job.OperatorReviewStatus);
        Assert.Equal("mark", job.OperatorReviewedBy);
        Assert.Equal(reviewedAtUtc, job.OperatorReviewedAtUtc);
        Assert.Equal(reviewedAtUtc, job.UpdatedAtUtc);
        Assert.Contains("profile", job.OperatorReviewSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryNormalizeOutcome_rejects_unknown_operator_review_outcome()
    {
        var success = LibraryRemediationOperatorReviewTracker.TryNormalizeOutcome("invented_status", out var normalized);

        Assert.False(success);
        Assert.Equal(string.Empty, normalized);
    }

    [Fact]
    public async Task RefreshAsync_reconciles_stale_radarr_external_item_id_from_tmdb_before_fetching_progress()
    {
        using var db = CreateDb();
        var integration = new IntegrationConfig
        {
            Id = 2,
            ServiceKey = "radarr",
            InstanceName = "Radarr",
            BaseUrl = "http://radarr.local",
            AuthType = "ApiKey",
            ApiKey = "secret"
        };
        var item = new LibraryItem
        {
            Id = 1298,
            MediaType = "Movie",
            CanonicalKey = "movie:1100988",
            Title = "28 Years Later",
            SortTitle = "28 Years Later",
            TmdbId = 1100988
        };
        var requestedAt = new DateTimeOffset(2026, 4, 27, 20, 9, 33, TimeSpan.Zero);
        var job = new LibraryRemediationJob
        {
            Id = 15,
            LibraryItemId = 1298,
            IntegrationId = 2,
            ServiceKey = "radarr",
            ServiceDisplayName = "Radarr",
            RequestedAction = "search_replacement",
            CommandName = "MoviesSearch",
            ExternalItemId = 6,
            ProviderCommandId = 466317,
            RequestedAtUtc = requestedAt,
            Status = "SearchQueued",
            SearchStatus = "Queued",
            VerificationStatus = "Pending"
        };
        var sourceLink = new LibraryItemSourceLink
        {
            LibraryItemId = 1298,
            IntegrationId = 2,
            ExternalId = "6",
            ExternalType = "movie",
            SourceTitle = "28 Years Later",
            SourceSortTitle = "28 Years Later",
            SourcePayloadHash = "hash",
            FirstSeenAtUtc = requestedAt,
            LastSeenAtUtc = requestedAt
        };

        db.IntegrationConfigs.Add(integration);
        db.LibraryItems.Add(item);
        db.LibraryRemediationJobs.Add(job);
        db.LibraryItemSourceLinks.Add(sourceLink);
        db.SaveChanges();

        using var handler = new StubHttpMessageHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url == "http://radarr.local/api/v3/movie?tmdbId=1100988")
            {
                return Json(HttpStatusCode.OK, "[{\"id\":42,\"monitored\":true}]");
            }

            if (url == "http://radarr.local/api/v3/command/466317")
            {
                return Json(HttpStatusCode.OK, "{\"id\":466317,\"status\":\"completed\",\"name\":\"MoviesSearch\"}");
            }

            if (url.Contains("/api/v3/queue?", StringComparison.Ordinal) && url.Contains("movieId=42", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, "{\"records\":[{\"title\":\"28.Years.Later.2025.2160p\",\"status\":\"downloading\",\"trackedDownloadState\":\"downloading\"}]}");
            }

            if (url.Contains("/api/v3/history/movie?", StringComparison.Ordinal) && url.Contains("movieId=42", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, "{\"records\":[{\"eventType\":\"grabbed\",\"date\":\"2026-04-27T20:10:00Z\",\"sourceTitle\":\"28.Years.Later.2025.2160p\"}]}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(url)
            };
        });
        var factory = new StubHttpClientFactory(handler);

        var changed = await LibraryRemediationProviderCommandTracker.RefreshAsync(job, db, factory, requestedAt.AddMinutes(5));
        var requestUris = handler.Requests.Select(request => request.RequestUri!.ToString()).ToArray();

        Assert.True(requestUris.Any(url => url == "http://radarr.local/api/v3/movie?tmdbId=1100988"), string.Join("\n", requestUris));
        Assert.True(requestUris.Any(url => url.Contains("/api/v3/command/466317", StringComparison.Ordinal)), string.Join("\n", requestUris));
        Assert.True(requestUris.Any(url => url.Contains("/api/v3/queue?", StringComparison.Ordinal) && url.Contains("movieId=42", StringComparison.Ordinal)), string.Join("\n", requestUris));
        Assert.True(requestUris.Any(url => url.Contains("/api/v3/history/movie?", StringComparison.Ordinal) && url.Contains("movieId=42", StringComparison.Ordinal)), string.Join("\n", requestUris));
        Assert.True(changed);
        Assert.Equal(42, job.ExternalItemId);
        Assert.Equal("Downloading", job.SearchStatus);
        Assert.Equal("Processing", job.Status);
        Assert.Contains("28.Years.Later.2025.2160p", job.OutcomeSummary);
        Assert.Equal("42", sourceLink.ExternalId);
        Assert.DoesNotContain(handler.Requests, request => request.RequestUri!.ToString().Contains("movieId=6", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RefreshAsync_returns_changed_when_external_item_reconciles_even_if_provider_snapshot_is_otherwise_unchanged()
    {
        using var db = CreateDb();
        var requestedAt = new DateTimeOffset(2026, 4, 27, 20, 5, 0, TimeSpan.Zero);
        var checkedAt = requestedAt.AddMinutes(5);
        var integration = new IntegrationConfig
        {
            Id = 7,
            ServiceKey = "radarr",
            InstanceName = "Radarr",
            BaseUrl = "http://radarr.local",
            ApiKey = "abc123"
        };
        db.IntegrationConfigs.Add(integration);

        var item = new LibraryItem
        {
            Id = 1298,
            MediaType = "Movie",
            Title = "28 Years Later",
            TmdbId = 1100988,
            CanonicalKey = "movie:1100988",
            UpdatedAtUtc = requestedAt,
            SourceUpdatedAtUtc = requestedAt
        };
        db.LibraryItems.Add(item);

        var sourceLink = new LibraryItemSourceLink
        {
            LibraryItemId = item.Id,
            IntegrationId = integration.Id,
            ExternalId = "6",
            ExternalType = "movie",
            SourceTitle = "28 Years Later",
            SourceSortTitle = "28 Years Later",
            LastSeenAtUtc = requestedAt
        };
        db.LibraryItemSourceLinks.Add(sourceLink);

        var job = new LibraryRemediationJob
        {
            LibraryItemId = item.Id,
            IntegrationId = integration.Id,
            ServiceKey = "radarr",
            ServiceDisplayName = "Radarr",
            RequestedAction = "search_replacement",
            CommandName = "MoviesSearch",
            ExternalItemId = 6,
            ProviderCommandId = 466317,
            ProviderCommandStatus = "completed",
            ProviderCommandSummary = "MoviesSearch completed.",
            ProviderCommandCheckedAtUtc = checkedAt,
            SearchStatus = "Queued",
            Status = "SearchQueued",
            RequestedAtUtc = requestedAt,
            UpdatedAtUtc = requestedAt
        };
        db.LibraryRemediationJobs.Add(job);
        await db.SaveChangesAsync();

        using var handler = new StubHttpMessageHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url == "http://radarr.local/api/v3/movie?tmdbId=1100988")
            {
                return Json(HttpStatusCode.OK, "[{\"id\":42,\"monitored\":true}]");
            }

            if (url == "http://radarr.local/api/v3/command/466317")
            {
                return Json(HttpStatusCode.OK, "{\"id\":466317,\"status\":\"completed\",\"name\":\"MoviesSearch\"}");
            }

            if (url.Contains("/api/v3/queue?", StringComparison.Ordinal) && url.Contains("movieId=42", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, "{\"records\":[]}");
            }

            if (url.Contains("/api/v3/history/movie?", StringComparison.Ordinal) && url.Contains("movieId=42", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, "{\"records\":[]}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(url)
            };
        });
        var factory = new StubHttpClientFactory(handler);

        var changed = await LibraryRemediationProviderCommandTracker.RefreshAsync(job, db, factory, checkedAt);

        Assert.True(changed);
        Assert.Equal(42, job.ExternalItemId);
        Assert.Equal("42", sourceLink.ExternalId);
    }

    private static MediaCloudDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<MediaCloudDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        var db = new MediaCloudDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string body)
        => new(statusCode)
        {
            Content = new StringContent(body)
        };

    private sealed class StubHttpClientFactory(StubHttpMessageHandler handler) : IHttpClientFactory
    {
        private readonly StubHttpMessageHandler _handler = handler;

        public HttpClient CreateClient(string name = "") => new(_handler, disposeHandler: false);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder = responder;
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new HttpRequestMessage(request.Method, request.RequestUri));
            return Task.FromResult(_responder(request));
        }
    }
}
