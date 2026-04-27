using Xunit;

public sealed class BazarrSubtitlePanelSourceTests
{
    [Fact]
    public void Movie_and_tv_detail_pages_render_subtitle_panel()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var moviePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/MovieDetails.razor"));
        var tvPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/TvDetails.razor"));
        var componentPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Shared/BazarrSubtitlePanel.razor"));

        var movieContent = File.ReadAllText(moviePath);
        var tvContent = File.ReadAllText(tvPath);
        var componentContent = File.ReadAllText(componentPath);

        Assert.Contains("<BazarrSubtitlePanel", movieContent);
        Assert.Contains("<BazarrSubtitlePanel", tvContent);
        Assert.Contains("<p class=\"card-label\">SUBTITLES</p>", componentContent);
        Assert.DoesNotContain("BAZARR SUBTITLES", componentContent);
        Assert.Contains("Missing targets", componentContent);
        Assert.Contains("Available subtitles", componentContent);
        Assert.Contains("subtitle-panel-content", componentContent);
        Assert.DoesNotContain("aria-expanded=\"@_expanded\"", componentContent);
        Assert.DoesNotContain("private bool _expanded;", componentContent);
        Assert.DoesNotContain("ToggleExpanded", componentContent);
    }
}
