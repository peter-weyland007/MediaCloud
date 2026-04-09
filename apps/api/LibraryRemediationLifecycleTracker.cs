using System.Text.Json;
using api.Models;

namespace api;

public sealed record LibraryRemediationLifecycleSnapshot(
    string Status,
    string SearchStatus,
    string BlacklistStatus,
    string OutcomeSummary,
    DateTimeOffset LastCheckedAtUtc);

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
                intent.PolicySummary,
                intent.NotesHandling,
                intent.ProfileDecision,
                intent.ProfileSummary));
}

public static class LibraryRemediationLifecycleTracker
{
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
            return new(status, searchStatus, blacklistStatus, FirstNonEmpty(outcome, job.ResultMessage, "Remediation was blocked."), checkedAt);
        }

        if (relatedIssue is not null
            && string.Equals(relatedIssue.Status, "Resolved", StringComparison.OrdinalIgnoreCase)
            && relatedIssue.ResolvedAtUtc.HasValue
            && relatedIssue.ResolvedAtUtc.Value >= job.RequestedAtUtc)
        {
            return new("Resolved", "Completed", blacklistStatus, "Related issue resolved after remediation request.", checkedAt);
        }

        if (string.Equals(searchStatus, "Queued", StringComparison.OrdinalIgnoreCase)
            && ReleaseChangedAfterRequest(job, item, latestContext))
        {
            var verification = VerifyIssueAfterReplacement(job.IssueType, item, relatedIssue);
            if (verification.IsVerified)
            {
                return new("Resolved", "Completed", blacklistStatus, verification.Message, checkedAt);
            }

            if (verification.ShouldMarkFailed)
            {
                return new("VerificationFailed", "Completed", blacklistStatus, verification.Message, checkedAt);
            }

            return new("ImportedReplacement", "Completed", blacklistStatus, verification.Message, checkedAt);
        }

        if (string.Equals(searchStatus, "Queued", StringComparison.OrdinalIgnoreCase)
            && ItemUpdatedAfterRequest(item, job.RequestedAtUtc))
        {
            return new("Processing", "Queued", blacklistStatus, "Source metadata changed after the remediation request. MediaCloud is waiting to confirm whether a replacement import sticks.", checkedAt);
        }

        return new(status, searchStatus, blacklistStatus, FirstNonEmpty(outcome, job.ResultMessage, "Remediation request recorded."), checkedAt);
    }

    public static void Apply(LibraryRemediationJob job, LibraryRemediationLifecycleSnapshot snapshot)
    {
        job.Status = snapshot.Status;
        job.SearchStatus = snapshot.SearchStatus;
        job.BlacklistStatus = snapshot.BlacklistStatus;
        job.OutcomeSummary = snapshot.OutcomeSummary;
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
            or "Resolved"
            or "VerificationFailed";
    }

    public static string DetermineInitialSearchStatus(LibraryRemediationIntent intent, LibraryItemRemediationResponse result)
        => !intent.ShouldSearchNow ? "Blocked"
            : result.Success ? "Queued"
            : "Failed";

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
            return "Blacklisted current release and queued replacement search.";
        }

        if (blacklistSucceeded == false)
        {
            return "Blacklist failed or was skipped, but replacement search was still queued.";
        }

        return "Queued replacement search.";
    }

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
            "corrupt_file" or "playback_failure" => !hasFreshPlayabilityEvidence
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

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;
}
