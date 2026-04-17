using System.Text.Json;
using api.Models;

namespace api;

public static class LidarrAlbumSearchLookup
{
    public static int? SelectAlbumId(LibraryItem item, IEnumerable<JsonElement> albums)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));
        if (albums is null) throw new ArgumentNullException(nameof(albums));

        var albumList = albums.ToList();
        var candidateTitles = BuildCandidateTitles(item);
        if (candidateTitles.Count == 0)
        {
            return null;
        }

        var candidates = albumList
            .Select(album => new AlbumMatchCandidate(
                Id: GetJsonInt(album, "id"),
                Title: NormalizeTitle(GetJsonString(album, "title")),
                SortTitle: NormalizeTitle(GetJsonString(album, "sortTitle")),
                Year: ParseYear(GetJsonString(album, "releaseDate"))))
            .Where(x => x.Id.HasValue)
            .ToList();

        if (item.Year.HasValue)
        {
            var exactYearMatch = candidates.FirstOrDefault(x => x.Year == item.Year.Value && MatchesAnyTitle(x, candidateTitles));
            if (exactYearMatch is not null)
            {
                return exactYearMatch.Id;
            }
        }

        var titleOnlyMatch = candidates.FirstOrDefault(x => MatchesAnyTitle(x, candidateTitles));
        return titleOnlyMatch?.Id;
    }

    public static async Task<int?> TryLookupAlbumIdAsync(LibraryItem item, IntegrationConfig integration, int artistId, IHttpClientFactory httpClientFactory)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));
        if (integration is null) throw new ArgumentNullException(nameof(integration));
        if (httpClientFactory is null) throw new ArgumentNullException(nameof(httpClientFactory));
        if (artistId <= 0)
        {
            return null;
        }

        try
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(20);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{integration.BaseUrl.TrimEnd('/')}/api/v1/album?artistId={artistId}");
            ApplyIntegrationAuthHeaders(integration, request);
            using var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            return SelectAlbumId(item, document.RootElement.EnumerateArray());
        }
        catch
        {
            return null;
        }
    }

    private static bool MatchesAnyTitle(AlbumMatchCandidate album, IReadOnlyCollection<string> candidateTitles)
        => candidateTitles.Contains(album.Title) || candidateTitles.Contains(album.SortTitle);

    private static List<string> BuildCandidateTitles(LibraryItem item)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);
        AddCandidate(values, item.Title);
        AddCandidate(values, item.SortTitle);
        AddTrailingSegment(values, item.Title);
        AddTrailingSegment(values, item.SortTitle);
        return values.ToList();
    }

    private static void AddTrailingSegment(HashSet<string> values, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        foreach (var separator in new[] { " — ", " - ", " – ", ": " })
        {
            var idx = raw.LastIndexOf(separator, StringComparison.Ordinal);
            if (idx >= 0 && idx + separator.Length < raw.Length)
            {
                AddCandidate(values, raw[(idx + separator.Length)..]);
            }
        }
    }

    private static void AddCandidate(HashSet<string> values, string? raw)
    {
        var normalized = NormalizeTitle(raw);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            values.Add(normalized);
        }
    }

    private static string NormalizeTitle(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var chars = raw.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray();
        return new string(chars);
    }

    private static int? ParseYear(string? releaseDate)
    {
        if (string.IsNullOrWhiteSpace(releaseDate))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(releaseDate, out var parsed))
        {
            return parsed.Year;
        }

        return releaseDate.Length >= 4 && int.TryParse(releaseDate[..4], out var year) ? year : null;
    }

    private static int? GetJsonInt(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed) ? parsed : null;
    }

    private static string? GetJsonString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.GetString();
    }

    private static void ApplyIntegrationAuthHeaders(IntegrationConfig integration, HttpRequestMessage request)
    {
        if (string.Equals(integration.AuthType, "ApiKey", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Add("X-Api-Key", integration.ApiKey);
        }
        else if (string.Equals(integration.AuthType, "Basic", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes($"{integration.Username}:{integration.Password}");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(bytes));
        }
    }

    private sealed record AlbumMatchCandidate(int? Id, string Title, string SortTitle, int? Year);
}
