using api.Models;

namespace api;

public static class TelevisionSourceCoverage
{
    public static IReadOnlyList<TelevisionSourceCoverageRow> BuildRows(
        LibraryItem item,
        IReadOnlyList<IntegrationConfig> enabledIntegrations,
        IReadOnlyList<LibraryItemSourceLink> sourceLinks)
    {
        ArgumentNullException.ThrowIfNull(item);
        enabledIntegrations ??= [];
        sourceLinks ??= [];

        var expectedServices = GetExpectedServices(item.MediaType);
        var integrationByService = enabledIntegrations
            .Where(x => x.Enabled)
            .GroupBy(x => NormalizeServiceKey(x.ServiceKey))
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.InstanceName).First());
        var linksByIntegrationId = sourceLinks
            .Where(x => x.LibraryItemId == item.Id && !x.IsDeletedAtSource)
            .GroupBy(x => x.IntegrationId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var rows = new List<TelevisionSourceCoverageRow>(expectedServices.Length);
        foreach (var service in expectedServices)
        {
            integrationByService.TryGetValue(service, out var integration);
            var hasLink = integration is not null
                && linksByIntegrationId.TryGetValue(integration.Id, out var links)
                && links.Count > 0;

            var note = integration is null
                ? "No enabled integration configured."
                : hasLink
                    ? "Source link is present."
                    : BuildMissingLinkNote(item.MediaType, service);

            rows.Add(new TelevisionSourceCoverageRow(
                service,
                IntegrationCatalog.GetName(service),
                integration?.Id,
                integration?.InstanceName ?? string.Empty,
                hasLink,
                note));
        }

        return rows;
    }

    public static string[] GetExpectedServices(string mediaType)
        => string.Equals(mediaType, "Series", StringComparison.OrdinalIgnoreCase)
            ? ["sonarr", "plex", "overseerr"]
            : ["sonarr", "plex"];

    private static string BuildMissingLinkNote(string mediaType, string service)
        => service switch
        {
            "sonarr" => string.Equals(mediaType, "Series", StringComparison.OrdinalIgnoreCase)
                ? "Missing link. Sonarr should be the catalog source of truth for this show."
                : "Missing link. Sonarr should be the catalog source of truth for this episode.",
            "plex" => string.Equals(mediaType, "Series", StringComparison.OrdinalIgnoreCase)
                ? "Missing link. Plex should confirm playback/library presence for this show."
                : "Missing link. Plex should confirm playback/library presence for this episode.",
            "overseerr" => "Missing link. Overseerr should capture request intent for this show.",
            _ => "Missing link."
        };

    private static string NormalizeServiceKey(string serviceKey)
        => (serviceKey ?? string.Empty).Trim().ToLowerInvariant();
}

public sealed record TelevisionSourceCoverageRow(
    string ServiceKey,
    string DisplayName,
    long? IntegrationId,
    string InstanceName,
    bool HasSourceLink,
    string Note);
