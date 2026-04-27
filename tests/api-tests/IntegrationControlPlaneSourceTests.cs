using Xunit;

public sealed class IntegrationControlPlaneSourceTests
{
    [Fact]
    public void Integration_contracts_include_control_plane_health_fields()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var apiPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var webPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Services/Auth/AuthUser.cs"));
        var apiContent = File.ReadAllText(apiPath);
        var webContent = File.ReadAllText(webPath);

        Assert.Contains("string RoleSummary", apiContent);
        Assert.Contains("DateTimeOffset? LastAttemptedAtUtc", apiContent);
        Assert.Contains("DateTimeOffset? LastSuccessfulAtUtc", apiContent);
        Assert.Contains("string LastError", apiContent);
        Assert.Contains("int ConsecutiveFailureCount", apiContent);
        Assert.Contains("bool SupportsSync", apiContent);

        Assert.Contains("string RoleSummary", webContent);
        Assert.Contains("DateTimeOffset? LastAttemptedAtUtc", webContent);
        Assert.Contains("DateTimeOffset? LastSuccessfulAtUtc", webContent);
        Assert.Contains("string LastError", webContent);
        Assert.Contains("int ConsecutiveFailureCount", webContent);
        Assert.Contains("bool SupportsSync", webContent);
    }

    [Fact]
    public void Prowlarr_has_explicit_control_plane_role_summary()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var apiPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var apiContent = File.ReadAllText(apiPath);

        Assert.Contains("\"prowlarr\" => \"Indexer control plane + search source orchestration\"", apiContent);
    }

    [Fact]
    public void Integrations_page_surfaces_control_plane_summary_health_and_sync_actions()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/Integrations.razor"));
        var content = File.ReadAllText(pagePath);

        Assert.Contains("CONTROL PLANE SUMMARY", content);
        Assert.Contains("Role", content);
        Assert.Contains("Health", content);
        Assert.Contains("Attention needed", content);
        Assert.Contains("/api/integrations/{item.Id}/sync", content);
        Assert.Contains("Syncing...", content);
        Assert.Contains("!item.SupportsSync", content);
        Assert.Contains("Ops-only", content);
    }
}
