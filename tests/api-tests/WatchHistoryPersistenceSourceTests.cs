using Xunit;

public sealed class WatchHistoryPersistenceSourceTests
{
    [Fact]
    public void Api_defines_internal_watch_history_storage_and_routes()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var dbContextPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Data/MediaCloudDbContext.cs"));
        var programPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var dbContent = File.ReadAllText(dbContextPath);
        var programContent = File.ReadAllText(programPath);

        Assert.Contains("DbSet<WatchHistoryEntry>", dbContent);
        Assert.Contains("modelBuilder.Entity<WatchHistoryEntry>", dbContent);
        Assert.Contains("CREATE TABLE IF NOT EXISTS WatchHistoryEntries", programContent);
        Assert.Contains("/api/library/items/{id:long}/watch-history", programContent);
        Assert.Contains("/api/library/items/{id:long}/watch-history/pull", programContent);
        Assert.Contains("UpsertWatchHistoryEntryAsync", programContent);
    }
}
