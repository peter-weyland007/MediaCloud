using System.Text;
using System.Text.Json;
using api.Data;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api;

public sealed record LibraryRemediationProviderCommandSnapshot(
    int? ProviderCommandId,
    string ProviderCommandStatus,
    string ProviderCommandSummary,
    DateTimeOffset? ProviderCommandCheckedAtUtc);

public sealed record LibraryRemediationQueuedCommandResult(
    bool Success,
    string Message,
    int? ProviderCommandId,
    string ProviderCommandStatus,
    string ProviderCommandSummary);

public sealed record LibraryRemediationArrSearchProgressSnapshot(
    string Status,
    string SearchStatus,
    string OutcomeSummary,
    string DownloadType);

public static class LibraryRemediationProviderCommandTracker
{
    public static LibraryRemediationQueuedCommandResult BuildQueuedResult(bool success, string message, string responseBody)
    {
        if (!success)
        {
            return new(false, message, null, string.Empty, string.Empty);
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            var commandId = GetJsonInt(root, "id");
            var status = NormalizeCommandStatus(GetJsonString(root, "status"));
            var commandName = GetJsonString(root, "name");
            var summary = BuildCommandSummary(commandId, status, commandName);
            return new(true, message, commandId, status, summary);
        }
        catch
        {
            return new(true, message, null, string.Empty, string.Empty);
        }
    }

    public static async Task<bool> RefreshAsync(
        LibraryRemediationJob job,
        MediaCloudDbContext db,
        IHttpClientFactory httpClientFactory,
        DateTimeOffset checkedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (!job.IntegrationId.HasValue || !job.ProviderCommandId.HasValue || !SupportsProviderCommands(job.ServiceKey))
        {
            return false;
        }

        var integration = await db.IntegrationConfigs.FirstOrDefaultAsync(
            x => x.Id == job.IntegrationId.Value,
            cancellationToken);
        if (integration is null)
        {
            return false;
        }

        var changed = await ReconcileExternalItemIdAsync(
            job,
            db,
            integration,
            httpClientFactory,
            checkedAtUtc,
            cancellationToken);

        var snapshot = await FetchSnapshotAsync(
            integration,
            job.ServiceKey,
            job.ProviderCommandId.Value,
            httpClientFactory,
            checkedAtUtc,
            cancellationToken);

        if (snapshot is null)
        {
            return changed;
        }

        changed = !string.Equals(job.ProviderCommandStatus, snapshot.ProviderCommandStatus, StringComparison.Ordinal)
            || !string.Equals(job.ProviderCommandSummary, snapshot.ProviderCommandSummary, StringComparison.Ordinal)
            || job.ProviderCommandCheckedAtUtc != snapshot.ProviderCommandCheckedAtUtc
            || changed;

        job.ProviderCommandStatus = snapshot.ProviderCommandStatus;
        job.ProviderCommandSummary = snapshot.ProviderCommandSummary;
        job.ProviderCommandCheckedAtUtc = snapshot.ProviderCommandCheckedAtUtc;
        if (snapshot.ProviderCommandCheckedAtUtc.HasValue)
        {
            job.LastCheckedAtUtc = snapshot.ProviderCommandCheckedAtUtc.Value;
            job.UpdatedAtUtc = snapshot.ProviderCommandCheckedAtUtc.Value;
        }

        var progressSnapshot = await FetchArrSearchProgressSnapshotAsync(
            integration,
            job,
            httpClientFactory,
            cancellationToken);
        if (progressSnapshot is not null)
        {
            changed = !string.Equals(job.SearchStatus, progressSnapshot.SearchStatus, StringComparison.Ordinal)
                || !string.Equals(job.Status, progressSnapshot.Status, StringComparison.Ordinal)
                || !string.Equals(job.OutcomeSummary, progressSnapshot.OutcomeSummary, StringComparison.Ordinal)
                || !string.Equals(job.DownloadType, progressSnapshot.DownloadType, StringComparison.Ordinal)
                || changed;

            job.SearchStatus = progressSnapshot.SearchStatus;
            job.Status = progressSnapshot.Status;
            job.OutcomeSummary = progressSnapshot.OutcomeSummary;
            job.DownloadType = progressSnapshot.DownloadType;
            job.LastCheckedAtUtc = checkedAtUtc;
            job.UpdatedAtUtc = checkedAtUtc;
        }

        return changed;
    }

