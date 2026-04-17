using Xunit;

public sealed class ProwlarrHealthSummarySourceTests
{
    [Fact]
    public void Integration_contracts_include_optional_prowlarr_summary()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var apiPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var webPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Services/Auth/AuthUser.cs"));
        var apiContent = File.ReadAllText(apiPath);
        var webContent = File.ReadAllText(webPath);

        Assert.Contains("ProwlarrSummaryResponse? ProwlarrSummary", apiContent);
        Assert.Contains("ProwlarrSummaryResponse? ProwlarrSummary", webContent);
        Assert.Contains("public record ProwlarrSummaryResponse(", apiContent);
        Assert.Contains("public record ProwlarrSummaryResponse(", webContent);
    }

    [Fact]
    public void Api_sources_prowlarr_operational_health_from_indexer_application_and_health_endpoints()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var apiPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var apiContent = File.ReadAllText(apiPath);

        Assert.Contains("/api/v1/indexerstatus", apiContent);
        Assert.Contains("/api/v1/applications", apiContent);
        Assert.Contains("/api/v1/health", apiContent);
        Assert.Contains("TryGetProwlarrSummaryAsync", apiContent);
    }

    [Fact]
    public void Prowlarr_is_classified_as_ops_only_instead_of_sync_backed()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var apiPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var apiContent = File.ReadAllText(apiPath);

        Assert.Contains("IntegrationCatalog.SupportsSync(row.ServiceKey)", apiContent);
        Assert.Contains("IntegrationCatalog.SupportsSync(entity.ServiceKey)", apiContent);
        Assert.Contains("IntegrationCatalog.SupportsSync(integration.ServiceKey)", apiContent);
        Assert.Contains("ops/health integration only", apiContent);
    }

    [Fact]
    public void Settings_integrations_page_renders_prowlarr_operational_counts()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/SettingsIntegrations.razor"));
        var content = File.ReadAllText(pagePath);

        Assert.Contains("Configured indexers", content);
        Assert.Contains("Unavailable indexers", content);
        Assert.Contains("Application links", content);
        Assert.Contains("Health issues", content);
        Assert.Contains("item.ProwlarrSummary", content);
        Assert.Contains("Operational", content);
    }
}
