namespace api.Models;

public class LibraryRemediationJob
{
    public long Id { get; set; }
    public long LibraryItemId { get; set; }
    public long? LibraryIssueId { get; set; }
    public string ServiceKey { get; set; } = string.Empty;
    public string ServiceDisplayName { get; set; } = string.Empty;
    public long? IntegrationId { get; set; }
    public string RequestedAction { get; set; } = string.Empty;
    public string CommandName { get; set; } = string.Empty;
    public int? ExternalItemId { get; set; }
    public string IssueType { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string ReasonCategory { get; set; } = string.Empty;
    public string Confidence { get; set; } = string.Empty;
    public bool ShouldSearchNow { get; set; }
    public bool ShouldBlacklistCurrentRelease { get; set; }
    public bool NeedsManualReview { get; set; }
    public bool NotesRecordedOnly { get; set; }
    public bool LookedUpRemotely { get; set; }
    public string PolicySummary { get; set; } = string.Empty;
    public string NotesHandling { get; set; } = string.Empty;
    public string ProfileDecision { get; set; } = string.Empty;
    public string ProfileSummary { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string SearchStatus { get; set; } = string.Empty;
    public string BlacklistStatus { get; set; } = string.Empty;
    public string OutcomeSummary { get; set; } = string.Empty;
    public string ResultMessage { get; set; } = string.Empty;
    public int? ProviderCommandId { get; set; }
    public string ProviderCommandStatus { get; set; } = string.Empty;
    public string ProviderCommandSummary { get; set; } = string.Empty;
    public string DownloadType { get; set; } = string.Empty;
    public DateTimeOffset? ProviderCommandCheckedAtUtc { get; set; }
    public string VerificationStatus { get; set; } = string.Empty;
    public string VerificationSummary { get; set; } = string.Empty;
    public string VerificationDetailsJson { get; set; } = string.Empty;
    public DateTimeOffset? VerificationCheckedAtUtc { get; set; }
    public string LoopbackStatus { get; set; } = string.Empty;
    public string LoopbackSummary { get; set; } = string.Empty;
    public string ReleaseSummary { get; set; } = string.Empty;
    public string OperatorReviewStatus { get; set; } = string.Empty;
    public string OperatorReviewSummary { get; set; } = string.Empty;
    public string OperatorReviewedBy { get; set; } = string.Empty;
    public DateTimeOffset? OperatorReviewedAtUtc { get; set; }
    public string ReleaseContextJson { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public DateTimeOffset RequestedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAtUtc { get; set; }
    public DateTimeOffset? LastCheckedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
