using Xunit;

public sealed class DetailActionButtonVisualConsistencyTests
{
    [Fact]
    public void Movie_source_sync_buttons_use_same_primary_filled_treatment_as_analyze_button()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/MovieDetails.razor"));
        var content = File.ReadAllText(fullPath);

        Assert.Contains("Class=\"media-btn media-btn--primary\"", content);
        Assert.Contains("Variant=\"Variant.Filled\"", content);
        Assert.Contains("Last synced:", content);
    }

    [Fact]
    public void Analyze_button_can_move_below_primary_file_with_left_aligned_status_text()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Shared/MediaItemDetailBody.razor"));
        var content = File.ReadAllText(fullPath);

        Assert.Contains("AnalyzeButtonBelowPrimaryFile", content);
        Assert.Contains("align-items:flex-start; margin-top:0.85rem; min-width:0;", content);
        Assert.Contains("text-align:left;", content);
        Assert.Contains("font-size:0.72rem;", content);
        Assert.DoesNotContain("@StatusMessage", content);
    }
}
