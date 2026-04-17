using System.Text.Json;
using api.Models;

public sealed record BazarrSourceLinkCandidate(string ServiceKey, string ExternalType, string ExternalId);
public sealed record BazarrSubtitleSyncTarget(string TargetKind, int ExternalId);
public sealed record BazarrSubtitleStatusDto(
    long LibraryItemId,
    bool Configured,
    bool HasMatch,
    string ServiceDisplayName,
    string InstanceName,
    string TargetKind,
    int? ExternalId,
    bool Monitored,
    IReadOnlyList<string> AvailableSubtitles,
    IReadOnlyList<string> MissingSubtitles,
    int MissingEpisodeCount,
    int EpisodeFileCount,
    string Status,
    string Summary,
    DateTimeOffset CheckedAtUtc);

public static class BazarrSubtitleStatusResolver
{
    public static BazarrSubtitleSyncTarget? ResolveTarget(LibraryItem item, IReadOnlyList<BazarrSourceLinkCandidate> sourceLinks)
    {
        var mediaType = (item.MediaType ?? string.Empty).Trim().ToLowerInvariant();
        return mediaType switch
        {
            "movie" => ResolveExternalId(sourceLinks, "radarr", "movie"),
            "episode" => ResolveExternalId(sourceLinks, "sonarr", "episode"),
            "series" => ResolveExternalId(sourceLinks, "sonarr", "series"),
            _ => null
        };
    }

    public static BazarrSubtitleStatusDto BuildUnavailable(long libraryItemId, bool configured, string summary)
        => new(
            libraryItemId,
            configured,
            false,
            "Bazarr",
            string.Empty,
            string.Empty,
            null,
            false,
            [],
            [],
            0,
            0,
            "Unavailable",
            summary,
            DateTimeOffset.UtcNow);

    public static BazarrSubtitleStatusDto BuildMovieStatus(long libraryItemId, string serviceDisplayName, string instanceName, int externalId, string payload)
        => BuildItemSubtitleStatus(libraryItemId, serviceDisplayName, instanceName, "movie", externalId, payload, "radarrId");

    public static BazarrSubtitleStatusDto BuildEpisodeStatus(long libraryItemId, string serviceDisplayName, string instanceName, int externalId, string payload)
        => BuildItemSubtitleStatus(libraryItemId, serviceDisplayName, instanceName, "episode", externalId, payload, "sonarrEpisodeId");

    public static BazarrSubtitleStatusDto BuildSeriesStatus(long libraryItemId, string serviceDisplayName, string instanceName, int externalId, string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var item = FindMatchingArrayItem(document.RootElement, "data", "sonarrSeriesId", externalId);
        if (item is null)
        {
            return new BazarrSubtitleStatusDto(
                libraryItemId,
                true,
                false,
                serviceDisplayName,
                instanceName,
                "series",
                externalId,
                false,
                [],
                [],
                0,
                0,
                "Unavailable",
                "Bazarr did not return a matching series record for this item.",
                DateTimeOffset.UtcNow);
        }

        var monitored = ReadBool(item.Value, "monitored");
        var missingEpisodeCount = ReadInt(item.Value, "episodeMissingCount");
        var episodeFileCount = ReadInt(item.Value, "episodeFileCount");
        var status = missingEpisodeCount > 0 ? "Missing" : "Covered";
        var summary = missingEpisodeCount > 0
            ? $"Bazarr reports subtitle gaps in {missingEpisodeCount} episodes out of {episodeFileCount} tracked episodes."
            : $"Bazarr reports no missing subtitle targets across {episodeFileCount} tracked episodes.";

        return new BazarrSubtitleStatusDto(
            libraryItemId,
            true,
            true,
            serviceDisplayName,
            instanceName,
            "series",
            externalId,
            monitored,
            [],
            [],
            missingEpisodeCount,
            episodeFileCount,
            status,
            summary,
            DateTimeOffset.UtcNow);
    }

