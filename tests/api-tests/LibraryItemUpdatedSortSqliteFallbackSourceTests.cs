using Xunit;

public sealed class LibraryItemUpdatedSortSqliteFallbackSourceTests
{
    [Fact]
    public void Library_item_endpoints_use_client_side_fallback_for_updated_sort_under_sqlite()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var programPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var programContent = File.ReadAllText(programPath);

        Assert.Contains("if (RequiresClientSideLibraryItemSort(sortBy))", programContent);
        Assert.Contains("rows = ApplyLibraryItemSortInMemory(await query.ToListAsync(), sortBy, sortDir)", programContent);
        Assert.Contains("rows = await ApplyLibraryItemSort(query, sortBy, sortDir)", programContent);
        Assert.Contains("static bool RequiresClientSideLibraryItemSort(string? sortBy)", programContent);
        Assert.Contains("=> string.Equals((sortBy ?? string.Empty).Trim(), \"updated\"", programContent);
        Assert.Contains("static IEnumerable<LibraryItem> ApplyLibraryItemSortInMemory(IEnumerable<LibraryItem> rows, string? sortBy, string? sortDir)", programContent);
        Assert.Contains("\"updated\" => descending ? rows.OrderByDescending(x => x.UpdatedAtUtc).ThenBy(x => x.SortTitle) : rows.OrderBy(x => x.UpdatedAtUtc).ThenBy(x => x.SortTitle)", programContent);
    }
}
