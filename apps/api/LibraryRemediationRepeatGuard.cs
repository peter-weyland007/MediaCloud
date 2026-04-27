using api.Models;

namespace api;

public sealed record LibraryRemediationRepeatDecision(
    bool Allowed,
    string Message,
    long? BlockingJobId,
    DateTimeOffset? CooldownEndsAtUtc);

public static class LibraryRemediationRepeatGuard
{
    public static readonly TimeSpan RetryCooldown = TimeSpan.FromMinutes(30);

    public static LibraryRemediationRepeatDecision Evaluate(
        LibraryRemediationJob job,
        IEnumerable<LibraryRemediationJob> relatedJobs,
        DateTimeOffset now)
    {
        var jobs = relatedJobs
            .Where(x => x.Id != job.Id)
            .Where(x => x.LibraryItemId == job.LibraryItemId)
            .Where(x => string.Equals(x.RequestedAction, "search_replacement", StringComparison.OrdinalIgnoreCase))
            .Where(x => string.Equals(NormalizeIssueKey(x), NormalizeIssueKey(job), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.RequestedAtUtc)
            .ToList();

        var activeDuplicate = jobs.FirstOrDefault(x => IsActiveRepeatStatus(x.Status));
        if (activeDuplicate is not null)
        {
            return new(
                false,
                $"A matching replacement remediation is already queued or still verifying (job {activeDuplicate.Id}). Wait for that run to finish before retrying.",
                activeDuplicate.Id,
                null);
        }

        var latestRelated = jobs.FirstOrDefault();
        if (latestRelated is not null)
        {
            var cooldownEndsAtUtc = latestRelated.RequestedAtUtc + RetryCooldown;
            if (cooldownEndsAtUtc > now)
            {
                return new(
                    false,
                    $"Repeat remediation is on cooldown until {cooldownEndsAtUtc:yyyy-MM-dd HH:mm} UTC to avoid rapid duplicate searches.",
                    latestRelated.Id,
                    cooldownEndsAtUtc);
            }
        }

        return new(true, string.Empty, null, null);
    }

    public static bool IsActiveRepeatStatus(string? status)
    {
        var normalized = (status ?? string.Empty).Trim();
        return normalized is "SearchQueued"
            or "BlacklistedAndQueued"
            or "Processing"
            or "ImportedReplacement";
    }

    private static string NormalizeIssueKey(LibraryRemediationJob job)
        => (job.IssueType ?? job.Reason ?? string.Empty).Trim().ToLowerInvariant();
}
