using Xunit;

public sealed class IntegrationOpsFeedbackSourceTests
{
    [Fact]
    public void Integrations_page_uses_snackbar_for_connection_test_feedback_instead_of_inline_message_block()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/Integrations.razor"));
        var content = File.ReadAllText(pagePath);

        Assert.Contains("Snackbar.Add(message, result.Success ? Severity.Success : Severity.Error);", content);
        Assert.DoesNotContain("@if (!string.IsNullOrWhiteSpace(_testMessage))", content);
    }

    [Fact]
    public void Integrations_page_lets_ops_only_integrations_surface_persisted_health_before_falling_back_to_ops_only_label()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/Integrations.razor"));
        var content = File.ReadAllText(pagePath);

        Assert.Contains("if (!string.IsNullOrWhiteSpace(item.LastError) || item.ConsecutiveFailureCount > 0) return \"Attention needed\";", content);
        Assert.Contains("if (item.LastSuccessfulAtUtc.HasValue) return \"Healthy\";", content);
        Assert.Contains("if (item.LastAttemptedAtUtc.HasValue) return \"Awaiting success\";", content);
        Assert.Contains("if (!item.SupportsSync)", content);
        Assert.Contains("return item.ProwlarrSummary is not null ? \"Operational\" : \"Ops-only\";", content);
    }

    [Fact]
    public void Integration_test_route_persists_attempt_and_success_state_for_ops_only_integrations()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var programPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var content = File.ReadAllText(programPath);

        Assert.Contains("app.MapPost(\"/api/integrations/{id:long}/test\"", content);
        Assert.Contains("var state = await db.IntegrationSyncStates.FirstOrDefaultAsync(x => x.IntegrationId == id);", content);
        Assert.Contains("state.LastAttemptedAtUtc = now;", content);
        Assert.Contains("state.LastSuccessfulAtUtc = now;", content);
        Assert.Contains("state.LastError = result.Success ? string.Empty : result.Message;", content);
    }

    [Fact]
    public void Tautulli_connection_test_uses_info_endpoint_that_returns_tautulli_version()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var programPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var content = File.ReadAllText(programPath);

        Assert.Contains("\"tautulli\" => $\"{baseUrl}/api/v2?apikey={Uri.EscapeDataString(config.ApiKey ?? string.Empty)}&cmd=get_tautulli_info\"", content);
    }
}
