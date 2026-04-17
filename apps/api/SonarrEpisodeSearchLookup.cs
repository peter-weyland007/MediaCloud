using System.Text.Json;
using System.Text.RegularExpressions;
using api.Models;

namespace api;

public static class SonarrEpisodeSearchLookup
{
    private static readonly Regex EpisodeTitleRegex = new(@"\bS(?<season>\d{1,2})E(?<episode>\d{1,3})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CanonicalKeyRegex = new(@":s(?<season>\d{2}):e(?<episode>\d{2,3})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static int? SelectEpisodeId(LibraryItem item, IEnumerable<JsonElement> episodes)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));
        if (episodes is null) throw new ArgumentNullException(nameof(episodes));

        var episodeList = episodes.ToList();
        if (item.TvdbId.HasValue && item.TvdbId.Value > 0)
        {
            var tvdbMatch = episodeList.FirstOrDefault(x => GetJsonInt(x, "tvdbId") == item.TvdbId.Value);
            var tvdbMatchId = GetJsonInt(tvdbMatch, "id");
            if (tvdbMatchId.HasValue && tvdbMatchId.Value > 0)
            {
                return tvdbMatchId.Value;
            }
        }

        var identity = TryParseEpisodeIdentity(item);
        if (identity is null)
        {
            return null;
        }

        foreach (var episode in episodeList)
        {
            var seasonNumber = GetJsonInt(episode, "seasonNumber");
            var episodeNumber = GetJsonInt(episode, "episodeNumber");
            var episodeId = GetJsonInt(episode, "id");
            if (episodeId.HasValue
                && episodeId.Value > 0
                && seasonNumber == identity.Value.SeasonNumber
                && episodeNumber == identity.Value.EpisodeNumber)
            {
                return episodeId.Value;
            }
        }

        return null;
    }

    public static async Task<int?> TryLookupEpisodeIdAsync(LibraryItem item, IntegrationConfig integration, int seriesId, IHttpClientFactory httpClientFactory)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));
        if (integration is null) throw new ArgumentNullException(nameof(integration));
        if (httpClientFactory is null) throw new ArgumentNullException(nameof(httpClientFactory));
        if (seriesId <= 0)
        {
            return null;
        }

        try
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(20);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{integration.BaseUrl.TrimEnd('/')}/api/v3/episode?seriesId={seriesId}");
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

            return SelectEpisodeId(item, document.RootElement.EnumerateArray());
        }
        catch
        {
            return null;
        }
    }

    private static (int SeasonNumber, int EpisodeNumber)? TryParseEpisodeIdentity(LibraryItem item)
    {
        var titleMatch = EpisodeTitleRegex.Match(item.Title ?? string.Empty);
        if (titleMatch.Success
            && int.TryParse(titleMatch.Groups["season"].Value, out var titleSeason)
            && int.TryParse(titleMatch.Groups["episode"].Value, out var titleEpisode))
        {
            return (titleSeason, titleEpisode);
        }

        var canonicalMatch = CanonicalKeyRegex.Match(item.CanonicalKey ?? string.Empty);
        if (canonicalMatch.Success
            && int.TryParse(canonicalMatch.Groups["season"].Value, out var canonicalSeason)
            && int.TryParse(canonicalMatch.Groups["episode"].Value, out var canonicalEpisode))
        {
            return (canonicalSeason, canonicalEpisode);
        }

        return null;
    }

    private static int? GetJsonInt(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed) ? parsed : null;
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
}
