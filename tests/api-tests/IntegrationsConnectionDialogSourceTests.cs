using Xunit;

public sealed class IntegrationsConnectionDialogSourceTests
{
    [Fact]
    public void Integrations_page_duplicates_connection_setup_surface_with_table_and_add_button()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/Integrations.razor"));
        var content = File.ReadAllText(pagePath);

        Assert.Contains("SET UP INTEGRATIONS", content);
        Assert.Contains("Add integration", content);
        Assert.Contains("<table", content);
        Assert.Contains("OpenCreateIntegrationDialog", content);
        Assert.Contains("OpenEditIntegrationDialog(item)", content);
        Assert.Contains("BuildEnabledPillStyle", content);
        Assert.Contains("BuildModePillStyle", content);
    }

    [Fact]
    public void Integrations_page_exposes_quick_filter_above_setup_table()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/Integrations.razor"));
        var content = File.ReadAllText(pagePath);

        Assert.Contains("QUICK FILTER", content);
        Assert.Contains("integration-quick-filter", content);
        Assert.Contains("_tableFilter", content);
        Assert.Contains("FilteredInstances()", content);
        Assert.Contains("ClearTableFilter", content);
        Assert.Contains("No integration instances match the current filter.", content);
    }

    [Fact]
    public void Integrations_page_uses_inline_mud_dialog_for_add_and_edit_connection_forms()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/Integrations.razor"));
        var content = File.ReadAllText(pagePath);

        Assert.Contains("<MudDialog Class=\"integration-dialog\" @bind-Visible=\"_integrationDialogVisible\"", content);
        Assert.Contains("FormName=\"integration-dialog-form\"", content);
        Assert.Contains("CloseIntegrationDialog", content);
        Assert.Contains("SaveIntegrationAsync", content);
    }

    [Fact]
    public void Integrations_page_table_exposes_test_and_sync_actions_for_row_level_operations()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/Integrations.razor"));
        var content = File.ReadAllText(pagePath);

        Assert.Contains("SyncIntegrationAsync(item)", content);
        Assert.Contains("TestIntegrationAsync(item)", content);
        Assert.Contains("_syncingId == item.Id ? \"Syncing...\" : \"Sync\"", content);
        Assert.Contains("_testingId == item.Id ? \"Testing...\" : \"Test\"", content);
        Assert.Contains("private sealed record TriggerIntegrationSyncResponse", content);
        Assert.Contains("private sealed record IntegrationTestResponse", content);
    }
}
