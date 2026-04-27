using Xunit;

public sealed class MovieDetailsSourceSyncPanelTests
{
    [Fact]
    public void Movie_details_loads_source_status_and_source_links_for_top_panel()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/MovieDetails.razor"));
        var content = File.ReadAllText(fullPath);

        Assert.Contains("/api/library/items/{Id}/source-status", content);
        Assert.Contains("/api/library/items/{Id}/sources", content);
        Assert.Contains("SOURCE SERVICES", content);
    }

    [Fact]
    public void Movie_details_renders_per_source_sync_button_and_timestamp()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/MovieDetails.razor"));
        var content = File.ReadAllText(fullPath);

        Assert.Contains("/api/library/items/{_item.Id}/sources/{source.ServiceKey}/sync", content);
        Assert.Contains("Last synced", content);
        Assert.Contains("await LoadItemAsync(clearStatusMessage: false);", content);
        Assert.Contains("Style=\"min-width:7rem;\"", content);
        Assert.Contains("FormatLastSyncedTimestamp(effectiveSyncedAtUtc)", content);
    }

    [Fact]
    public void Movie_details_uses_persisted_source_status_timestamp_after_page_refresh()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/MovieDetails.razor"));
        var content = File.ReadAllText(fullPath);

        Assert.Contains("var effectiveSyncedAtUtc = hasRecordedSync ? recordedSyncAtUtc : source.LastSyncedAtUtc;", content);
        Assert.Contains("DateTimeOffset? LastSyncedAtUtc", content);
    }

    [Fact]
    public void Movie_details_does_not_render_source_panel_status_banner_after_sync()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/MovieDetails.razor"));
        var content = File.ReadAllText(fullPath);

        Assert.DoesNotContain("<span class=\"card-note\">@_sourcePanelStatusMessage</span>", content);
        Assert.DoesNotContain("_sourcePanelStatusMessage = payload?.Message", content);
    }
}
