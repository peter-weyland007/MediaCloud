namespace api.Models;

public class LibraryItemSourceLink
{
    public long Id { get; set; }
    public long LibraryItemId { get; set; }
    public long IntegrationId { get; set; }

    public string ExternalId { get; set; } = string.Empty;
    public string ExternalType { get; set; } = string.Empty;
    public DateTimeOffset? ExternalUpdatedAtUtc { get; set; }

    public DateTimeOffset LastSeenAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset FirstSeenAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public string SourcePayloadHash { get; set; } = string.Empty;
    public bool IsDeletedAtSource { get; set; }
}