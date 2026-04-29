using System.Text.Json;
using api.Models;

namespace api;

public sealed record LibraryRemediationLifecycleSnapshot(
    string Status,
    string SearchStatus,
    string BlacklistStatus,
    string OutcomeSummary,
    string VerificationStatus,
    string VerificationSummary,
    string VerificationDetailsJson,
    string LoopbackStatus,
    string LoopbackSummary,
    DateTimeOffset LastCheckedAtUtc,
    DateTimeOffset? VerificationCheckedAtUtc);

public static class LibraryRemediationExecution
{
    public static LibraryItemRemediationResponse BuildBlockedResult(
        long libraryItemId,
        string serviceKey,
        string serviceDisplayName,
        string commandName,
        int? externalItemId,
        string reason,
        string notes,
        string message,
        LibraryRemediationIntent intent)
        => new(
            libraryItemId,
            false,
            serviceKey,
            serviceDisplayName,
            commandName,
            externalItemId,
            false,
            reason,
            notes,
            message,
            new LibraryRemediationIntentDto(
                intent.IssueType,
                intent.RequestedAction,
                intent.ReasonCategory,
                intent.Confidence,
                intent.ShouldSearchNow,
                intent.ShouldBlacklistCurrentRelease,
                intent.NeedsManualReview,
                intent.NotesRecordedOnly,
                intent.ApprovalRequired,
                intent.ApprovalReason,
                intent.PolicySummary,
                intent.NotesHandling,
                intent.ProfileDecision,
                intent.ProfileSummary,
                intent.PolicyState,
                intent.NextActionSummary));
}

public static class LibraryRemediationLifecycleTracker
{
    private const string SearchStatusQueued = "Queued";
    private const string SearchStatusNoResults = "NoResults";
    private const string SearchStatusGrabbed = "Grabbed";

