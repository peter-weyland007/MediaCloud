using Xunit;

public sealed class RadarrDefaultAddPolicySourceTests
{
    [Fact]
    public void Program_exposes_radarr_default_add_policy_read_and_write_routes()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var programPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var content = File.ReadAllText(programPath);

        Assert.Contains("app.MapGet(\"/api/integrations/{id:long}/radarr/default-add-policy\"", content);
        Assert.Contains("app.MapPut(\"/api/integrations/{id:long}/radarr/default-add-policy\"", content);
        Assert.Contains("preferred-root-folder", content);
        Assert.Contains("minimum-availability", content);
        Assert.Contains("monitored-default", content);
        Assert.Contains("RadarrDefaultAddPolicyResponse", content);
        Assert.Contains("UpdateRadarrDefaultAddPolicyRequest", content);
    }

    [Fact]
    public void Radarr_detail_page_surfaces_default_add_policy_panel()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/IntegrationServiceDetails.razor"));
        var content = File.ReadAllText(pagePath);

        Assert.Contains("DEFAULT ADD POLICY", content);
        Assert.Contains("/api/integrations/{integrationId}/radarr/default-add-policy", content);
        Assert.Contains("SaveRadarrDefaultAddPolicyAsync", content);
        Assert.Contains("ReloadRadarrDefaultAddPolicyAsync", content);
        Assert.Contains("Minimum availability", content);
        Assert.Contains("Monitor by default", content);
    }

    [Fact]
    public void Radarr_backfill_adds_use_saved_default_add_policy_values()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var programPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var content = File.ReadAllText(programPath);

        Assert.Contains("LoadRadarrDefaultAddPolicyAsync", content);
        Assert.Contains("EnsureMovieInRadarrAsync(candidate, radarr, db, httpClientFactory)", content);
        Assert.Contains("minimumAvailability = defaultAddPolicy.MinimumAvailability", content);
        Assert.Contains("monitored = defaultAddPolicy.Monitored", content);
        Assert.Contains("rootFolderPath = defaultAddPolicy.RootFolderPath", content);
        Assert.Contains("qualityProfileId = defaultAddPolicy.QualityProfileId", content);
    }
}
