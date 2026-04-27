using Xunit;

public sealed class RadarrIntegrationDetailSourceTests
{
    [Fact]
    public void Radarr_integration_detail_page_surfaces_operational_sections_for_sync_monitoring_paths_and_drift()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/IntegrationServiceDetails.razor"));
        var content = File.ReadAllText(pagePath);

        Assert.Contains("RADARR CONTROL", content);
        Assert.Contains("CATALOG SYNC", content);
        Assert.Contains("MONITORING OWNERSHIP", content);
        Assert.Contains("NAMING CONVENTION", content);
        Assert.Contains("Standard file format", content);
        Assert.Contains("Movie folder format", content);
        Assert.Contains("ROOT FOLDERS & MAPPINGS", content);
        Assert.Contains("DRIFT & RECONCILIATION", content);
    }

    [Fact]
    public void Radarr_integration_detail_page_reads_existing_control_plane_endpoints_for_monitoring_paths_and_drift()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/IntegrationServiceDetails.razor"));
        var content = File.ReadAllText(pagePath);

        Assert.Contains("/api/settings/monitoring", content);
        Assert.Contains("/api/integrations/{integrationId}/radarr/naming", content);
        Assert.Contains("/api/library-path-mappings", content);
        Assert.Contains("/api/integrations/{integrationId}/remote-roots", content);
        Assert.Contains("/api/reconcile/plex-backfill/preview", content);
        Assert.Contains("/api/library-path-mappings/{mappingId}/test", content);
    }
}
