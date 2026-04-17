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
    public void Analyze_button_uses_stacked_right_aligned_status_text_under_button()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Shared/MediaItemDetailBody.razor"));
        var content = File.ReadAllText(fullPath);

        Assert.Contains("display:flex; flex-direction:column; gap:0.2rem; align-items:flex-end; align-self:start; min-width:0;", content);
        Assert.Contains("text-align:right;", content);
        Assert.Contains("font-size:0.72rem;", content);
        Assert.DoesNotContain("@StatusMessage", content);
    }
}
