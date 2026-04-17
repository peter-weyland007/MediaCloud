using Xunit;

public sealed class IntegrationsNavigationSourceTests
{
    [Fact]
    public void Nav_menu_exposes_top_level_integrations_link_separate_from_settings()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var navPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Layout/NavMenu.razor"));
        var content = File.ReadAllText(navPath);

        Assert.Contains("href=\"integrations\"", content);
        Assert.Contains(">Integrations</NavLink>", content);
        Assert.Contains("href=\"settings\"", content);
    }

    [Fact]
    public void Integrations_landing_page_exists_as_top_level_panel_directory()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/Integrations.razor"));
        var content = File.ReadAllText(pagePath);

        Assert.Contains("@page \"/integrations\"", content);
        Assert.Contains("Integration control plane", content);
        Assert.Contains("/settings/integrations", content);
        Assert.Contains("@foreach (var group in GroupInstancesByService())", content);
        Assert.Contains("href=\"@($\"/integrations/{group.ServiceKey}\")\"", content);
    }

    [Fact]
    public void Integration_service_detail_page_exists_for_deeper_operational_views()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/IntegrationServiceDetails.razor"));
        var content = File.ReadAllText(pagePath);

        Assert.Contains("@page \"/integrations/{ServiceKey}\"", content);
        Assert.Contains("Service instances", content);
        Assert.Contains("Back to integrations", content);
        Assert.Contains("Open connection settings", content);
        Assert.Contains("TestIntegrationAsync(item)", content);
    }
}
