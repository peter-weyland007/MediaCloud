using System.Text.Json;
using api.Data;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api;

public static class LibraryRemediationJobFactory
{
    public static LibraryRemediationJob Create(
        long libraryItemId,
        long? libraryIssueId,
        LibraryRemediationIntent intent,
        LibraryItemRemediationResponse result,
        LibraryRemediationReleaseContext? releaseContext,
        string requestedBy,
        DateTimeOffset requestedAtUtc,
        bool? blacklistSucceeded)
        => Create(libraryItemId, libraryIssueId, null, intent, result, releaseContext, requestedBy, requestedAtUtc, blacklistSucceeded);

    public static LibraryRemediationJob Create(
        long libraryItemId,
        long? libraryIssueId,
        long? integrationId,
        LibraryRemediationIntent intent,
        LibraryItemRemediationResponse result,
        LibraryRemediationReleaseContext? releaseContext,
        string requestedBy,
        DateTimeOffset requestedAtUtc,
        bool? blacklistSucceeded)
    {
        var now = requestedAtUtc;
        var initialStatus = LibraryRemediationLifecycleTracker.DetermineInitialStatus(intent, result, blacklistSucceeded);
        return new LibraryRemediationJob
        {
            LibraryItemId = libraryItemId,
            LibraryIssueId = libraryIssueId,
            ServiceKey = result.ServiceKey,
            ServiceDisplayName = result.ServiceDisplayName,
            IntegrationId = integrationId,
            RequestedAction = intent.RequestedAction,
            CommandName = result.CommandName,
            ExternalItemId = result.ExternalItemId,
            IssueType = intent.IssueType,
            Reason = result.Reason,
            Notes = result.Notes,
            ReasonCategory = intent.ReasonCategory,
            Confidence = intent.Confidence,
            ShouldSearchNow = intent.ShouldSearchNow,
            ShouldBlacklistCurrentRelease = intent.ShouldBlacklistCurrentRelease,
            NeedsManualReview = intent.NeedsManualReview,
            NotesRecordedOnly = intent.NotesRecordedOnly,
            LookedUpRemotely = result.LookedUpRemotely,
            PolicySummary = intent.PolicySummary,
            NotesHandling = intent.NotesHandling,
            ProfileDecision = intent.ProfileDecision,
            ProfileSummary = intent.ProfileSummary,
            Status = initialStatus,
            SearchStatus = LibraryRemediationLifecycleTracker.DetermineInitialSearchStatus(intent, result),
            BlacklistStatus = LibraryRemediationLifecycleTracker.DetermineInitialBlacklistStatus(intent, blacklistSucceeded),
            OutcomeSummary = LibraryRemediationLifecycleTracker.DetermineInitialOutcomeSummary(intent, result, blacklistSucceeded),
            ResultMessage = result.Message,
            ProviderCommandId = result.ProviderCommandId,
            ProviderCommandStatus = result.ProviderCommandStatus,
            ProviderCommandSummary = result.ProviderCommandSummary,
            ProviderCommandCheckedAtUtc = result.ProviderCommandId.HasValue ? requestedAtUtc : null,
            VerificationStatus = LibraryRemediationLifecycleTracker.DetermineInitialVerificationStatus(intent, result),
            VerificationSummary = LibraryRemediationLifecycleTracker.DetermineInitialVerificationSummary(intent, result),
            VerificationDetailsJson = LibraryRemediationLifecycleTracker.BuildInitialVerificationDetailsJson(intent, result),
            VerificationCheckedAtUtc = LibraryRemediationLifecycleTracker.DetermineInitialVerificationCheckedAtUtc(intent, result, requestedAtUtc),
            LoopbackStatus = LibraryRemediationLifecycleTracker.DetermineInitialLoopbackStatus(intent, result),
            LoopbackSummary = LibraryRemediationLifecycleTracker.DetermineInitialLoopbackSummary(intent, result),
            ReleaseSummary = releaseContext?.ReleaseSummary ?? string.Empty,
            ReleaseContextJson = releaseContext?.RawContextJson ?? string.Empty,
            RequestedBy = string.IsNullOrWhiteSpace(requestedBy) ? "admin" : requestedBy.Trim(),
            RequestedAtUtc = requestedAtUtc,
            FinishedAtUtc = LibraryRemediationLifecycleTracker.IsTerminalStatus(initialStatus) ? requestedAtUtc : null,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }
}

public static class LibraryRemediationJobLiveState
{
    public static bool Refresh(
        LibraryRemediationJob job,
        LibraryItem item,
        LibraryIssue? relatedIssue,
        string? currentSourceTitle,
        DateTimeOffset checkedAtUtc)
    {
        var latestContext = LibraryRemediationReleaseAwareness.BuildContext(
            job.ServiceKey,
            job.ExternalItemId,
            item.PrimaryFilePath,
            item.QualityProfile,
            currentSourceTitle,
            []);

        var beforeStatus = job.Status;
        var beforeSearchStatus = job.SearchStatus;
        var beforeBlacklistStatus = job.BlacklistStatus;
        var beforeOutcome = job.OutcomeSummary;
        var beforeVerificationStatus = job.VerificationStatus;
        var beforeVerificationSummary = job.VerificationSummary;
        var beforeVerificationCheckedAt = job.VerificationCheckedAtUtc;
        var beforeLoopbackStatus = job.LoopbackStatus;
        var beforeLoopbackSummary = job.LoopbackSummary;
        var beforeFinishedAt = job.FinishedAtUtc;
        var beforeIssueStatus = relatedIssue?.Status;
        var beforeIssueResolvedAt = relatedIssue?.ResolvedAtUtc;
        var snapshot = LibraryRemediationLifecycleTracker.Evaluate(job, item, relatedIssue, latestContext) with
        {
            LastCheckedAtUtc = checkedAtUtc,
            VerificationCheckedAtUtc = checkedAtUtc
        };

        LibraryRemediationLifecycleTracker.Apply(job, snapshot);
        if (relatedIssue is not null)
        {
            if (string.Equals(job.VerificationStatus, "Verified", StringComparison.OrdinalIgnoreCase))
            {
                relatedIssue.Status = "Resolved";
                relatedIssue.ResolvedAtUtc = checkedAtUtc;
                relatedIssue.LastDetectedAtUtc = checkedAtUtc;
            }
            else if (string.Equals(job.VerificationStatus, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                relatedIssue.Status = "Open";
                relatedIssue.ResolvedAtUtc = null;
                relatedIssue.LastDetectedAtUtc = checkedAtUtc;
            }
        }

        if (LibraryRemediationLifecycleTracker.IsTerminalStatus(job.Status))
        {
            job.FinishedAtUtc ??= checkedAtUtc;
        }

        return !string.Equals(beforeStatus, job.Status, StringComparison.Ordinal)
            || !string.Equals(beforeSearchStatus, job.SearchStatus, StringComparison.Ordinal)
            || !string.Equals(beforeBlacklistStatus, job.BlacklistStatus, StringComparison.Ordinal)
            || !string.Equals(beforeOutcome, job.OutcomeSummary, StringComparison.Ordinal)
            || !string.Equals(beforeVerificationStatus, job.VerificationStatus, StringComparison.Ordinal)
            || !string.Equals(beforeVerificationSummary, job.VerificationSummary, StringComparison.Ordinal)
            || beforeVerificationCheckedAt != job.VerificationCheckedAtUtc
            || !string.Equals(beforeLoopbackStatus, job.LoopbackStatus, StringComparison.Ordinal)
            || !string.Equals(beforeLoopbackSummary, job.LoopbackSummary, StringComparison.Ordinal)
            || beforeFinishedAt != job.FinishedAtUtc
            || !string.Equals(beforeIssueStatus, relatedIssue?.Status, StringComparison.Ordinal)
            || beforeIssueResolvedAt != relatedIssue?.ResolvedAtUtc
            || job.LastCheckedAtUtc != checkedAtUtc;
    }
}

public sealed record LibraryRemediationRecommendation(
    long LibraryItemId,
    bool HasRecommendation,
    string IssueType,
    string RequestedAction,
    string Confidence,
    bool ShouldSearchNow,
    bool ShouldBlacklistCurrentRelease,
    bool NeedsManualReview,
    bool ApprovalRequired,
    string ApprovalReason,
    string PolicySummary,
    string ProfileSummary,
    string PolicyState,
    string NextActionSummary,
    string EvidenceSummary);

public static class LibraryRemediationRecommendationEngine
{
    public static LibraryRemediationRecommendation Build(
        LibraryItem item,
        LibraryIssue? latestIssue,
        PlaybackDiagnosticEntry? latestDiagnostic)
    {
        var issueType = DetermineIssueType(latestIssue, latestDiagnostic);
        if (string.IsNullOrWhiteSpace(issueType))
        {
            return new LibraryRemediationRecommendation(
                item.Id,
                false,
                string.Empty,
                "observe",
                "low",
                false,
                false,
                false,
                false,
                "No file-changing remediation is recommended yet.",
                "MediaCloud does not have enough issue or playback evidence yet to recommend remediation.",
                string.Empty,
                "insufficient_evidence",
                "Pull playback diagnostics or log an issue first so MediaCloud has enough context to recommend a next step.",
                "No open remediation issue or playback diagnostic evidence was available.");
        }

        var intent = LibraryRemediationPlanner.BuildIntent(issueType, latestIssue?.Summary ?? latestDiagnostic?.Summary);
        intent = LibraryRemediationDiagnosticsDecisioning.ApplyLatestDiagnostic(intent, latestDiagnostic);
        intent = LibraryRemediationProfileDecisioning.Apply(intent, item);

        return new LibraryRemediationRecommendation(
            item.Id,
            true,
            intent.IssueType,
            intent.RequestedAction,
            intent.Confidence,
            intent.ShouldSearchNow,
            intent.ShouldBlacklistCurrentRelease,
            intent.NeedsManualReview,
            intent.ApprovalRequired,
            intent.ApprovalReason,
            intent.PolicySummary,
            intent.ProfileSummary,
            intent.PolicyState,
            intent.NextActionSummary,
            BuildEvidenceSummary(latestIssue, latestDiagnostic));
    }

