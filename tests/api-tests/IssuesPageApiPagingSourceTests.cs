using Xunit;

public sealed class IssuesPageApiPagingSourceTests
{
    [Fact]
    public void Issues_api_returns_paged_payload_expected_by_the_blazor_page()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var programPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var issuesPagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/Issues.razor"));

        var programContent = File.ReadAllText(programPath);
        var issuesPageContent = File.ReadAllText(issuesPagePath);

        Assert.Contains("app.MapGet(\"/api/library/issues\"", programContent);
        Assert.Contains("string? mediaType, string? sortBy, string? sortDir, int? pageIndex, int? pageSize", programContent);
        Assert.Contains("new LibraryIssuePageResponse(rows, totalCount, normalizedPageIndex, normalizedPageSize, availableIssueTypes)", programContent);
        Assert.Contains("private record LibraryIssuePageDto(IReadOnlyList<LibraryIssueDto> Items, int TotalCount, int PageIndex, int PageSize, IReadOnlyList<string> AvailableIssueTypes);", issuesPageContent);
    }
}
