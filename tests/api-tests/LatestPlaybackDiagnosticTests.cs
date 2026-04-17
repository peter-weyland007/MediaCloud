using api;
using api.Data;
using api.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

public sealed class LatestPlaybackDiagnosticTests
{
    [Fact]
    public async Task LoadLatestAsync_returns_most_recent_diagnostic_without_sqlite_orderby_datetimeoffset_translation()
    {
        await using var db = CreateDb();
        var item = new LibraryItem
        {
            CanonicalKey = "episode:tvdb:8827503",
            MediaType = "Episode",
            Title = "1883 — S01E09 — Racing Clouds",
            SortTitle = "1883 s01e09 Racing Clouds",
            QualityProfile = "Any"
        };

        db.LibraryItems.Add(item);
        await db.SaveChangesAsync();

        db.PlaybackDiagnosticEntries.Add(new PlaybackDiagnosticEntry
        {
            LibraryItemId = item.Id,
            SourceService = "tautulli",
            ExternalId = "older",
            OccurredAtUtc = new DateTimeOffset(2026, 4, 10, 12, 0, 0, TimeSpan.Zero),
            ImportedAtUtc = new DateTimeOffset(2026, 4, 10, 12, 0, 5, TimeSpan.Zero),
            Summary = "Older"
        });
        db.PlaybackDiagnosticEntries.Add(new PlaybackDiagnosticEntry
        {
            LibraryItemId = item.Id,
            SourceService = "tautulli",
            ExternalId = "newest",
            OccurredAtUtc = new DateTimeOffset(2026, 4, 10, 13, 0, 0, TimeSpan.Zero),
            ImportedAtUtc = new DateTimeOffset(2026, 4, 10, 13, 0, 5, TimeSpan.Zero),
            Summary = "Newest"
        });
        await db.SaveChangesAsync();

        var latest = await LatestPlaybackDiagnostic.LoadLatestAsync(db, item.Id);

        Assert.NotNull(latest);
        Assert.Equal("Newest", latest!.Summary);
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
