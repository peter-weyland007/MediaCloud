using Xunit;

public sealed class MediaProfilePresetSlugCollisionSourceTests
{
    [Fact]
    public void Saving_custom_media_profile_presets_checks_slug_key_collisions_not_just_display_names()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var settingsPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/MediaProfileSettings.cs"));
        var content = File.ReadAllText(settingsPath);

        Assert.Contains("var presetKey = BuildCustomKey(normalizedName);", content);
        Assert.Contains("EnsureCustomNameAvailableAsync(db, normalizedName, candidatePresetKey: presetKey)", content);
        Assert.Contains("string? candidatePresetKey = null", content);
        Assert.Contains("A custom media profile preset with key", content);
    }
}
