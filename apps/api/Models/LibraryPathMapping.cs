namespace api.Models;

public class LibraryPathMapping
{
    public long Id { get; set; }
    public long IntegrationId { get; set; }
    public string RemoteRootPath { get; set; } = string.Empty;
    public string LocalRootPath { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
