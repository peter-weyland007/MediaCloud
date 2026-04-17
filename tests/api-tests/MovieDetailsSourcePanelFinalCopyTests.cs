using Xunit;

public sealed class MovieDetailsSourcePanelFinalCopyTests
{
    [Fact]
    public void Movie_details_source_panel_uses_badge_based_match_and_mismatch_labels()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/MovieDetails.razor"));
        var content = File.ReadAllText(fullPath);

        Assert.Contains("\"Matched\"", content);
        Assert.Contains("\"Mismatch\"", content);
        Assert.Contains("BuildSourceBadgeLabel", content);
        Assert.DoesNotContain("Link status", content);
        Assert.DoesNotContain("Monitoring:", content);
    }

    [Fact]
    public void Movie_details_source_panel_avoids_raw_source_note_dumping()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/MovieDetails.razor"));
        var content = File.ReadAllText(fullPath);

        Assert.DoesNotContain("@source.Note", content);
        Assert.Contains("BuildSourceBadgeLabel", content);
        Assert.DoesNotContain("BuildMonitoringStatusLabel", content);
    }
}