    public static LibraryRemediationArrSearchProgressSnapshot? BuildArrSearchProgressSnapshot(
        string? serviceKey,
        int? targetExternalItemId,
        string? queueResponseBody,
        string? historyResponseBody,
        DateTimeOffset requestedAtUtc)
    {
        if (NormalizeServiceKey(serviceKey) is not ("radarr" or "sonarr"))
        {
            return null;
        }

        var importedHistory = FilterHistoryCandidatesByTargetExternalItemId(
                ParseHistoryRecords(historyResponseBody)
                    .Where(x => x.EventTimeUtc.HasValue && x.EventTimeUtc.Value >= requestedAtUtc)
                    .OrderByDescending(x => x.EventTimeUtc)
                    .ToArray(),
                targetExternalItemId)
            .FirstOrDefault(x => IsImportedHistoryEvent(x.EventType));
        if (importedHistory is not null)
        {
            var title = FirstNonEmpty(importedHistory.SourceTitle, "the replacement");
            return new("Processing", "Imported", $"{GetServiceDisplayName(serviceKey)} imported {title}{BuildProtocolSuffix(importedHistory.Protocol)}. MediaCloud is waiting for source refresh and verification.", NormalizeDownloadType(importedHistory.Protocol));
        }

        var queueRecords = FilterQueueRecordsByTargetExternalItemId(ParseQueueRecords(queueResponseBody), targetExternalItemId);
        var importing = queueRecords.FirstOrDefault(IsImportingQueueRecord);
        if (importing is not null)
        {
            var title = FirstNonEmpty(importing.Title, "the replacement");
            return new("Processing", "Importing", $"{GetServiceDisplayName(serviceKey)} finished the download and is importing {title}{BuildProtocolSuffix(importing.Protocol)}.", NormalizeDownloadType(importing.Protocol));
        }

        var downloading = queueRecords.FirstOrDefault(IsDownloadingQueueRecord);
        if (downloading is not null)
        {
            var title = FirstNonEmpty(downloading.Title, "the replacement");
            return new("Processing", "Downloading", $"{GetServiceDisplayName(serviceKey)} is downloading {title}{BuildProtocolSuffix(downloading.Protocol)}.", NormalizeDownloadType(downloading.Protocol));
        }

        var grabbedHistory = FilterHistoryCandidatesByTargetExternalItemId(
                ParseHistoryRecords(historyResponseBody)
                    .Where(x => x.EventTimeUtc.HasValue && x.EventTimeUtc.Value >= requestedAtUtc)
                    .OrderByDescending(x => x.EventTimeUtc)
                    .ToArray(),
                targetExternalItemId)
            .FirstOrDefault(x => string.Equals((x.EventType ?? string.Empty).Trim(), "grabbed", StringComparison.OrdinalIgnoreCase));
        if (grabbedHistory is not null)
        {
            var title = FirstNonEmpty(grabbedHistory.SourceTitle, "a replacement");
            return new("Processing", "Grabbed", $"{GetServiceDisplayName(serviceKey)} grabbed {title}{BuildProtocolSuffix(grabbedHistory.Protocol)} and is waiting for download/import.", NormalizeDownloadType(grabbedHistory.Protocol));
        }

        return null;
    }

    public static bool SupportsProviderCommands(string? serviceKey)
        => NormalizeServiceKey(serviceKey) is "radarr" or "sonarr" or "lidarr";

    private static async Task<bool> ReconcileExternalItemIdAsync(
        LibraryRemediationJob job,
        MediaCloudDbContext db,
        IntegrationConfig integration,
        IHttpClientFactory httpClientFactory,
        DateTimeOffset checkedAtUtc,
        CancellationToken cancellationToken)
    {
        if (NormalizeServiceKey(job.ServiceKey) != "radarr")
        {
            return false;
        }

        var item = await db.LibraryItems
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == job.LibraryItemId, cancellationToken);
        if (item?.TmdbId is not int tmdbId || tmdbId <= 0)
        {
            return false;
        }

        var resolvedExternalItemId = await FetchRadarrMovieIdByTmdbAsync(integration, tmdbId, httpClientFactory, cancellationToken);
        if (!resolvedExternalItemId.HasValue || resolvedExternalItemId.Value <= 0 || job.ExternalItemId == resolvedExternalItemId.Value)
        {
            return false;
        }

        job.ExternalItemId = resolvedExternalItemId.Value;
        job.LastCheckedAtUtc = checkedAtUtc;
        job.UpdatedAtUtc = checkedAtUtc;

