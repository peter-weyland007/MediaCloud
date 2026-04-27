using Xunit;

public sealed class MudButtonGroupSourceTests
{
    [Theory]
    [InlineData("apps/web/Components/Pages/Integrations.razor")]
    [InlineData("apps/web/Components/Pages/IntegrationServiceDetails.razor")]
    [InlineData("apps/web/Components/Pages/SettingsUsers.razor")]
    [InlineData("apps/web/Components/Pages/SettingsLibraries.razor")]
    [InlineData("apps/web/Components/Pages/SettingsRuntimePolicy.razor")]
    [InlineData("apps/web/Components/Pages/Issues.razor")]
    public void Key_pages_use_mud_button_groups_for_clustered_actions(string relativePath)
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, relativePath));
        var content = File.ReadAllText(fullPath);

        Assert.Contains("MudButtonGroup", content);
    }

    [Fact]
    public void Integrations_page_uses_mud_button_group_for_actions_column()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/Integrations.razor"));
        var content = File.ReadAllText(fullPath);

        Assert.Contains("<MudButtonGroup Variant=\"Variant.Filled\" Color=\"Color.Primary\" Size=\"Size.Small\">", content);
        Assert.Contains("OpenEditIntegrationDialog(item)", content);
        Assert.Contains("SyncIntegrationAsync(item)", content);
        Assert.Contains("TestIntegrationAsync(item)", content);
        Assert.Contains("OpenDeleteIntegrationDialog(item)", content);
        Assert.Contains("ConfirmDeleteIntegrationAsync", content);
    }
}
