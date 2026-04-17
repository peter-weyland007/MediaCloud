using Xunit;

public sealed class MovieDetailIssuePanelUiTests
{
    [Fact]
    public void Movie_details_page_includes_collapsed_media_file_issues_panel_and_history_actions()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/MovieDetails.razor"));
        var content = File.ReadAllText(fullPath);

        Assert.Contains("MEDIA FILE ISSUES", content);
        Assert.Contains("@if (_issuesExpanded)", content);
        Assert.Contains("ToggleIssuesPanel", content);
        Assert.Contains("Save issue", content);
        Assert.Contains("Resolve", content);
        Assert.Contains("Open: @OpenIssueCount · Resolved: @ResolvedIssueCount", content);
    }
}
