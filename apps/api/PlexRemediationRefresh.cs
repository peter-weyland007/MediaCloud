using System.Xml.Linq;
using api.Data;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api;

public sealed record PlexMetadataRefreshResult(bool Attempted, bool Success, string Message);

public static class PlexRemediationRefresh
{
    public static string BuildMetadataRefreshUrl(string baseUrl, string ratingKey)
        => $"{baseUrl.TrimEnd('/')}/library/metadata/{Uri.EscapeDataString(ratingKey)}/refresh?force=1";

    public static string BuildMetadataDetailsUrl(string baseUrl, string ratingKey)
        => $"{baseUrl.TrimEnd('/')}/library/metadata/{Uri.EscapeDataString(ratingKey)}";

    public static string BuildSectionRefreshUrl(string baseUrl, string sectionKey, string folderPath)
        => $"{baseUrl.TrimEnd('/')}/library/sections/{Uri.EscapeDataString(sectionKey)}/refresh?path={Uri.EscapeDataString(folderPath)}";

    public static async Task<PlexMetadataRefreshResult> TryRefreshLibraryItemAsync(long libraryItemId, string? remediatedOutputPath, MediaCloudDbContext db, IHttpClientFactory httpClientFactory)
    {
        var item = await db.LibraryItems.FirstOrDefaultAsync(x => x.Id == libraryItemId);
        if (item is null)
        {
            return new PlexMetadataRefreshResult(false, false, "Plex refresh skipped: MediaCloud could not reload the library item after remediation.");
        }

        var plex = await db.IntegrationConfigs
            .Where(x => x.Enabled && x.ServiceKey == "plex")
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync();
        if (plex is null)
        {
            return new PlexMetadataRefreshResult(false, false, "Plex refresh skipped: no enabled Plex integration is configured in MediaCloud.");
        }

        try
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(20);
            return await RefreshMetadataAsync(item, plex, client, remediatedOutputPath);
        }
        catch (Exception ex)
        {
            return new PlexMetadataRefreshResult(true, false, $"Plex refresh failed unexpectedly: {ex.Message}");
        }
    }

    public static async Task<PlexMetadataRefreshResult> RefreshMetadataAsync(LibraryItem item, IntegrationConfig plex, HttpClient client, string? remediatedOutputPath)
    {
        var ratingKey = (item.PlexRatingKey ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(ratingKey))
        {
            return new PlexMetadataRefreshResult(false, false, "Plex refresh skipped: this item has no Plex rating key.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Put, BuildMetadataRefreshUrl(plex.BaseUrl, ratingKey));
        ApplyPlexAuth(plex, request);
        using var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var body = (await response.Content.ReadAsStringAsync()).Trim();
            var message = string.IsNullOrWhiteSpace(body)
                ? $"Plex refresh failed (HTTP {(int)response.StatusCode})."
                : $"Plex refresh failed: {body}";
            return new PlexMetadataRefreshResult(true, false, message);
        }

        var folderPath = GetFolderPath(remediatedOutputPath);
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return new PlexMetadataRefreshResult(true, true, "Requested Plex metadata refresh for this movie.");
        }

        var sectionKey = await TryLoadLibrarySectionKeyAsync(plex, client, ratingKey);
        if (string.IsNullOrWhiteSpace(sectionKey))
        {
            return new PlexMetadataRefreshResult(true, true, "Requested Plex metadata refresh for this movie. Plex library scan skipped because MediaCloud could not determine the library section.");
        }

        using var sectionRequest = new HttpRequestMessage(HttpMethod.Get, BuildSectionRefreshUrl(plex.BaseUrl, sectionKey, folderPath));
        ApplyPlexAuth(plex, sectionRequest);
        using var sectionResponse = await client.SendAsync(sectionRequest);
        if (!sectionResponse.IsSuccessStatusCode)
        {
            var body = (await sectionResponse.Content.ReadAsStringAsync()).Trim();
            var message = string.IsNullOrWhiteSpace(body)
                ? $"Requested Plex metadata refresh for this movie. Plex library scan failed (HTTP {(int)sectionResponse.StatusCode})."
                : $"Requested Plex metadata refresh for this movie. Plex library scan failed: {body}";
            return new PlexMetadataRefreshResult(true, false, message);
        }

        return new PlexMetadataRefreshResult(true, true, "Requested Plex metadata refresh for this movie. Requested Plex library scan for the remediated file folder.");
    }

    private static string? GetFolderPath(string? remediatedOutputPath)
    {
        if (string.IsNullOrWhiteSpace(remediatedOutputPath))
        {
            return null;
        }

        try
        {
            return Path.GetDirectoryName(remediatedOutputPath);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> TryLoadLibrarySectionKeyAsync(IntegrationConfig plex, HttpClient client, string ratingKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildMetadataDetailsUrl(plex.BaseUrl, ratingKey));
        ApplyPlexAuth(plex, request);
        using var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync();
        return TryParseLibrarySectionKey(body);
    }

    internal static string? TryParseLibrarySectionKey(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        try
        {
            var doc = XDocument.Parse(xml);
            return doc.Descendants("Video")
                .Select(x => (string?)x.Attribute("librarySectionID") ?? (string?)x.Attribute("librarySectionKey"))
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        }
        catch
        {
            return null;
        }
    }

    private static void ApplyPlexAuth(IntegrationConfig plex, HttpRequestMessage request)
    {
        var authType = (plex.AuthType ?? string.Empty).Trim();
        if (string.Equals(authType, "ApiKey", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(plex.ApiKey))
        {
            request.Headers.Add("X-Plex-Token", plex.ApiKey);
        }
    }
}
