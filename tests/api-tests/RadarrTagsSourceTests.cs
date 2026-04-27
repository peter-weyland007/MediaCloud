using Xunit;

public sealed class RadarrTagsSourceTests
{
    [Fact]
    public void Program_exposes_radarr_tags_read_and_write_routes()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var programPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var content = File.ReadAllText(programPath);

        Assert.Contains("app.MapGet(\"/api/integrations/{id:long}/radarr/tags\"", content);
        Assert.Contains("app.MapPut(\"/api/integrations/{id:long}/radarr/tags\"", content);
        Assert.Contains("/api/v3/tag", content);
        Assert.Contains("preferred-tag-ids", content);
        Assert.Contains("RadarrTagOptionResponse", content);
        Assert.Contains("RadarrTagsResponse", content);
        Assert.Contains("UpdateRadarrTagsRequest", content);
    }

    [Fact]
    public void Radarr_detail_page_surfaces_tags_management_panel()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/IntegrationServiceDetails.razor"));
        var content = File.ReadAllText(pagePath);

        Assert.Contains("TAGS", content);
        Assert.Contains("/api/integrations/{integrationId}/radarr/tags", content);
        Assert.Contains("SaveRadarrTagsAsync", content);
        Assert.Contains("ReloadRadarrTagsAsync", content);
        Assert.Contains("Apply tags when adding movies to Radarr", content);
    }

    [Fact]
    public void Radarr_backfill_adds_use_saved_tag_ids()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var programPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var content = File.ReadAllText(programPath);

        Assert.Contains("LoadRadarrTagsAsync", content);
        Assert.Contains("tags = radarrTags.SelectedTagIds", content);
    }
}
