using Xunit;

public sealed class RadarrSearchRetryControlsSourceTests
{
    [Fact]
    public void Radarr_integration_detail_page_surfaces_search_retry_workbench()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/IntegrationServiceDetails.razor"));
        var content = File.ReadAllText(pagePath);

        Assert.Contains("SEARCH & RETRY", content);
        Assert.Contains("Retry search in Radarr", content);
        Assert.Contains("Add to Radarr", content);
        Assert.Contains("Refresh drift preview", content);
        Assert.Contains("/api/library/items/{libraryItemId}/remediation/search-replacement", content);
        Assert.Contains("/api/library/items/{libraryItemId}/sources/radarr/sync", content);
        Assert.Contains("RunRadarrSearchReplacementAsync", content);
        Assert.Contains("SyncLibraryItemToRadarrAsync", content);
    }

    [Fact]
    public void Radarr_search_retry_workbench_reads_drift_preview_rows()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/IntegrationServiceDetails.razor"));
        var content = File.ReadAllText(pagePath);

        Assert.Contains("VisibleRadarrSearchRetryRows", content);
        Assert.Contains("_radarrDriftPreview?.Items", content);
        Assert.Contains("row.MissingRadarr", content);
        Assert.Contains("row.CanBackfill", content);
    }

    [Fact]
    public void Radarr_search_retry_workbench_supports_bulk_selection_and_bulk_actions()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/IntegrationServiceDetails.razor"));
        var content = File.ReadAllText(pagePath);

        Assert.Contains("Bulk Add to Radarr", content);
        Assert.Contains("Bulk Retry search", content);
        Assert.Contains("AreAllVisibleRadarrSearchRetryRowsSelected", content);
        Assert.Contains("ToggleSelectAllRadarrSearchRetryRows", content);
        Assert.Contains("ToggleRadarrSearchRetryRowSelection", content);
        Assert.Contains("BulkSyncSelectedLibraryItemsToRadarrAsync", content);
        Assert.Contains("BulkRunRadarrSearchReplacementAsync", content);
        Assert.Contains("_selectedRadarrSearchRetryLibraryItemIds", content);
    }
}
