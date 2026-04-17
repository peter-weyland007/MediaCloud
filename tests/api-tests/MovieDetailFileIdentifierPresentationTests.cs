using Xunit;

public sealed class MovieDetailFileIdentifierPresentationTests
{
    [Fact]
    public void Shared_media_detail_body_hides_tvdb_for_movies()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Shared/MediaItemDetailBody.razor"));
        var content = File.ReadAllText(fullPath);

        Assert.Contains("!string.Equals(Item.MediaType, \"Movie\", StringComparison.OrdinalIgnoreCase)", content);
        Assert.Contains("TVDB:", content);
    }

    [Fact]
    public void Library_item_detail_route_backfills_blank_movie_primary_file_path_from_source()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var content = File.ReadAllText(fullPath);

        Assert.Contains("string.Equals(row.MediaType, \"Movie\", StringComparison.OrdinalIgnoreCase)", content);
        Assert.Contains("string.IsNullOrWhiteSpace(row.PrimaryFilePath)", content);
        Assert.Contains("await TryRefreshPrimaryFilePathFromSourceAsync(row, db, httpClientFactory)", content);
    }

    [Fact]
    public void Primary_file_refresh_helper_returns_resolved_path_after_mapping()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var content = File.ReadAllText(fullPath);

        Assert.Contains("item.PrimaryFilePath = resolved;", content);
        Assert.Contains("return resolved;", content);
    }
}