    public static LibraryRemediationLifecycleSnapshot Evaluate(
        LibraryRemediationJob job,
        LibraryItem item,
        LibraryIssue? relatedIssue,
        LibraryRemediationReleaseContext? latestContext)
    {
        var checkedAt = DateTimeOffset.UtcNow;
        var status = string.IsNullOrWhiteSpace(job.Status) ? "Pending" : job.Status;
        var searchStatus = string.IsNullOrWhiteSpace(job.SearchStatus) ? InferSearchStatus(job) : job.SearchStatus;
        var blacklistStatus = string.IsNullOrWhiteSpace(job.BlacklistStatus) ? InferBlacklistStatus(job) : job.BlacklistStatus;
        var outcome = string.IsNullOrWhiteSpace(job.OutcomeSummary) ? job.ResultMessage : job.OutcomeSummary;

        if (IsBlockedStatus(status))
        {
            var summary = FirstNonEmpty(outcome, job.ResultMessage, "Remediation was blocked.");
            return new(
                status,
                searchStatus,
                blacklistStatus,
                summary,
                DetermineVerificationStatusForStatus(status),
                DetermineVerificationSummaryForStatus(status, summary),
                BuildVerificationDetailsJson(job.IssueType, item, relatedIssue, status, summary),
                DetermineLoopbackStatusForStatus(status),
                DetermineLoopbackSummaryForStatus(status, job),
                checkedAt,
                checkedAt);
        }

        if (relatedIssue is not null
            && string.Equals(relatedIssue.Status, "Resolved", StringComparison.OrdinalIgnoreCase)
            && relatedIssue.ResolvedAtUtc.HasValue
            && relatedIssue.ResolvedAtUtc.Value >= job.RequestedAtUtc)
        {
            const string summary = "Related issue resolved after remediation request.";
            return new(
                "Resolved",
                "Completed",
                blacklistStatus,
                summary,
                "Verified",
                "Related issue was already resolved during verification.",
                BuildVerificationDetailsJson(job.IssueType, item, relatedIssue, "Resolved", summary),
                "NotNeeded",
                "Verification passed; no repeat remediation is recommended.",
                checkedAt,
                checkedAt);
        }

        if (IsImportedStatus(searchStatus))
        {
            if (ItemUpdatedAfterRequest(item, job.RequestedAtUtc))
            {
                var verification = VerifyIssueAfterReplacement(job.IssueType, item, relatedIssue);
                if (verification.IsVerified)
                {
                    return new(
                        "Resolved",
                        "Completed",
                        blacklistStatus,
                        verification.Message,
                        "Verified",
                        verification.Message,
                        BuildVerificationDetailsJson(job.IssueType, item, relatedIssue, "Resolved", verification.Message),
                        "NotNeeded",
                        "Verification passed; no repeat remediation is recommended.",
                        checkedAt,
                        checkedAt);
                }

                if (verification.ShouldMarkFailed)
                {
                    return new(
                        "VerificationFailed",
                        "Completed",
                        blacklistStatus,
                        verification.Message,
                        "Failed",
                        verification.Message,
                        BuildVerificationDetailsJson(job.IssueType, item, relatedIssue, "VerificationFailed", verification.Message),
                        "Recommended",
                        BuildLoopbackSummary(job, verification.Message),
                        checkedAt,
                        checkedAt);
                }
            }

            const string summary = "Radarr imported a replacement. MediaCloud is waiting for source refresh and verification.";
            return new(
                "Processing",
                "Imported",
                blacklistStatus,
                FirstNonEmpty(outcome, summary),
                "WaitingForEvidence",
                "The provider says the replacement imported, but MediaCloud is still waiting for refreshed local metadata.",
                BuildVerificationDetailsJson(job.IssueType, item, relatedIssue, "Processing", FirstNonEmpty(outcome, summary)),
                "Standby",
                "Wait for the current remediation attempt to refresh local metadata before repeating it.",
                checkedAt,
                checkedAt);
        }

        if (IsImportingStatus(searchStatus))
        {
            var summary = FirstNonEmpty(outcome, "The provider is importing the replacement now.");
            return new(
                "Processing",
                "Importing",
                blacklistStatus,
                summary,
                "Pending",
                "The provider is importing the replacement before verification can start.",
                BuildVerificationDetailsJson(job.IssueType, item, relatedIssue, "Processing", summary),
                "Standby",
                "Wait for the current remediation attempt to finish importing before repeating it.",
                checkedAt,
                checkedAt);
        }

        if (IsDownloadingStatus(searchStatus))
        {
            var summary = FirstNonEmpty(outcome, "The provider is downloading the replacement now.");
            return new(
                "Processing",
                "Downloading",
                blacklistStatus,
                summary,
                "Pending",
                "The provider is downloading the replacement before verification can start.",
                BuildVerificationDetailsJson(job.IssueType, item, relatedIssue, "Processing", summary),
                "Standby",
                "Wait for the current remediation attempt to finish downloading before repeating it.",
                checkedAt,
                checkedAt);
        }

        if (IsGrabbedStatus(searchStatus))
        {
            var summary = FirstNonEmpty(outcome, "The provider grabbed a replacement and is waiting for download/import.");
            return new(
                "Processing",
                "Grabbed",
                blacklistStatus,
                summary,
                "Pending",
                "The provider grabbed a replacement, but verification cannot start until download/import finishes.",
                BuildVerificationDetailsJson(job.IssueType, item, relatedIssue, "Processing", summary),
                "Standby",
                "Wait for the current remediation attempt to finish downloading before repeating it.",
                checkedAt,
                checkedAt);
        }

        if (IsQueuedStatus(searchStatus)
            && ReleaseChangedAfterRequest(job, item, latestContext))
        {
            var verification = VerifyIssueAfterReplacement(job.IssueType, item, relatedIssue);
            if (verification.IsVerified)
            {
                return new(
                    "Resolved",
                    "Completed",
                    blacklistStatus,
                    verification.Message,
                    "Verified",
                    verification.Message,
                    BuildVerificationDetailsJson(job.IssueType, item, relatedIssue, "Resolved", verification.Message),
                    "NotNeeded",
                    "Verification passed; no repeat remediation is recommended.",
                    checkedAt,
                    checkedAt);
            }

            if (verification.ShouldMarkFailed)
            {
                return new(
                    "VerificationFailed",
                    "Completed",
                    blacklistStatus,
                    verification.Message,
                    "Failed",
                    verification.Message,
                    BuildVerificationDetailsJson(job.IssueType, item, relatedIssue, "VerificationFailed", verification.Message),
                    "Recommended",
                    BuildLoopbackSummary(job, verification.Message),
                    checkedAt,
                    checkedAt);
            }

            return new(
                "ImportedReplacement",
                "Completed",
                blacklistStatus,
                verification.Message,
                "WaitingForEvidence",
                verification.Message,
                BuildVerificationDetailsJson(job.IssueType, item, relatedIssue, "ImportedReplacement", verification.Message),
                "Standby",
                "Wait for fresher metadata or probes before repeating remediation.",
                checkedAt,
                checkedAt);
        }

        if (IsQueuedStatus(searchStatus)
            && ItemUpdatedAfterRequest(item, job.RequestedAtUtc))
        {
            const string summary = "Source metadata changed after the remediation request. MediaCloud is waiting to confirm whether a replacement import sticks.";
            return new(
                "Processing",
                "Queued",
                blacklistStatus,
                summary,
                "WaitingForEvidence",
                summary,
                BuildVerificationDetailsJson(job.IssueType, item, relatedIssue, "Processing", summary),
                "Standby",
                "Wait for fresher metadata or probes before repeating remediation.",
                checkedAt,
                checkedAt);
        }

        var defaultSummary = FirstNonEmpty(outcome, job.ResultMessage, "Remediation request recorded.");
        return new(
            status,
            searchStatus,
            blacklistStatus,
            defaultSummary,
            DetermineVerificationStatusForStatus(status),
            FirstNonEmpty(job.VerificationSummary, DetermineVerificationSummaryForStatus(status, defaultSummary)),
            BuildVerificationDetailsJson(job.IssueType, item, relatedIssue, status, defaultSummary),
            DetermineLoopbackStatusForStatus(status),
            FirstNonEmpty(job.LoopbackSummary, DetermineLoopbackSummaryForStatus(status, job)),
            checkedAt,
            checkedAt);
    }

