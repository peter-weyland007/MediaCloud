using Xunit;

public sealed class RadarrQualityProfilesSourceTests
{
    [Fact]
    public void Program_exposes_radarr_quality_profile_read_and_write_routes()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var programPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var content = File.ReadAllText(programPath);

        Assert.Contains("app.MapGet(\"/api/integrations/{id:long}/radarr/quality-profiles\"", content);
        Assert.Contains("app.MapPut(\"/api/integrations/{id:long}/radarr/quality-profiles\"", content);
        Assert.Contains("app.MapPost(\"/api/integrations/{id:long}/radarr/quality-profiles/sync-mediacloud-profile\"", content);
        Assert.Contains("/api/v3/qualityprofile", content);
        Assert.Contains("MediaCloud Profile", content);
        Assert.Contains("preferred-quality-profile-id", content);
        Assert.Contains("RadarrQualityProfileOptionResponse", content);
        Assert.Contains("RadarrQualityProfilesResponse", content);
        Assert.Contains("UpdateRadarrPreferredQualityProfileRequest", content);
    }

    [Fact]
    public void Radarr_integration_detail_page_surfaces_quality_profiles_panel_and_api_binding()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/IntegrationServiceDetails.razor"));
        var content = File.ReadAllText(pagePath);

        Assert.Contains("QUALITY PROFILES", content);
        Assert.Contains("/api/integrations/{integrationId}/radarr/quality-profiles", content);
        Assert.Contains("/api/integrations/{integrationId}/radarr/quality-profiles/sync-mediacloud-profile", content);
        Assert.Contains("SaveRadarrQualityProfileAsync", content);
        Assert.Contains("ReloadRadarrQualityProfilesAsync", content);
        Assert.Contains("SyncRadarrMediaCloudProfileAsync", content);
        Assert.Contains("Sync MediaCloud Profile", content);
        Assert.Contains("Preferred quality profile", content);
    }
}
