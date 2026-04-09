using api.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

public sealed class TvDisplaySettingsTests
{
    [Fact]
    public async Task LoadAsync_returns_default_when_setting_missing()
    {
        await using var db = CreateDb();

        var settings = await TvDisplaySettings.LoadAsync(db, "tv.hide_specials_by_default", fallbackHideSpecials: false);

        Assert.False(settings.HideSpecialsByDefault);
    }

    [Fact]
    public async Task UpsertAsync_persists_hide_specials_value()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.Parse("2026-04-07T20:00:00Z");

        await TvDisplaySettings.UpsertAsync(db, "tv.hide_specials_by_default", hideSpecialsByDefault: true, now);
        await db.SaveChangesAsync();

        var settings = await TvDisplaySettings.LoadAsync(db, "tv.hide_specials_by_default", fallbackHideSpecials: false);

        Assert.True(settings.HideSpecialsByDefault);
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