    public static void Apply(LibraryRemediationJob job, LibraryRemediationLifecycleSnapshot snapshot)
    {
        job.Status = snapshot.Status;
        job.SearchStatus = snapshot.SearchStatus;
        job.BlacklistStatus = snapshot.BlacklistStatus;
        job.OutcomeSummary = snapshot.OutcomeSummary;
        job.VerificationStatus = snapshot.VerificationStatus;
        job.VerificationSummary = snapshot.VerificationSummary;
        job.VerificationDetailsJson = snapshot.VerificationDetailsJson;
        job.VerificationCheckedAtUtc = snapshot.VerificationCheckedAtUtc;
        job.LoopbackStatus = snapshot.LoopbackStatus;
        job.LoopbackSummary = snapshot.LoopbackSummary;
        job.LastCheckedAtUtc = snapshot.LastCheckedAtUtc;
        job.UpdatedAtUtc = snapshot.LastCheckedAtUtc;
    }

    public static string DetermineInitialStatus(LibraryRemediationIntent intent, LibraryItemRemediationResponse result, bool? blacklistSucceeded)
    {
        if (!intent.ShouldSearchNow)
        {
            return intent.ProfileDecision is "review_language_profile" or "review_quality_profile"
                ? "BlockedProfileReview"
                : "BlockedManualReview";
        }

        if (!result.Success)
        {
            return "Failed";
        }

        var searchStatus = DetermineInitialSearchStatus(intent, result);
        if (IsNoResultsStatus(searchStatus))
        {
            return "NoReplacementFound";
        }

        if (IsGrabbedStatus(searchStatus))
        {
            return "Processing";
        }

        return blacklistSucceeded == true ? "BlacklistedAndQueued" : "SearchQueued";
    }

