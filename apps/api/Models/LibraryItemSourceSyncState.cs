namespace api.Models;

public class LibraryItemSourceSyncState
{
    public long Id { get; set; }
    public long LibraryItemId { get; set; }
    public long IntegrationId { get; set; }
    public DateTimeOffset LastSyncedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}