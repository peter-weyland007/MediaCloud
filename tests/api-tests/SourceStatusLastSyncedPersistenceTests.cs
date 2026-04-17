using Xunit;

public sealed class SourceStatusLastSyncedPersistenceTests
{
    [Fact]
    public void Api_source_status_contract_includes_persisted_last_synced_timestamp()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var content = File.ReadAllText(fullPath);

        Assert.Contains("LibraryItemSourceSyncStates", content);
        Assert.Contains("UpsertLibraryItemSourceSyncStateAsync", content);
        Assert.Contains("DateTimeOffset? LastSyncedAtUtc", content);
        Assert.Contains("GetLibraryItemSourceSyncStateMapAsync", content);
    }
}
