using Xunit;

public sealed class SettingsBehaviorMediaProfilePresetSourceTests
{
    [Fact]
    public void Settings_behavior_page_includes_named_media_profile_preset_management()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/SettingsBehavior.razor"));
        var content = File.ReadAllText(fullPath);

        Assert.Contains("Save as named preset", content);
        Assert.Contains("Saved presets", content);
        Assert.Contains("Rename", content);
        Assert.Contains("Delete", content);
        Assert.Contains("Custom preset actions", content);
        Assert.Contains("Each saved custom preset has actions on the right", content);
        Assert.Contains("already exists", content);
        Assert.Contains("/api/settings/media-profile/presets", content);
        Assert.DoesNotContain("style=\"", content);
        Assert.Contains("settings-link-button", content);
        Assert.Contains("settings-preset-card", content);
    }
}
