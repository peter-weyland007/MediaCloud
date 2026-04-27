namespace api.Models;

public class WatchHistoryEntry
{
    public long Id { get; set; }
    public long LibraryItemId { get; set; }
    public long? IntegrationId { get; set; }
    public string SourceService { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
    public DateTimeOffset ImportedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? StoppedAtUtc { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string Player { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string Decision { get; set; } = string.Empty;
    public string TranscodeDecision { get; set; } = string.Empty;
    public string VideoDecision { get; set; } = string.Empty;
    public string AudioDecision { get; set; } = string.Empty;
    public string SubtitleDecision { get; set; } = string.Empty;
    public string Container { get; set; } = string.Empty;
    public string VideoCodec { get; set; } = string.Empty;
    public string AudioCodec { get; set; } = string.Empty;
    public string SubtitleCodec { get; set; } = string.Empty;
    public string QualityProfile { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public string RawPayloadJson { get; set; } = string.Empty;
}
