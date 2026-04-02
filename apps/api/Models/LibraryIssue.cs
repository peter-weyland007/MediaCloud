namespace api.Models;

public class LibraryIssue
{
    public long Id { get; set; }
    public long LibraryItemId { get; set; }

    public string IssueType { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Status { get; set; } = "Open";
    public string PolicyVersion { get; set; } = "v1";

    public string Summary { get; set; } = string.Empty;
    public string DetailsJson { get; set; } = "{}";
    public string SuggestedAction { get; set; } = string.Empty;

    public DateTimeOffset FirstDetectedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastDetectedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAtUtc { get; set; }
}