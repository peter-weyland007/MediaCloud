using Xunit;

public sealed class IntegrationPanelLayoutSourceTests
{
    [Fact]
    public void Integrations_page_uses_table_driven_control_plane_with_filtering_and_panel_links()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/Integrations.razor"));
        var content = File.ReadAllText(pagePath);

        Assert.Contains("SET UP INTEGRATIONS", content);
        Assert.Contains("QUICK FILTER", content);
        Assert.Contains("@foreach (var item in FilteredInstances())", content);
        Assert.Contains("Href=\"@($\"/integrations/{item.ServiceKey}\")\"", content);
        Assert.Contains("FilteredInstances()", content);
    }

    [Fact]
    public void Integration_service_detail_page_uses_collapsible_panels_with_plain_chevrons()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/IntegrationServiceDetails.razor"));
        var content = File.ReadAllText(pagePath);

        Assert.Contains("integration-detail-panel-toggle", content);
        Assert.Contains("@onclick='() => TogglePanel(\"summary\")'", content);
        Assert.Contains("PanelChevron(\"summary\")", content);
        Assert.Contains("=> IsPanelExpanded(key) ? \"▾\" : \"▸\";", content);
    }

    [Fact]
    public void Integration_service_detail_page_surfaces_radarr_control_and_mapping_panels()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/IntegrationServiceDetails.razor"));
        var content = File.ReadAllText(pagePath);

        Assert.Contains("NAMING CONVENTION", content);
        Assert.Contains("ROOT FOLDERS & MAPPINGS", content);
        Assert.Contains("OpenCreatePathMappingDialogAsync", content);
        Assert.Contains("OpenEditPathMappingDialogAsync", content);
        Assert.Contains("TestPathMappingAsync(_radarrPathMapping)", content);
    }
}