        if (job.IntegrationId.HasValue)
        {
            var currentExternalId = resolvedExternalItemId.Value.ToString();
            var sourceLink = (await db.LibraryItemSourceLinks
                .Where(x => x.LibraryItemId == job.LibraryItemId && x.IntegrationId == job.IntegrationId.Value)
                .ToListAsync(cancellationToken))
                .OrderByDescending(x => x.LastSeenAtUtc)
                .FirstOrDefault();
            if (sourceLink is not null)
            {
                var conflictingLink = await db.LibraryItemSourceLinks.FirstOrDefaultAsync(
                    x => x.LibraryItemId == job.LibraryItemId
                        && x.IntegrationId == job.IntegrationId.Value
                        && x.ExternalId == currentExternalId,
                    cancellationToken);
                if (conflictingLink is null || conflictingLink.Id == sourceLink.Id)
                {
                    sourceLink.ExternalId = currentExternalId;
                    sourceLink.LastSeenAtUtc = checkedAtUtc;
                }
            }
        }

        return true;
    }

    private static async Task<int?> FetchRadarrMovieIdByTmdbAsync(
        IntegrationConfig integration,
        int tmdbId,
        IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(20);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{integration.BaseUrl.TrimEnd('/')}/api/v3/movie?tmdbId={tmdbId}");
            ApplyAuthHeaders(integration, request);
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
            {
                return null;
            }

            var row = document.RootElement[0];
            return GetJsonInt(row, "id");
        }
        catch
        {
            return null;
        }
    }

    private static async Task<LibraryRemediationProviderCommandSnapshot?> FetchSnapshotAsync(
        IntegrationConfig integration,
        string? serviceKey,
        int providerCommandId,
        IHttpClientFactory httpClientFactory,
        DateTimeOffset checkedAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            var endpointPath = BuildCommandEndpointPath(serviceKey, providerCommandId);
            if (string.IsNullOrWhiteSpace(endpointPath))
            {
                return null;
            }

            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(20);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{integration.BaseUrl.TrimEnd('/')}{endpointPath}");
            ApplyAuthHeaders(integration, request);
            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var failureSummary = string.IsNullOrWhiteSpace(body)
                    ? $"MediaCloud could not refresh provider command {providerCommandId} (HTTP {(int)response.StatusCode})."
                    : body[..Math.Min(250, body.Length)];
                return new(providerCommandId, string.Empty, failureSummary, checkedAtUtc);
            }

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var status = NormalizeCommandStatus(GetJsonString(root, "status"));
            var commandName = GetJsonString(root, "name");
            var completionMessage = FirstNonEmpty(
                GetJsonString(root, "message"),
                GetJsonString(root, "body"));
            var summary = BuildCommandSummary(providerCommandId, status, commandName, completionMessage);
            return new(providerCommandId, status, summary, checkedAtUtc);
        }
        catch (Exception ex)
        {
            var message = ex.Message.Length > 250 ? ex.Message[..250] : ex.Message;
            return new(providerCommandId, string.Empty, message, checkedAtUtc);
        }
    }

    private static string BuildCommandEndpointPath(string? serviceKey, int providerCommandId)
        => NormalizeServiceKey(serviceKey) switch
        {
            "lidarr" => $"/api/v1/command/{providerCommandId}",
            "radarr" or "sonarr" => $"/api/v3/command/{providerCommandId}",
            _ => string.Empty
        };

    private static DateTimeOffset? ParseJsonDateTimeOffset(JsonElement element, string propertyName)
    {
        var value = GetJsonString(element, propertyName);
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string NormalizeServiceKey(string? serviceKey)
        => (serviceKey ?? string.Empty).Trim().ToLowerInvariant();

    private static async Task<LibraryRemediationArrSearchProgressSnapshot?> FetchArrSearchProgressSnapshotAsync(
        IntegrationConfig integration,
        LibraryRemediationJob job,
        IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken)
    {
        if (!job.ExternalItemId.HasValue || NormalizeServiceKey(job.ServiceKey) is not ("radarr" or "sonarr"))
        {
            return null;
        }

        try
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(20);
            var baseUrl = integration.BaseUrl.TrimEnd('/');
            var queueEndpoint = NormalizeServiceKey(job.ServiceKey) == "radarr"
                ? $"/api/v3/queue?movieId={job.ExternalItemId.Value}&includeUnknownMovieItems=true"
                : $"/api/v3/queue?episodeId={job.ExternalItemId.Value}&includeUnknownSeriesItems=true";
            var historyEndpoint = NormalizeServiceKey(job.ServiceKey) == "radarr"
                ? $"/api/v3/history/movie?movieId={job.ExternalItemId.Value}&page=1&pageSize=20&sortDirection=descending"
                : $"/api/v3/history/episode?episodeId={job.ExternalItemId.Value}&page=1&pageSize=20&sortDirection=descending";

            var queueBody = await SendProviderGetAsync(client, integration, $"{baseUrl}{queueEndpoint}", cancellationToken);
            var historyBody = await SendProviderGetAsync(client, integration, $"{baseUrl}{historyEndpoint}", cancellationToken);
            return BuildArrSearchProgressSnapshot(job.ServiceKey, job.ExternalItemId, queueBody, historyBody, job.RequestedAtUtc);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> SendProviderGetAsync(
        HttpClient client,
        IntegrationConfig integration,
        string url,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyAuthHeaders(integration, request);
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static IReadOnlyList<ProviderQueueRecord> ParseQueueRecords(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            var rows = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("records", out var recordsElement)
                ? recordsElement
                : root;
            if (rows.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return rows.EnumerateArray()
                .Select(x => new ProviderQueueRecord(
                    GetJsonString(x, "title") ?? string.Empty,
                    GetJsonString(x, "status") ?? string.Empty,
                    FirstNonEmpty(GetJsonString(x, "trackedDownloadState"), GetJsonString(x, "trackedDownloadStatus"), GetJsonString(x, "state")),
                    ResolveTargetExternalItemId(x),
                    GetJsonString(x, "protocol") ?? string.Empty))
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<LibraryRemediationHistoryCandidate> ParseHistoryRecords(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            var rows = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("records", out var recordsElement)
                ? recordsElement
                : root;
            if (rows.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return rows.EnumerateArray()
                .Select(x => new LibraryRemediationHistoryCandidate(
                    (long?)GetJsonInt(x, "id"),
                    GetJsonString(x, "eventType") ?? string.Empty,
                    GetJsonString(x, "sourceTitle") ?? string.Empty,
                    GetJsonString(x, "downloadId") ?? string.Empty,
                    x.ToString(),
                    ParseJsonDateTimeOffset(x, "date") ?? ParseJsonDateTimeOffset(x, "eventTime") ?? ParseJsonDateTimeOffset(x, "importedDate"),
                    ResolveTargetExternalItemId(x),
                    GetJsonString(x, "protocol") ?? string.Empty))
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static bool IsDownloadingQueueRecord(ProviderQueueRecord record)
    {
        var status = (record.Status ?? string.Empty).Trim().ToLowerInvariant();
        var state = (record.TrackedState ?? string.Empty).Trim().ToLowerInvariant();
        return status.Contains("download", StringComparison.Ordinal)
            || state.Contains("download", StringComparison.Ordinal)
            || state is "queued" or "delay";
    }

    private static bool IsImportingQueueRecord(ProviderQueueRecord record)
    {
        var status = (record.Status ?? string.Empty).Trim().ToLowerInvariant();
        var state = (record.TrackedState ?? string.Empty).Trim().ToLowerInvariant();
        return status.Contains("import", StringComparison.Ordinal)
            || state.Contains("import", StringComparison.Ordinal)
            || (status == "completed" && state is "importpending" or "import_pending");
    }

    private static bool IsImportedHistoryEvent(string? eventType)
        => (eventType ?? string.Empty).Trim().ToLowerInvariant() is "downloadfolderimported" or "downloadimported" or "imported" or "moviefileimported";

    private static IReadOnlyList<ProviderQueueRecord> FilterQueueRecordsByTargetExternalItemId(IReadOnlyList<ProviderQueueRecord> records, int? targetExternalItemId)
    {
        if (!targetExternalItemId.HasValue || targetExternalItemId.Value <= 0 || records.Count == 0)
        {
            return records;
        }

        var matched = records.Where(x => x.TargetExternalItemId == targetExternalItemId.Value).ToArray();
        if (matched.Length > 0)
        {
            return matched;
        }

        return records.Any(x => x.TargetExternalItemId.HasValue)
            ? []
            : records;
    }

    private static IReadOnlyList<LibraryRemediationHistoryCandidate> FilterHistoryCandidatesByTargetExternalItemId(IReadOnlyList<LibraryRemediationHistoryCandidate> candidates, int? targetExternalItemId)
    {
        if (!targetExternalItemId.HasValue || targetExternalItemId.Value <= 0 || candidates.Count == 0)
        {
            return candidates;
        }

        var matched = candidates.Where(x => x.TargetExternalItemId == targetExternalItemId.Value).ToArray();
        if (matched.Length > 0)
        {
            return matched;
        }

        return candidates.Any(x => x.TargetExternalItemId.HasValue)
            ? []
            : candidates;
    }

    private static int? ResolveTargetExternalItemId(JsonElement element)
        => FirstNonNull(
            GetJsonInt(element, "movieId"),
            GetJsonInt(element, "seriesId"),
            GetJsonInt(element, "episodeId"),
            GetJsonInt(element, "albumId"),
            GetNestedJsonInt(element, "movie", "id"),
            GetNestedJsonInt(element, "series", "id"),
            GetNestedJsonInt(element, "episode", "id"),
            GetNestedJsonInt(element, "album", "id"));

    private static int? GetNestedJsonInt(JsonElement element, string parentPropertyName, string childPropertyName)
    {
        if (!element.TryGetProperty(parentPropertyName, out var parent) || parent.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return GetJsonInt(parent, childPropertyName);
    }

    private static int? FirstNonNull(params int?[] values)
        => values.FirstOrDefault(x => x.HasValue);

    private static string BuildProtocolSuffix(string? protocol)
    {
        var normalized = (protocol ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "usenet" => " via Usenet",
            "torrent" => " via Torrent",
            _ => string.Empty
        };
    }

    private static string NormalizeDownloadType(string? protocol)
    {
        var normalized = (protocol ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "usenet" => "Usenet",
            "torrent" => "Torrent",
            _ => string.Empty
        };
    }

    private static string GetServiceDisplayName(string? serviceKey)
        => NormalizeServiceKey(serviceKey) switch
        {
            "radarr" => "Radarr",
            "sonarr" => "Sonarr",
            _ => "Provider"
        };

    private sealed record ProviderQueueRecord(string Title, string Status, string TrackedState, int? TargetExternalItemId, string Protocol);

    private static void ApplyAuthHeaders(IntegrationConfig integration, HttpRequestMessage request)
    {
        var authType = (integration.AuthType ?? string.Empty).Trim();
        if (authType.Equals("ApiKey", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.TryAddWithoutValidation("X-Api-Key", integration.ApiKey);
            return;
        }

        if (!authType.Equals("Basic", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var credentialBytes = Encoding.UTF8.GetBytes($"{integration.Username}:{integration.Password}");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(credentialBytes));
    }

    private static string NormalizeCommandStatus(string? status)
    {
        var normalized = (status ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        if (normalized.Equals("queued", StringComparison.OrdinalIgnoreCase)) return "Queued";
        if (normalized.Equals("started", StringComparison.OrdinalIgnoreCase)) return "Started";
        if (normalized.Equals("running", StringComparison.OrdinalIgnoreCase)) return "Running";
        if (normalized.Equals("completed", StringComparison.OrdinalIgnoreCase)) return "Completed";
        if (normalized.Equals("failed", StringComparison.OrdinalIgnoreCase)) return "Failed";
        if (normalized.Equals("aborted", StringComparison.OrdinalIgnoreCase)) return "Aborted";
        return normalized;
    }

    private static string BuildCommandSummary(int? providerCommandId, string? status, string? commandName, string? providerMessage = null)
    {
        var idLabel = providerCommandId.HasValue ? $"command {providerCommandId.Value}" : "command";
        var normalizedStatus = NormalizeCommandStatus(status);
        var normalizedCommandName = (commandName ?? string.Empty).Trim();
        var summary = string.IsNullOrWhiteSpace(normalizedStatus)
            ? $"Provider accepted {idLabel}."
            : string.IsNullOrWhiteSpace(normalizedCommandName)
                ? $"Provider {idLabel} is {normalizedStatus.ToLowerInvariant()}."
                : $"Provider {normalizedCommandName} {idLabel} is {normalizedStatus.ToLowerInvariant()}.";

        var suffix = (providerMessage ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(suffix))
        {
            return summary;
        }

        return $"{summary} {suffix}";
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;

    private static string? GetJsonString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    private static int? GetJsonInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
        {
            return number;
        }

        if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out number))
        {
            return number;
        }

        return null;
    }
}