    public static string DetermineIssueType(LibraryIssue? latestIssue, PlaybackDiagnosticEntry? latestDiagnostic)
    {
        if (latestIssue is not null && string.Equals(latestIssue.Status, "Open", StringComparison.OrdinalIgnoreCase))
        {
            return latestIssue.IssueType;
        }

        if (latestDiagnostic is null)
        {
            return string.Empty;
        }

        if (string.Equals(latestDiagnostic.HealthLabel, "Healthy", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var combined = $"{latestDiagnostic.SuspectedCause} {latestDiagnostic.ErrorMessage} {latestDiagnostic.Summary}";
        return LooksLikeBadMediaFailure(combined)
            ? "corrupt_file"
            : "playback_failure";
    }

    public static string BuildEvidenceSummary(LibraryIssue? latestIssue, PlaybackDiagnosticEntry? latestDiagnostic)
    {
        if (latestIssue is not null && latestDiagnostic is not null)
        {
            return $"Issue '{latestIssue.IssueType}' is open and the latest playback diagnostic says: {FirstNonEmpty(latestDiagnostic.SuspectedCause, latestDiagnostic.Summary, latestDiagnostic.ErrorMessage)}";
        }

        if (latestIssue is not null)
        {
            return $"Issue '{latestIssue.IssueType}' is currently open for this item.";
        }

        if (latestDiagnostic is not null)
        {
            return $"Latest playback diagnostic says: {FirstNonEmpty(latestDiagnostic.SuspectedCause, latestDiagnostic.Summary, latestDiagnostic.ErrorMessage)}";
        }

        return "No remediation evidence available.";
    }

    private static bool LooksLikeBadMediaFailure(string? text)
    {
        var combined = (text ?? string.Empty).Trim().ToLowerInvariant();
        return combined.Contains("corrupt")
            || combined.Contains("input/output error")
            || combined.Contains("i/o error")
            || combined.Contains("crc")
            || combined.Contains("read error")
            || combined.Contains("broken file")
            || combined.Contains("damaged");
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;
}

public static class PlaybackDiagnosticIssueAutomation
{
    private static readonly string[] ManagedIssueTypes = ["playback_failure", "corrupt_file"];

    public static async Task UpsertFromPlaybackDiagnosticsAsync(
        MediaCloudDbContext db,
        LibraryItem item,
        PlaybackDiagnosticEntry? latestDiagnostic,
        DateTimeOffset detectedAtUtc)
    {
        var targetIssueType = LibraryRemediationRecommendationEngine.DetermineIssueType(null, latestDiagnostic);
        var managedIssues = await db.LibraryIssues
            .Where(x => x.LibraryItemId == item.Id && x.PolicyVersion == "playback-diagnostics-v1" && ManagedIssueTypes.Contains(x.IssueType))
            .OrderByDescending(x => x.Id)
            .ToListAsync();

        if (string.IsNullOrWhiteSpace(targetIssueType))
        {
            foreach (var existing in managedIssues.Where(x => !string.Equals(x.Status, "Resolved", StringComparison.OrdinalIgnoreCase)))
            {
                existing.Status = "Resolved";
                existing.ResolvedAtUtc = detectedAtUtc;
                existing.LastDetectedAtUtc = detectedAtUtc;
            }

            return;
        }

        foreach (var sibling in managedIssues.Where(x => !string.Equals(x.IssueType, targetIssueType, StringComparison.OrdinalIgnoreCase)
                                                        && !string.Equals(x.Status, "Resolved", StringComparison.OrdinalIgnoreCase)))
        {
            sibling.Status = "Resolved";
            sibling.ResolvedAtUtc = detectedAtUtc;
            sibling.LastDetectedAtUtc = detectedAtUtc;
        }

        var issue = managedIssues.FirstOrDefault(x => string.Equals(x.IssueType, targetIssueType, StringComparison.OrdinalIgnoreCase));
        if (issue is null)
        {
            issue = new LibraryIssue
            {
                LibraryItemId = item.Id,
                IssueType = targetIssueType,
                FirstDetectedAtUtc = detectedAtUtc
            };
            db.LibraryIssues.Add(issue);
        }

        var recommendation = LibraryRemediationRecommendationEngine.Build(item, null, latestDiagnostic);
        issue.PolicyVersion = "playback-diagnostics-v1";
        issue.Status = "Open";
        issue.ResolvedAtUtc = null;
        issue.LastDetectedAtUtc = detectedAtUtc;
        issue.Severity = string.Equals(targetIssueType, "corrupt_file", StringComparison.OrdinalIgnoreCase) ? "High" : "Warning";
        issue.Summary = string.Equals(targetIssueType, "corrupt_file", StringComparison.OrdinalIgnoreCase)
            ? "Playback diagnostics suggest the current file is damaged or unreadable."
            : "Playback diagnostics show a recurring playback failure that needs follow-up.";
        issue.SuggestedAction = recommendation.NextActionSummary.Length > 256
            ? recommendation.NextActionSummary[..256]
            : recommendation.NextActionSummary;
        issue.DetailsJson = JsonSerializer.Serialize(new
        {
            source = "playback-diagnostics",
            policyState = recommendation.PolicyState,
            evidenceSummary = recommendation.EvidenceSummary,
            diagnostic = latestDiagnostic is null ? null : new
            {
                latestDiagnostic.HealthLabel,
                latestDiagnostic.Summary,
                latestDiagnostic.SuspectedCause,
                latestDiagnostic.ErrorMessage,
                latestDiagnostic.OccurredAtUtc,
                latestDiagnostic.SourceService,
                latestDiagnostic.ClientName,
                latestDiagnostic.Player
            }
        });
    }
}