    private static BazarrSubtitleSyncTarget? ResolveExternalId(IReadOnlyList<BazarrSourceLinkCandidate> sourceLinks, string serviceKey, string externalType)
    {
        var match = sourceLinks.FirstOrDefault(link =>
            string.Equals(link.ServiceKey, serviceKey, StringComparison.OrdinalIgnoreCase)
            && string.Equals(link.ExternalType, externalType, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(link.ExternalId, out _));

        return match is not null && int.TryParse(match.ExternalId, out var externalId)
            ? new BazarrSubtitleSyncTarget(externalType, externalId)
            : null;
    }

    private static BazarrSubtitleStatusDto BuildItemSubtitleStatus(long libraryItemId, string serviceDisplayName, string instanceName, string targetKind, int externalId, string payload, string idProperty)
    {
        using var document = JsonDocument.Parse(payload);
        var item = FindMatchingArrayItem(document.RootElement, "data", idProperty, externalId);
        if (item is null)
        {
            return new BazarrSubtitleStatusDto(
                libraryItemId,
                true,
                false,
                serviceDisplayName,
                instanceName,
                targetKind,
                externalId,
                false,
                [],
                [],
                0,
                0,
                "Unavailable",
                "Bazarr did not return a matching subtitle record for this item.",
                DateTimeOffset.UtcNow);
        }

        var monitored = ReadBool(item.Value, "monitored");
        var availableSubtitles = ReadSubtitleLabels(item.Value, "subtitles");
        var missingSubtitles = ReadSubtitleLabels(item.Value, "missing_subtitles");
        var status = missingSubtitles.Count > 0 ? "Missing" : availableSubtitles.Count > 0 ? "Covered" : "Unavailable";
        var summary = missingSubtitles.Count > 0
            ? $"Bazarr reports {availableSubtitles.Count} available subtitle tracks and {missingSubtitles.Count} missing subtitle target{(missingSubtitles.Count == 1 ? string.Empty : "s")}."
            : availableSubtitles.Count > 0
                ? $"Bazarr reports {availableSubtitles.Count} available subtitle track{(availableSubtitles.Count == 1 ? string.Empty : "s")} and no missing targets."
                : "Bazarr has no subtitle tracks or missing-target signals recorded for this item yet.";

        return new BazarrSubtitleStatusDto(
            libraryItemId,
            true,
            true,
            serviceDisplayName,
            instanceName,
            targetKind,
            externalId,
            monitored,
            availableSubtitles,
            missingSubtitles,
            0,
            0,
            status,
            summary,
            DateTimeOffset.UtcNow);
    }

    private static JsonElement? FindMatchingArrayItem(JsonElement root, string arrayProperty, string idProperty, int expectedId)
    {
        if (!root.TryGetProperty(arrayProperty, out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in data.EnumerateArray())
        {
            if (ReadInt(item, idProperty) == expectedId)
            {
                return item;
            }
        }

        return null;
    }

    private static List<string> ReadSubtitleLabels(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property.EnumerateArray()
            .Select(FormatSubtitleLabel)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string FormatSubtitleLabel(JsonElement element)
    {
        var name = ReadString(element, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var suffixes = new List<string>();
        if (ReadBool(element, "forced"))
        {
            suffixes.Add("forced");
        }

        if (ReadBool(element, "hi"))
        {
            suffixes.Add("HI");
        }

        return suffixes.Count == 0 ? name : $"{name} ({string.Join(", ", suffixes)})";
    }

    private static string ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()?.Trim() ?? string.Empty
            : string.Empty;

    private static int ReadInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out var numeric) => numeric,
            JsonValueKind.String when int.TryParse(property.GetString(), out var parsed) => parsed,
            _ => 0
        };
    }

    private static bool ReadBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(property.GetString(), out var parsed) => parsed,
            _ => false
        };
    }
}
