namespace web.Components.Shared;

public record ApiErrorResponse(string Message);

public record SearchReplacementRequest(string Reason, string Notes, string? IssueType = null);

public record LibraryRemediationIntentDto(
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

public record LibraryItemRemediationResponse(
    long LibraryItemId,
    bool Success,
    string ServiceKey,
    string ServiceDisplayName,
    string CommandName,
    int? ExternalItemId,
    bool LookedUpRemotely,
    string Reason,
    string Notes,
    string Message,
    LibraryRemediationIntentDto? Intent = null);

public record LibraryRemediationJobDto(
    long Id,
    long LibraryItemId,
    long? LibraryIssueId,
    string ServiceKey,
    string ServiceDisplayName,
    string RequestedAction,
    string CommandName,
    int? ExternalItemId,
    string IssueType,
    string Reason,
    string Notes,
    string ReasonCategory,
    string Confidence,
    bool ShouldSearchNow,
    bool ShouldBlacklistCurrentRelease,
    bool NeedsManualReview,
    bool NotesRecordedOnly,
    bool LookedUpRemotely,
    string PolicySummary,
    string NotesHandling,
    string ProfileDecision,
    string ProfileSummary,
    string Status,
    string SearchStatus,
    string BlacklistStatus,
    string OutcomeSummary,
    string ResultMessage,
    string ReleaseSummary,
    string RequestedBy,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    DateTimeOffset? LastCheckedAtUtc);
