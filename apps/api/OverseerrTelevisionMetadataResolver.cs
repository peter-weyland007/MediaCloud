using System.Text.Json;

namespace api;

public sealed record OverseerrTelevisionMetadata(
    string Title,
    string SortTitle,
    int? Year,
    int? TmdbId,
    int? TvdbId,
    string ImdbId,
    string Overview);

public static class OverseerrTelevisionMetadataResolver
{
    public static OverseerrTelevisionMetadata Resolve(JsonElement requestRow, JsonElement? detail = null)
    {
        var media = requestRow.TryGetProperty("media", out var mediaElement) && mediaElement.ValueKind == JsonValueKind.Object
            ? mediaElement
            : default;

        var title = FirstNonEmpty(
                GetJsonString(media, "title"),
                GetJsonString(media, "name"),
                GetJsonString(requestRow, "subject"),
                GetJsonString(detail, "name"),
                GetJsonString(detail, "originalName"))
            ?? "Unknown";

        var overview = FirstNonEmpty(
                GetJsonString(media, "overview"),
                GetJsonString(detail, "overview"))
            ?? string.Empty;

        var tmdbId = GetJsonInt(media, "tmdbId")
            ?? GetJsonInt(detail, "id")
            ?? GetJsonInt(detail, "tmdbId");

        var tvdbId = GetJsonInt(media, "tvdbId")
            ?? GetJsonInt(detail, "tvdbId")
            ?? GetNestedJsonInt(detail, "externalIds", "tvdbId");

        var imdbId = FirstNonEmpty(
                GetJsonString(media, "imdbId"),
                GetJsonString(detail, "imdbId"),
                GetNestedJsonString(detail, "externalIds", "imdbId"))
            ?? string.Empty;

        var year = ParseYearFromDate(
            FirstNonEmpty(
                GetJsonString(media, "firstAirDate"),
                GetJsonString(media, "releaseDate"),
                GetJsonString(detail, "firstAirDate"),
                GetJsonString(detail, "releaseDate")));

        return new OverseerrTelevisionMetadata(title, title, year, tmdbId, tvdbId, imdbId, overview);
    }

    public static bool NeedsDetailLookup(OverseerrTelevisionMetadata metadata)
        => string.IsNullOrWhiteSpace(metadata.Title)
            || string.Equals(metadata.Title, "Unknown", StringComparison.OrdinalIgnoreCase)
            || !metadata.Year.HasValue
            || string.IsNullOrWhiteSpace(metadata.Overview);

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string? GetJsonString(JsonElement? element, string property)
        => element.HasValue && element.Value.ValueKind == JsonValueKind.Object
            ? JsonPropertyHelpers.GetPropertyString(element.Value, property)
            : null;

    private static string? GetNestedJsonString(JsonElement? element, string parentProperty, string childProperty)
    {
        if (!element.HasValue || element.Value.ValueKind != JsonValueKind.Object) return null;
        if (!element.Value.TryGetProperty(parentProperty, out var parent) || parent.ValueKind != JsonValueKind.Object) return null;
        return JsonPropertyHelpers.GetPropertyString(parent, childProperty);
    }

    private static int? GetJsonInt(JsonElement? element, string property)
    {
        var value = GetJsonString(element, property);
        return int.TryParse(value, out var parsed) ? parsed : null;
    }

    private static int? GetNestedJsonInt(JsonElement? element, string parentProperty, string childProperty)
    {
        var value = GetNestedJsonString(element, parentProperty, childProperty);
        return int.TryParse(value, out var parsed) ? parsed : null;
    }

    private static int? ParseYearFromDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value!.Length < 4) return null;
        return int.TryParse(value[..4], out var parsed) ? parsed : null;
    }
}
