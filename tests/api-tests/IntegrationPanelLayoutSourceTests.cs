using Xunit;

public sealed class IntegrationPanelLayoutSourceTests
{
    [Fact]
    public void Settings_integrations_page_uses_collapsible_service_panels_collapsed_by_default()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/SettingsIntegrations.razor"));
        var content = File.ReadAllText(pagePath);

        Assert.Contains("INTEGRATION PANELS", content);
        Assert.Contains("aria-expanded=\"@IsServicePanelExpanded(group.ServiceKey)\"", content);
        Assert.Contains("@if (IsServicePanelExpanded(group.ServiceKey))", content);
        Assert.Contains("ToggleServicePanel(group.ServiceKey)", content);
        Assert.Contains("▾", content);
        Assert.Contains("▸", content);
    }

    [Fact]
    public void Settings_integrations_page_uses_stronger_panel_structure_with_identity_metrics_and_status_clusters()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/SettingsIntegrations.razor"));
        var content = File.ReadAllText(pagePath);

        Assert.Contains("integration-service-identity", content);
        Assert.Contains("integration-service-monogram", content);
        Assert.Contains("integration-service-summary-grid", content);
        Assert.Contains("integration-service-metric-chip", content);
        Assert.Contains("integration-service-status-cluster", content);
        Assert.Contains("GetServiceMonogram(group.DisplayName)", content);
    }

    [Fact]
    public void Settings_integrations_page_groups_instances_per_service_and_surfaces_prowlarr_detail_summary()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/SettingsIntegrations.razor"));
        var content = File.ReadAllText(pagePath);

        Assert.Contains("GroupInstancesByService()", content);
        Assert.Contains("PROWLARR DETAIL", content);
        Assert.Contains("Indexer control plane + search source orchestration", content);
        Assert.Contains("Attention needed", content);
        Assert.Contains("Healthy", content);
    }
}
