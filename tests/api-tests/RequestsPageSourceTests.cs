using Xunit;

public sealed class RequestsPageSourceTests
{
    [Fact]
    public void Requests_page_uses_table_search_and_submit_workflow()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/Requests.razor"));
        var content = File.ReadAllText(pagePath);

        Assert.Contains("Search titles", content);
        Assert.Contains("Media type", content);
        Assert.Contains("REQUEST RESULTS", content);
        Assert.Contains("request-results-header", content);
        Assert.Contains("request-results-summary", content);
        Assert.DoesNotContain("request-page-shell", content);
        Assert.Contains("request-results-panel", content);
        Assert.DoesNotContain("margin-top: auto", content);
        Assert.Contains("request-results-footer", content);
        Assert.Contains("Rows per page", content);
        Assert.Contains("Previous", content);
        Assert.Contains("Next", content);
        Assert.Contains("PagedResults", content);
        Assert.DoesNotContain("SEARCH RESULTS", content);
        Assert.DoesNotContain("REQUESTABLE", content);
        Assert.Contains("<table", content);
        Assert.Contains("Title", content);
        Assert.Contains("Poster", content);
        Assert.Contains("Overseerr", content);
        Assert.Contains("Request Record", content);
        Assert.Contains("request-header-help", content);
        Assert.Contains("request-header-help-popup", content);
        Assert.Contains("Shows whether MediaCloud already sees the title in your library data.", content);
        Assert.Contains("Shows whether Overseerr already has this title in its media catalog.", content);
        Assert.Contains("Shows whether Overseerr already has a request record for this title.", content);
        Assert.Contains("Shows whether Radarr or Sonarr is already tracking this title downstream.", content);
        Assert.Contains("No request record", content);
        Assert.Contains("Present in Overseerr", content);
        Assert.Contains("Create request record", content);
        Assert.Contains("Tracked in Sonarr", content);
        Assert.Contains("Tracked in Radarr", content);
        Assert.Contains("row.CanReconcileRequest || row.InOverseerrMedia ? \"warn\" : \"muted\"", content);
        Assert.Contains("GetOverseerrStateLabel(row)", content);
        Assert.Contains("Downstream", content);
        Assert.Contains("GetRequestActionLabel(row)", content);
        Assert.Contains("IsRequestActionDisabled(row, isSubmitting)", content);
        Assert.Contains("Actionable", content);
        Assert.Contains("No request action", content);
        Assert.Contains("GetPosterUrl(row.PosterPath)", content);
        Assert.Contains("<img", content);
        Assert.Contains("request-poster-preview-trigger", content);
        Assert.Contains("request-poster-preview-popup", content);
        Assert.Contains("Preview larger poster", content);
        Assert.Contains("/api/requests/search", content);
        Assert.Contains("/api/requests", content);
        Assert.Contains("Requesting...", content);
        Assert.Contains("await SearchAsync();", content);
        Assert.Contains("Snackbar.Add", content);
    }
}