    public static bool IsTerminalStatus(string? status)
    {
        var normalized = (status ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return normalized is "BlockedProfileReview"
            or "BlockedManualReview"
            or "Failed"
            or "NoReplacementFound"
            or "Resolved"
            or "VerificationFailed";
    }

    public static string DetermineInitialSearchStatus(LibraryRemediationIntent intent, LibraryItemRemediationResponse result)
    {
        if (!intent.ShouldSearchNow)
        {
            return "Blocked";
        }

        if (!result.Success)
        {
            return "Failed";
        }

        var hint = (result.SearchStatusHint ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(hint) ? SearchStatusQueued : hint;
    }

    public static string DetermineInitialBlacklistStatus(LibraryRemediationIntent intent, bool? blacklistSucceeded)
    {
        if (!intent.ShouldSearchNow || !intent.ShouldBlacklistCurrentRelease)
        {
            return "NotNeeded";
        }

        if (!blacklistSucceeded.HasValue)
        {
            return "Skipped";
        }

        return blacklistSucceeded.Value ? "Succeeded" : "Failed";
    }

    public static string DetermineInitialOutcomeSummary(LibraryRemediationIntent intent, LibraryItemRemediationResponse result, bool? blacklistSucceeded)
    {
        if (!intent.ShouldSearchNow)
        {
            return intent.ProfileDecision is "review_language_profile" or "review_quality_profile"
                ? intent.ProfileSummary
                : intent.PolicySummary;
        }

        if (!result.Success)
        {
            return result.Message;
        }

        if (blacklistSucceeded == true)
        {
            if (IsNoResultsStatus(result.SearchStatusHint))
            {
                return "Blacklisted current release and ran replacement search, but the provider reported no downloadable results.";
            }

            return "Blacklisted current release and queued replacement search.";
        }

        if (blacklistSucceeded == false)
        {
            if (IsNoResultsStatus(result.SearchStatusHint))
            {
                return "Blacklist failed or was skipped, and the replacement search completed with no downloadable results.";
            }

            return "Blacklist failed or was skipped, but replacement search was still queued.";
        }

        if (IsNoResultsStatus(result.SearchStatusHint))
        {
            return "Replacement search completed, but the provider reported no downloadable results.";
        }

        if (IsGrabbedStatus(result.SearchStatusHint))
        {
            return "Replacement search grabbed at least one candidate. MediaCloud is waiting for import and verification.";
        }

        return "Queued replacement search.";
    }

    public static string DetermineInitialVerificationStatus(LibraryRemediationIntent intent, LibraryItemRemediationResponse result)
    {
        if (!intent.ShouldSearchNow)
        {
            return "NotStarted";
        }

        if (!result.Success)
        {
            return "Failed";
        }

        return IsNoResultsStatus(result.SearchStatusHint)
            ? "NoMatch"
            : "Pending";
    }

    public static string DetermineInitialVerificationSummary(LibraryRemediationIntent intent, LibraryItemRemediationResponse result)
    {
        if (!intent.ShouldSearchNow)
        {
            return intent.ProfileDecision is "review_language_profile" or "review_quality_profile"
                ? "Verification cannot start until the acquisition profile is reviewed."
                : "Verification did not start because this issue is routed to manual review.";
        }

        if (!result.Success)
        {
            return "Remediation request failed before verification could complete.";
        }

        return IsNoResultsStatus(result.SearchStatusHint)
            ? "No replacement was found, so verification cannot continue on a new release yet."
            : "Waiting for the source stack to import or reject a replacement before verification can run.";
    }

    public static string BuildInitialVerificationDetailsJson(LibraryRemediationIntent intent, LibraryItemRemediationResponse result)
        => JsonSerializer.Serialize(new
        {
            phase = "initial",
            issueType = intent.IssueType,
            requestedAction = intent.RequestedAction,
            searchStatusHint = result.SearchStatusHint,
            result.Success,
            result.Message
        });

    public static DateTimeOffset? DetermineInitialVerificationCheckedAtUtc(LibraryRemediationIntent intent, LibraryItemRemediationResponse result, DateTimeOffset requestedAtUtc)
        => !intent.ShouldSearchNow || !result.Success || IsNoResultsStatus(result.SearchStatusHint)
            ? requestedAtUtc
            : null;

    public static string DetermineInitialLoopbackStatus(LibraryRemediationIntent intent, LibraryItemRemediationResponse result)
    {
        if (!intent.ShouldSearchNow)
        {
            return "NotNeeded";
        }

        if (!result.Success || IsNoResultsStatus(result.SearchStatusHint))
        {
            return "Recommended";
        }

        return "Standby";
    }

    public static string DetermineInitialLoopbackSummary(LibraryRemediationIntent intent, LibraryItemRemediationResponse result)
    {
        if (!intent.ShouldSearchNow)
        {
            return "Repeat remediation is not recommended until profile review or manual triage is complete.";
        }

        if (!result.Success)
        {
            return "Fix the provider or execution failure before repeating remediation.";
        }

        return IsNoResultsStatus(result.SearchStatusHint)
            ? "No replacement was found; use profile tuning or manual review before repeating remediation."
            : "Wait for verification evidence before deciding whether remediation should repeat.";
    }

    private static bool IsQueuedStatus(string? searchStatus)
        => string.Equals(searchStatus, SearchStatusQueued, StringComparison.OrdinalIgnoreCase);

    private static bool IsDownloadingStatus(string? searchStatus)
        => string.Equals(searchStatus, "Downloading", StringComparison.OrdinalIgnoreCase);

    private static bool IsImportingStatus(string? searchStatus)
        => string.Equals(searchStatus, "Importing", StringComparison.OrdinalIgnoreCase);

    private static bool IsImportedStatus(string? searchStatus)
        => string.Equals(searchStatus, "Imported", StringComparison.OrdinalIgnoreCase);

    private static bool IsNoResultsStatus(string? searchStatus)
        => string.Equals(searchStatus, SearchStatusNoResults, StringComparison.OrdinalIgnoreCase);

    private static bool IsGrabbedStatus(string? searchStatus)
        => string.Equals(searchStatus, SearchStatusGrabbed, StringComparison.OrdinalIgnoreCase);

    private static (bool IsVerified, bool ShouldMarkFailed, string Message) VerifyIssueAfterReplacement(string? issueType, LibraryItem item, LibraryIssue? relatedIssue)
    {
        var normalizedIssueType = (issueType ?? relatedIssue?.IssueType ?? string.Empty).Trim().ToLowerInvariant();
        var audioLanguages = ParseLanguages(item.AudioLanguagesJson);
        var subtitleLanguages = ParseLanguages(item.SubtitleLanguagesJson);
        var hasLanguageEvidence = audioLanguages.Count > 0 || subtitleLanguages.Count > 0;
        var hasEnglishAudio = ContainsEnglish(audioLanguages);
        var hasEnglishSubtitle = ContainsEnglish(subtitleLanguages);
        var playability = (item.PlayabilityScore ?? string.Empty).Trim().ToLowerInvariant();
        var latestItemEvidenceAt = item.SourceUpdatedAtUtc.HasValue && item.SourceUpdatedAtUtc.Value > item.UpdatedAtUtc
            ? item.SourceUpdatedAtUtc.Value
            : item.UpdatedAtUtc;
        var hasFreshPlayabilityEvidence = item.PlayabilityCheckedAtUtc.HasValue && item.PlayabilityCheckedAtUtc.Value >= latestItemEvidenceAt;
        var runtimeDelta = item.RuntimeMinutes.HasValue && item.ActualRuntimeMinutes.HasValue
            ? Math.Abs(item.RuntimeMinutes.Value - item.ActualRuntimeMinutes.Value)
            : (double?)null;

        return normalizedIssueType switch
        {
            "wrong_language" or "audio_language_mismatch" => !hasLanguageEvidence
                ? (false, false, "Replacement imported. Verification is waiting for refreshed language metadata.")
                : hasEnglishAudio
                    ? (true, false, "Replacement imported and verification passed: English audio is now present.")
                    : (false, true, "Replacement imported, but the language issue is still present after verification."),
            "subtitle_missing" or "subtitle_language_mismatch" => !hasLanguageEvidence
                ? (false, false, "Replacement imported. Verification is waiting for refreshed subtitle metadata.")
                : hasEnglishSubtitle
                    ? (true, false, "Replacement imported and verification passed: expected subtitle language is now present.")
                    : (false, true, "Replacement imported, but the subtitle issue is still present after verification."),
            "runtime_mismatch" => runtimeDelta.HasValue && runtimeDelta.Value <= 3d
                ? (true, false, "Replacement imported and verification passed: runtime mismatch is now within tolerance.")
                : runtimeDelta.HasValue
                    ? (false, true, "Replacement imported, but the runtime issue is still present after verification.")
                    : (false, false, "Replacement imported. Runtime verification is waiting for a fresh ffprobe result."),
            "corrupt_file" or "playback_failure" or "media_compatibility" => !hasFreshPlayabilityEvidence
                ? (false, false, "Replacement imported. Verification is waiting for a fresh playability probe.")
                : playability is "good" or "excellent"
                    ? (true, false, "Replacement imported and verification passed: playability is now healthy.")
                    : !string.IsNullOrWhiteSpace(playability)
                        ? (false, true, "Replacement imported, but playability still looks bad after verification.")
                        : (false, false, "Replacement imported. Playability verification is waiting for a fresh probe."),
            _ => relatedIssue is not null && string.Equals(relatedIssue.Status, "Resolved", StringComparison.OrdinalIgnoreCase)
                ? (true, false, "Related issue was already resolved during verification.")
                : (false, false, "Replacement imported. Verification is waiting for clearer issue evidence.")
        };
    }

    private static IReadOnlyCollection<string> ParseLanguages(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var values = JsonSerializer.Deserialize<List<string>>(json);
            return values?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static bool ContainsEnglish(IEnumerable<string> values)
        => values.Any(value => value.Contains("english", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(value, "eng", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(value, "en", StringComparison.OrdinalIgnoreCase));

    private static bool ReleaseChangedAfterRequest(LibraryRemediationJob job, LibraryItem item, LibraryRemediationReleaseContext? latestContext)
    {
        if (!ItemUpdatedAfterRequest(item, job.RequestedAtUtc) || latestContext is null)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(job.ReleaseSummary)
               && !string.Equals(job.ReleaseSummary.Trim(), latestContext.ReleaseSummary.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool ItemUpdatedAfterRequest(LibraryItem item, DateTimeOffset requestedAtUtc)
        => (item.SourceUpdatedAtUtc.HasValue && item.SourceUpdatedAtUtc.Value > requestedAtUtc)
           || item.UpdatedAtUtc > requestedAtUtc;

    private static bool IsBlockedStatus(string status)
        => status.StartsWith("Blocked", StringComparison.OrdinalIgnoreCase);

    private static string InferSearchStatus(LibraryRemediationJob job)
        => string.Equals(job.Status, "Failed", StringComparison.OrdinalIgnoreCase) ? "Failed"
            : IsBlockedStatus(job.Status) ? "Blocked"
            : "Queued";

    private static string InferBlacklistStatus(LibraryRemediationJob job)
        => job.ShouldBlacklistCurrentRelease ? "Skipped" : "NotNeeded";

    private static string DetermineVerificationStatusForStatus(string status)
        => status switch
        {
            "Resolved" => "Verified",
            "VerificationFailed" or "Failed" => "Failed",
            "NoReplacementFound" => "NoMatch",
            "ImportedReplacement" or "Processing" => "WaitingForEvidence",
            "BlockedProfileReview" or "BlockedManualReview" => "NotStarted",
            _ => "Pending"
        };

    private static string DetermineVerificationSummaryForStatus(string status, string fallbackSummary)
        => status switch
        {
            "Resolved" => "Verification passed and current evidence says the issue is fixed.",
            "VerificationFailed" => "Verification failed because the latest evidence still shows the issue.",
            "NoReplacementFound" => "No replacement was found, so verification cannot continue on a new release yet.",
            "ImportedReplacement" => "A replacement appears to be in place, but verification still needs fresher evidence.",
            "Processing" => "A source-side change was observed; verification is waiting for fresher evidence.",
            "BlockedProfileReview" => "Verification cannot start until the acquisition profile is reviewed.",
            "BlockedManualReview" => "Verification did not start because this issue is routed to manual review.",
            "Failed" => "Remediation request failed before verification could complete.",
            _ => fallbackSummary
        };

    private static string DetermineLoopbackStatusForStatus(string status)
        => status switch
        {
            "VerificationFailed" or "NoReplacementFound" or "Failed" => "Recommended",
            "BlacklistedAndQueued" or "SearchQueued" or "Processing" or "ImportedReplacement" => "Standby",
            _ => "NotNeeded"
        };

    private static string DetermineLoopbackSummaryForStatus(string status, LibraryRemediationJob job)
        => status switch
        {
            "VerificationFailed" => BuildLoopbackSummary(job, "Verification still shows the issue."),
            "NoReplacementFound" => "No replacement was found; use profile tuning or manual review before repeating remediation.",
            "Failed" => "Fix the provider or execution failure before repeating remediation.",
            "BlacklistedAndQueued" or "SearchQueued" or "Processing" or "ImportedReplacement" => "Wait for the current remediation attempt to finish verification before repeating it.",
            _ => "Verification passed; no repeat remediation is recommended."
        };

    private static string BuildLoopbackSummary(LibraryRemediationJob job, string verificationMessage)
    {
        if (string.Equals(job.RequestedAction, "search_replacement", StringComparison.OrdinalIgnoreCase))
        {
            return $"{verificationMessage} Consider repeat remediation with a new replacement search or escalate to manual review if the same evidence keeps returning.";
        }

        return $"{verificationMessage} Consider repeat remediation only after operator review.";
    }

    private static string BuildVerificationDetailsJson(string? issueType, LibraryItem item, LibraryIssue? relatedIssue, string status, string summary)
    {
        var audioLanguages = ParseLanguages(item.AudioLanguagesJson);
        var subtitleLanguages = ParseLanguages(item.SubtitleLanguagesJson);
        var runtimeDelta = item.RuntimeMinutes.HasValue && item.ActualRuntimeMinutes.HasValue
            ? Math.Abs(item.RuntimeMinutes.Value - item.ActualRuntimeMinutes.Value)
            : (double?)null;

        return JsonSerializer.Serialize(new
        {
            status,
            issueType,
            summary,
            relatedIssueStatus = relatedIssue?.Status,
            playability = item.PlayabilityScore,
            playabilityCheckedAtUtc = item.PlayabilityCheckedAtUtc,
            runtimeDelta,
            audioLanguages,
            subtitleLanguages,
            sourceUpdatedAtUtc = item.SourceUpdatedAtUtc,
            itemUpdatedAtUtc = item.UpdatedAtUtc
        });
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;
}
