using Xunit;

public sealed class SonarrQualityProfilesSourceTests
{
    [Fact]
    public void Program_exposes_sonarr_quality_profile_read_write_and_sync_routes()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var programPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var content = File.ReadAllText(programPath);

        Assert.Contains("app.MapGet(\"/api/integrations/{id:long}/sonarr/quality-profiles\"", content);
        Assert.Contains("app.MapPut(\"/api/integrations/{id:long}/sonarr/quality-profiles\"", content);
        Assert.Contains("app.MapPost(\"/api/integrations/{id:long}/sonarr/quality-profiles/sync-mediacloud-profile\"", content);
        Assert.Contains("/api/v3/qualityprofile", content);
        Assert.Contains("MediaCloud Profile", content);
    }

    [Fact]
    public void Sonarr_integration_detail_page_surfaces_quality_profiles_panel_and_sync_button()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/IntegrationServiceDetails.razor"));
        var content = File.ReadAllText(pagePath);

        Assert.Contains("IsSonarrPage", content);
        Assert.Contains("/api/integrations/{integrationId}/sonarr/quality-profiles", content);
        Assert.Contains("/api/integrations/{integrationId}/sonarr/quality-profiles/sync-mediacloud-profile", content);
        Assert.Contains("LoadSonarrQualityProfilesAsync", content);
        Assert.Contains("SaveSonarrQualityProfileAsync", content);
        Assert.Contains("SyncSonarrMediaCloudProfileAsync", content);
        Assert.Contains("Sync MediaCloud Profile", content);
    }
}
