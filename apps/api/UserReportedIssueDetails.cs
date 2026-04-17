using System.Text.Json;

namespace api;

public sealed record UserReportedIssueDetails(
    string Notes,
    string AffectedClient,
    string AffectedDevice,
    string Origin,
    DateTimeOffset? FlaggedAtUtc,
    IReadOnlyList<string> AvailableAudioLanguages,
    IReadOnlyList<string> AvailableSubtitleLanguages);

public static class UserReportedIssueDetailsFactory
{
    public static UserReportedIssueDetails Create(
        string? notes,
        string? affectedClient,
        string? affectedDevice,
        string? origin,
        DateTimeOffset? flaggedAtUtc,
        IEnumerable<string>? availableAudioLanguages,
        IEnumerable<string>? availableSubtitleLanguages)
        => new(
            Normalize(notes),
            Normalize(affectedClient),
            Normalize(affectedDevice),
            Normalize(origin),
            flaggedAtUtc,
            NormalizeList(availableAudioLanguages),
            NormalizeList(availableSubtitleLanguages));

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static IReadOnlyList<string> NormalizeList(IEnumerable<string>? values)
        => (values ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}

public static class UserReportedIssueDetailsJson
{
    public static string Serialize(UserReportedIssueDetails details)
        => JsonSerializer.Serialize(details);

    public static UserReportedIssueDetails Parse(string? detailsJson)
    {
        if (string.IsNullOrWhiteSpace(detailsJson))
        {
            return UserReportedIssueDetailsFactory.Create(null, null, null, null, null, null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(detailsJson);
            var root = doc.RootElement;
            return UserReportedIssueDetailsFactory.Create(
                GetString(root, "notes", "Notes"),
                GetString(root, "affectedClient", "AffectedClient"),
                GetString(root, "affectedDevice", "AffectedDevice"),
                GetString(root, "origin", "Origin"),
                GetDateTimeOffset(root, "flaggedAtUtc", "FlaggedAtUtc"),
                GetStringArray(root, "availableAudioLanguages", "AvailableAudioLanguages"),
                GetStringArray(root, "availableSubtitleLanguages", "AvailableSubtitleLanguages"));
        }
        catch
        {
            return UserReportedIssueDetailsFactory.Create(null, null, null, null, null, null, null);
        }
    }

    private static string? GetString(JsonElement root, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static DateTimeOffset? GetDateTimeOffset(JsonElement root, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            if (DateTimeOffset.TryParse(value.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement root, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            return value
                .EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString() ?? string.Empty)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();
        }

        return [];
    }
}
