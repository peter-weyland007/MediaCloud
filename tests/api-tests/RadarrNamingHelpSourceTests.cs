using Xunit;

public sealed class RadarrNamingHelpSourceTests
{
    [Fact]
    public void Radarr_naming_panel_uses_top_right_app_native_info_icon_and_unboxed_chevron()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/IntegrationServiceDetails.razor"));
        var content = File.ReadAllText(pagePath);

        Assert.Contains("integration-detail-panel-icon-button", content);
        Assert.Contains("integration-detail-panel-actions", content);
        Assert.Contains("integration-detail-panel-icon-glyph", content);
        Assert.Contains("OpenRadarrNamingHelp", content);
        Assert.Contains("@PanelChevron(\"radarr-naming\")", content);
        Assert.DoesNotContain("<MudAlert Severity=\"Severity.Info\"", content);
        Assert.DoesNotContain("ⓘ", content);
        Assert.DoesNotContain("integration-detail-panel-chevron-button", content);
    }

    [Fact]
    public void Radarr_naming_help_dialog_reuses_app_dialog_styling()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/IntegrationServiceDetails.razor"));
        var cssPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/wwwroot/app.css"));
        var pageContent = File.ReadAllText(pagePath);
        var cssContent = File.ReadAllText(cssPath);

        Assert.Contains("<MudDialog Class=\"integration-dialog radarr-naming-help-dialog\"", pageContent);
        Assert.Contains(".radarr-naming-help-dialog", cssContent);
        Assert.Contains("Movie naming tokens", pageContent);
        Assert.Contains("{Movie Title}", pageContent);
        Assert.Contains("{ImdbId}", pageContent);
        Assert.Contains("{MediaInfo AudioCodec}", pageContent);
        Assert.Contains("{Custom Formats}", pageContent);
        Assert.Contains("{Original Title}", pageContent);
    }
}
