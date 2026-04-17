using Xunit;

public sealed class MovieDetailIssuePanelSourceTests
{
    [Fact]
    public void Library_item_issue_create_and_resolve_endpoints_allow_operator_role()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var content = File.ReadAllText(fullPath);

        Assert.Contains("app.MapPost(\"/api/library/items/{id:long}/issues\"", content);
        Assert.Contains("app.MapPost(\"/api/library/issues/{issueId:long}/resolve\"", content);
        Assert.Contains("}).RequireAuthorization(\"OperatorOnly\");", content);
    }

    [Fact]
    public void Movie_detail_issue_panel_uses_summary_only_and_auto_severity_text()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/MovieDetails.razor"));
        var content = File.ReadAllText(fullPath);

        Assert.Contains("<label for=\"movie-issue-summary\">Summary</label>", content);
        Assert.DoesNotContain("movie-issue-notes", content);
        Assert.Contains("Auto-set from issue type after save.", content);
    }
}
