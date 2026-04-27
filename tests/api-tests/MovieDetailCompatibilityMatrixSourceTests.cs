using Xunit;

public sealed class MovieDetailCompatibilityMatrixSourceTests
{
    [Fact]
    public void Movie_details_page_surfaces_profile_vs_file_compatibility_table_inside_why_mediacloud_chose_this()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/MovieDetails.razor"));
        var content = File.ReadAllText(fullPath);

        Assert.DoesNotContain("PROFILE MATCH MATRIX", content);
        Assert.DoesNotContain("ToggleCompatibilityPanel", content);
        Assert.DoesNotContain("_compatibilityExpanded", content);
        Assert.DoesNotContain("aria-controls=\"movie-compatibility-panel\"", content);
        Assert.Contains("WHY MEDIACLOUD CHOSE THIS", content);
        Assert.Contains("class=\"movie-remediation-why-compare\"", content);
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
