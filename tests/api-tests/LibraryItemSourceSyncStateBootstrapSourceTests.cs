using Xunit;

public sealed class LibraryItemSourceSyncStateBootstrapSourceTests
{
    [Fact]
    public void Startup_bootstrap_creates_library_item_source_sync_state_table_for_existing_sqlite_databases()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var programPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var content = File.ReadAllText(programPath);

        Assert.Contains("CREATE TABLE IF NOT EXISTS LibraryItemSourceSyncStates", content);
        Assert.Contains("IX_LibraryItemSourceSyncStates_LibraryItemId_IntegrationId", content);
        Assert.Contains("IX_LibraryItemSourceSyncStates_IntegrationId", content);
    }
}
