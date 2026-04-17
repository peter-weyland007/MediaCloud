using Xunit;

public sealed class IntegrationVersionPersistenceSourceTests
{
    [Fact]
    public void Integration_configs_schema_and_model_include_persisted_version_fields()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var programPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var modelPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Models/IntegrationConfig.cs"));
        var dbContextPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Data/MediaCloudDbContext.cs"));

        var programContent = File.ReadAllText(programPath);
        var modelContent = File.ReadAllText(modelPath);
        var dbContextContent = File.ReadAllText(dbContextPath);

        Assert.Contains("CurrentVersion TEXT NOT NULL DEFAULT ''", programContent);
        Assert.Contains("LatestReleaseVersion TEXT NOT NULL DEFAULT ''", programContent);
        Assert.Contains("EnsureSqliteColumn(db, \"IntegrationConfigs\", \"CurrentVersion\"", programContent);
        Assert.Contains("EnsureSqliteColumn(db, \"IntegrationConfigs\", \"LatestReleaseVersion\"", programContent);

        Assert.Contains("public string CurrentVersion { get; set; } = string.Empty;", modelContent);
        Assert.Contains("public string LatestReleaseVersion { get; set; } = string.Empty;", modelContent);
        Assert.Contains("entity.Property(x => x.CurrentVersion).HasMaxLength(128).IsRequired();", dbContextContent);
        Assert.Contains("entity.Property(x => x.LatestReleaseVersion).HasMaxLength(128).IsRequired();", dbContextContent);
    }

    [Fact]
    public void Integration_read_and_test_endpoints_persist_and_return_real_version_fields()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var programPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var programContent = File.ReadAllText(programPath);

        Assert.Contains("row.CurrentVersion", programContent);
        Assert.Contains("row.LatestReleaseVersion", programContent);
        Assert.Contains("RefreshIntegrationVersionMetadataAsync(row, httpClientFactory)", programContent);
        Assert.Contains("config.CurrentVersion = result.Version;", programContent);
        Assert.Contains("config.LatestReleaseVersion = latestReleaseVersion;", programContent);
        Assert.Contains("FetchLatestReleaseVersionAsync", programContent);
    }
}
