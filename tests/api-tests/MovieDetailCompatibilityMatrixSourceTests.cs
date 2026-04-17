using Xunit;

public sealed class MovieDetailCompatibilityMatrixSourceTests
{
    [Fact]
    public void Movie_details_page_includes_collapsible_profile_vs_file_compatibility_table()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/MovieDetails.razor"));
        var content = File.ReadAllText(fullPath);

        Assert.Contains("PROFILE MATCH MATRIX", content);
        Assert.Contains("ToggleCompatibilityPanel", content);
        Assert.Contains("_compatibilityExpanded", content);
        Assert.Contains("aria-controls=\"movie-compatibility-panel\"", content);
        Assert.Contains("<table class=\"movie-compatibility-table\">", content);
        Assert.Contains("Target", content);
        Assert.Contains("File", content);
        Assert.Contains("Fit", content);
        Assert.Contains("GetCompatibilityStatusClass", content);
        Assert.Contains("GetCompatibilityStatusLabel", content);
        Assert.Contains("movie-compatibility-status--disc-image", content);
        Assert.Contains("\"Disc Image\"", content);
        Assert.DoesNotContain("GetCompatibilityActualCellStyle", content);
    }
}
