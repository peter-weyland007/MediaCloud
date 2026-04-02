namespace api.Models;

public class LibraryItem
{
    public long Id { get; set; }
    public string CanonicalKey { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string SortTitle { get; set; } = string.Empty;
    public int? Year { get; set; }

    public int? TmdbId { get; set; }
    public int? TvdbId { get; set; }
    public string ImdbId { get; set; } = string.Empty;
    public string PlexRatingKey { get; set; } = string.Empty;

    public double? RuntimeMinutes { get; set; }
    public double? ActualRuntimeMinutes { get; set; }
    public string PrimaryFilePath { get; set; } = string.Empty;
    public string AudioLanguagesJson { get; set; } = "[]";
    public string SubtitleLanguagesJson { get; set; } = "[]";

    public bool IsAvailable { get; set; }
    public string QualityProfile { get; set; } = string.Empty;
    public DateTimeOffset? SourceUpdatedAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}