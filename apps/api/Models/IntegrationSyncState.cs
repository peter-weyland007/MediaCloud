namespace api.Models;

public class IntegrationSyncState
{
    public long Id { get; set; }
    public long IntegrationId { get; set; }
    public DateTimeOffset? LastAttemptedAtUtc { get; set; }
    public DateTimeOffset? LastSuccessfulAtUtc { get; set; }
    public string LastCursor { get; set; } = string.Empty;
    public string LastEtag { get; set; } = string.Empty;
    public string LastError { get; set; } = string.Empty;
    public int ConsecutiveFailureCount { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}