using api.Models;

namespace api;

public sealed record LibraryRemediationFollowUpPlan(
    string ActionKey,
    bool CanRepeatSearch,
    string IssueType,
    string Reason,
    string OperatorSummary,
    string RepeatNotes,
    bool ForceManualReview = false,
    int RetryAttemptCount = 0);

public static class LibraryRemediationFollowUpPlanner
{
    public static readonly int MaxRepeatSearchAttempts = 2;

    public static LibraryRemediationFollowUpPlan Build(LibraryRemediationJob job)
        => Build(job, []);

    public static LibraryRemediationFollowUpPlan Build(
        LibraryRemediationJob job,
        IEnumerable<LibraryRemediationJob> relatedJobs)
    {
        var requestedAction = (job.RequestedAction ?? string.Empty).Trim();
        var verificationStatus = (job.VerificationStatus ?? string.Empty).Trim();
        var loopbackStatus = (job.LoopbackStatus ?? string.Empty).Trim();
        var issueType = FirstNonEmpty(job.IssueType, job.Reason, "other");
        var reason = FirstNonEmpty(job.Reason, issueType);
        var repeatNotes = BuildRepeatNotes(job);
        var retryAttemptCount = CountPriorRepeatAttempts(job, relatedJobs);

        if (!string.Equals(loopbackStatus, "Recommended", StringComparison.OrdinalIgnoreCase))
        {
            return new(
                "none",
                false,
                issueType,
                reason,
                "Wait for the current remediation attempt to finish verification before repeating it.",
                repeatNotes,
                false,
                retryAttemptCount);
        }

        if (!string.Equals(requestedAction, "search_replacement", StringComparison.OrdinalIgnoreCase))
        {
            return new(
                "manual_review",
                false,
                issueType,
                reason,
                "Loopback is recommended, but this remediation path is not a repeatable replacement search. Review it manually.",
                repeatNotes,
                true,
                retryAttemptCount);
        }

        if (retryAttemptCount >= MaxRepeatSearchAttempts)
        {
            return new(
                "manual_review",
                false,
                issueType,
                reason,
                $"MediaCloud already attempted {retryAttemptCount} replacement retries for this issue. Escalate to manual review or profile changes instead of retrying again.",
                repeatNotes,
                true,
                retryAttemptCount);
        }

        return verificationStatus switch
        {
            "Failed" => new(
                "repeat_search_replacement",
                true,
                issueType,
                reason,
                "Verification failed after replacement; repeat the search or escalate to manual review if the same evidence keeps returning.",
                repeatNotes,
                false,
                retryAttemptCount),
            "NoMatch" => new(
                "review_then_retry_search",
                true,
                issueType,
                reason,
                "No replacement was found last time. Review profiles, quality/language constraints, and source coverage before retrying the search.",
                repeatNotes,
                false,
                retryAttemptCount),
            _ => new(
                "manual_review",
                false,
                issueType,
                reason,
                "Loopback is recommended, but MediaCloud needs operator review before retrying this remediation path.",
                repeatNotes,
                true,
                retryAttemptCount)
        };
    }

    private static int CountPriorRepeatAttempts(
        LibraryRemediationJob job,
        IEnumerable<LibraryRemediationJob> relatedJobs)
    {
        var issueKey = NormalizeIssueKey(job);
        return relatedJobs
            .Where(x => x.Id != job.Id)
            .Where(x => x.LibraryItemId == job.LibraryItemId)
            .Where(x => string.Equals(x.RequestedAction, "search_replacement", StringComparison.OrdinalIgnoreCase))
            .Where(x => string.Equals(NormalizeIssueKey(x), issueKey, StringComparison.OrdinalIgnoreCase))
            .Where(x => x.RequestedAtUtc > job.RequestedAtUtc)
            .Count();
    }

    private static string BuildRepeatNotes(LibraryRemediationJob job)
    {
        var parts = new List<string>();

        var originalNotes = (job.Notes ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(originalNotes))
        {
            parts.Add($"Previous notes: {originalNotes}");
        }

        var verificationSummary = (job.VerificationSummary ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(verificationSummary))
        {
            parts.Add($"Verification: {verificationSummary}");
        }

        var loopbackSummary = (job.LoopbackSummary ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(loopbackSummary))
        {
            parts.Add($"Loopback: {loopbackSummary}");
        }

        return parts.Count == 0
            ? "Repeat remediation requested after operator review."
            : string.Join(" ", parts);
    }

    private static string NormalizeIssueKey(LibraryRemediationJob job)
        => FirstNonEmpty(job.IssueType, job.Reason, string.Empty).ToLowerInvariant();

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;
}
