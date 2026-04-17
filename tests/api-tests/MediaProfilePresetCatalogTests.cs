using api.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

public sealed class MediaProfilePresetCatalogTests
{
    [Fact]
    public async Task ListAsync_returns_built_in_presets_and_persisted_custom_presets()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.Parse("2026-04-13T14:00:00Z");
        var custom = new MediaProfileSettingsResponse(
            PreferredContainer: "mkv",
            PreferredVideoCodec: "hevc",
            MaxPreferredResolution: "4k",
            AllowHevc: true,
            Allow10BitVideo: true,
            PreferredAudioCodec: "eac3",
            AllowImageBasedSubtitles: true,
            PreferTextSubtitlesOnly: false,
            MaxPreferredBitrateMbps: 40,
            ActivePresetKey: "custom-theater",
            ActivePresetName: "Theater");

        await MediaProfilePresetCatalog.SaveCustomAsync(db, "Theater", custom, now);
        await db.SaveChangesAsync();

        var presets = await MediaProfilePresetCatalog.ListAsync(db);

        Assert.Contains(presets, x => x.Key == "broad-plex-compatibility" && x.IsBuiltIn);
        Assert.Contains(presets, x => x.Key == "modern-quality-efficiency" && x.IsBuiltIn);
        var theater = Assert.Single(presets, x => x.Key == "custom-theater");
        Assert.False(theater.IsBuiltIn);
        Assert.Equal("Theater", theater.Name);
        Assert.Equal("hevc", theater.Settings.PreferredVideoCodec);
        Assert.True(theater.Settings.Allow10BitVideo);
    }

    [Fact]
    public async Task SaveCustomAsync_slugifies_name_and_can_be_loaded_as_active_preset()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.Parse("2026-04-13T14:05:00Z");
        var defaults = MediaProfilePresetCatalog.BroadPlexCompatibility.Settings;
        var custom = defaults with
        {
            PreferredContainer = "mkv",
            PreferredVideoCodec = "hevc",
            ActivePresetKey = "",
            ActivePresetName = ""
        };

        var saved = await MediaProfilePresetCatalog.SaveCustomAsync(db, "Living Room 4K", custom, now);
        await MediaProfileSettings.SaveCurrentAsync(db, saved.Settings with { ActivePresetKey = saved.Key, ActivePresetName = saved.Name }, now);
        await db.SaveChangesAsync();

        var loaded = await MediaProfileSettings.LoadCurrentAsync(db, MediaProfilePresetCatalog.BroadPlexCompatibility.Settings);

        Assert.Equal("custom-living-room-4k", saved.Key);
        Assert.Equal("Living Room 4K", saved.Name);
        Assert.Equal("custom-living-room-4k", loaded.ActivePresetKey);
        Assert.Equal("Living Room 4K", loaded.ActivePresetName);
        Assert.Equal("hevc", loaded.PreferredVideoCodec);
    }

    [Fact]
    public async Task RenameCustomAsync_updates_name_and_active_preset_metadata()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.Parse("2026-04-13T14:10:00Z");
        var saved = await MediaProfilePresetCatalog.SaveCustomAsync(db, "Theater", MediaProfilePresetCatalog.ModernQualityEfficiency.Settings, now);
        await MediaProfileSettings.SaveCurrentAsync(db, saved.Settings, now);
        await db.SaveChangesAsync();

        var renamed = await MediaProfilePresetCatalog.RenameCustomAsync(db, saved.Key, "Bedroom OLED", now.AddMinutes(1));
        await db.SaveChangesAsync();
        var loaded = await MediaProfileSettings.LoadCurrentAsync(db, MediaProfilePresetCatalog.BroadPlexCompatibility.Settings);

        Assert.Equal(saved.Key, renamed!.Key);
        Assert.Equal("Bedroom OLED", renamed.Name);
        Assert.Equal(saved.Key, loaded.ActivePresetKey);
        Assert.Equal("Bedroom OLED", loaded.ActivePresetName);
    }

    [Fact]
    public async Task SaveCustomAsync_rejects_duplicate_custom_preset_names_case_insensitively()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.Parse("2026-04-13T14:12:00Z");

        await MediaProfilePresetCatalog.SaveCustomAsync(db, "Living Room", MediaProfilePresetCatalog.BroadPlexCompatibility.Settings, now);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MediaProfilePresetCatalog.SaveCustomAsync(db, "living room", MediaProfilePresetCatalog.ModernQualityEfficiency.Settings, now.AddMinutes(1)));

        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public async Task RenameCustomAsync_rejects_duplicate_custom_preset_names_case_insensitively()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.Parse("2026-04-13T14:13:00Z");
        var alpha = await MediaProfilePresetCatalog.SaveCustomAsync(db, "Alpha", MediaProfilePresetCatalog.BroadPlexCompatibility.Settings, now);
        var beta = await MediaProfilePresetCatalog.SaveCustomAsync(db, "Beta", MediaProfilePresetCatalog.ModernQualityEfficiency.Settings, now.AddMinutes(1));
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MediaProfilePresetCatalog.RenameCustomAsync(db, beta.Key, " alpha ", now.AddMinutes(2)));

        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public async Task DeleteCustomAsync_removes_preset_and_clears_active_identity_when_selected()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.Parse("2026-04-13T14:15:00Z");
        var saved = await MediaProfilePresetCatalog.SaveCustomAsync(db, "Travel", MediaProfilePresetCatalog.BroadPlexCompatibility.Settings, now);
        await MediaProfileSettings.SaveCurrentAsync(db, saved.Settings, now);
        await db.SaveChangesAsync();

        var removed = await MediaProfilePresetCatalog.DeleteCustomAsync(db, saved.Key, now.AddMinutes(1));
        await db.SaveChangesAsync();
        var loaded = await MediaProfileSettings.LoadCurrentAsync(db, MediaProfilePresetCatalog.BroadPlexCompatibility.Settings);
        var presets = await MediaProfilePresetCatalog.ListAsync(db);

        Assert.True(removed);
        Assert.DoesNotContain(presets, x => x.Key == saved.Key);
        Assert.Equal(string.Empty, loaded.ActivePresetKey);
        Assert.Equal(string.Empty, loaded.ActivePresetName);
    }

    private static MediaCloudDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<MediaCloudDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        var db = new MediaCloudDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }
}
