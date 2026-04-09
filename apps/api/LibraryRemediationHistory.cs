using api.Models;

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
    {
        var now = requestedAtUtc;
        var initialStatus = LibraryRemediationLifecycleTracker.DetermineInitialStatus(intent, result, blacklistSucceeded);
        return new LibraryRemediationJob
        {
            LibraryItemId = libraryItemId,
            LibraryIssueId = libraryIssueId,
            ServiceKey = result.ServiceKey,
            ServiceDisplayName = result.ServiceDisplayName,
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
