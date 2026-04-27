using Xunit;

public sealed class IntegrationFolderMappingsSourceTests
{
    [Fact]
    public void Radarr_integration_detail_page_hosts_mapping_management_instead_of_linking_to_settings_page()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/IntegrationServiceDetails.razor"));
        var content = File.ReadAllText(pagePath);

        Assert.Contains("Add mapping", content);
        Assert.Contains("Edit mapping", content);
        Assert.Contains("Delete mapping", content);
        Assert.Contains("Save mapping", content);
        Assert.Contains("integration-path-mapping-dialog", content);
        Assert.DoesNotContain("/settings/libraries", content);
    }

    [Fact]
    public void Settings_landing_page_no_longer_promotes_library_paths_as_separate_settings_area()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var settingsPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/Settings.razor"));
        var content = File.ReadAllText(settingsPath);

        Assert.DoesNotContain("LIBRARY PATHS", content);
        Assert.DoesNotContain("/settings/libraries", content);
        Assert.DoesNotContain("Physical library folders", content);
    }
}
