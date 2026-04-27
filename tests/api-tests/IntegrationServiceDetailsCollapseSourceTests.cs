using Xunit;

public sealed class IntegrationServiceDetailsCollapseSourceTests
{
    [Fact]
    public void Integration_service_detail_panels_are_toggle_driven_and_default_collapsed()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/IntegrationServiceDetails.razor"));
        var content = File.ReadAllText(pagePath);

        Assert.Contains("integration-detail-panel-toggle", content);
        Assert.Contains("TogglePanel(", content);
        Assert.Contains("IsPanelExpanded(", content);
        Assert.Contains("aria-expanded=\"@IsPanelExpanded(", content);
        Assert.Contains("integration-detail-panel-body", content);
        Assert.Contains("integration-detail-panel-chevron", content);
    }

    [Fact]
    public void Integration_service_detail_panels_start_with_empty_expanded_set()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/IntegrationServiceDetails.razor"));
        var content = File.ReadAllText(pagePath);

        Assert.Contains("private readonly HashSet<string> _expandedPanels = [];", content);
        Assert.Contains("=> _expandedPanels.Contains(key);", content);
    }
}
