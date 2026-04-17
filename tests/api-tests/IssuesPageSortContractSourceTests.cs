using Xunit;

public sealed class IssuesPageSortContractSourceTests
{
    [Fact]
    public void Issues_api_supports_the_sort_keys_the_blazor_page_sends()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var programPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var issuesPagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/Issues.razor"));

        var programContent = File.ReadAllText(programPath);
        var issuesPageContent = File.ReadAllText(issuesPagePath);

        Assert.Contains("ToggleSortAsync(\"item\")", issuesPageContent);
        Assert.Contains("ToggleSortAsync(\"type\")", issuesPageContent);
        Assert.Contains("ToggleSortAsync(\"media\")", issuesPageContent);
        Assert.Contains("ToggleSortAsync(\"status\")", issuesPageContent);
        Assert.Contains("ToggleSortAsync(\"difference\")", issuesPageContent);

        Assert.Contains("\"item\" or \"title\" =>", programContent);
        Assert.Contains("\"type\" =>", programContent);
        Assert.Contains("\"media\" =>", programContent);
        Assert.Contains("\"status\" =>", programContent);
        Assert.Contains("\"difference\" =>", programContent);
    }
}
