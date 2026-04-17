using Xunit;

public sealed class SettingsBehaviorApiRoutesSourceTests
{
    [Fact]
    public void Settings_behavior_routes_exist_for_integration_pulls_and_media_profile_management()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var programPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var content = File.ReadAllText(programPath);

        Assert.Contains("/api/settings/integration-pulls", content);
        Assert.Contains("GetIntegrationPullAutoEnabledAsync", content);
        Assert.Contains("GetIntegrationPullIntervalMinutesAsync", content);
        Assert.Contains("/api/settings/media-profile", content);
        Assert.Contains("/api/settings/media-profile/presets", content);
        Assert.Contains("MediaProfileSettings.LoadCurrentAsync", content);
        Assert.Contains("MediaProfilePresetCatalog.ListAsync", content);
        Assert.Contains("MediaProfilePresetCatalog.SaveCustomAsync", content);
        Assert.Contains("MediaProfilePresetCatalog.RenameCustomAsync", content);
        Assert.Contains("MediaProfilePresetCatalog.DeleteCustomAsync", content);
    }
}
