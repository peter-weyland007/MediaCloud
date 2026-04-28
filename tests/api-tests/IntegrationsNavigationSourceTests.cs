using Xunit;

public sealed class IntegrationsNavigationSourceTests
{
    [Fact]
    public void Integrations_navigation_moves_under_settings_instead_of_sidebar()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var navPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Layout/NavMenu.razor"));
        var settingsPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/Settings.razor"));
        var navContent = File.ReadAllText(navPath);
        var settingsContent = File.ReadAllText(settingsPath);

        Assert.DoesNotContain("href=\"integrations\"", navContent);
        Assert.DoesNotContain(">Integrations</NavLink>", navContent);
        Assert.Contains("href=\"settings\"", navContent);

        Assert.Contains("INTEGRATIONS", settingsContent);
        Assert.Contains("Open integrations settings", settingsContent);
        Assert.Contains("href=\"/integrations\"", settingsContent);
    }

    [Fact]
    public void Integrations_landing_page_exists_as_top_level_control_plane()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/Integrations.razor"));
        var content = File.ReadAllText(pagePath);

        Assert.Contains("@page \"/integrations\"", content);
        Assert.Contains("Integration control plane", content);
        Assert.Contains("SET UP INTEGRATIONS", content);
        Assert.Contains("@foreach (var item in FilteredInstances())", content);
        Assert.Contains("Href=\"@($\"/integrations/{item.ServiceKey}\")\"", content);
        Assert.DoesNotContain("/settings/integrations", content);
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
        Assert.Contains("Manage connections", content);
        Assert.DoesNotContain("/settings/integrations", content);
        Assert.Contains("TestIntegrationAsync(item)", content);
    }
}
