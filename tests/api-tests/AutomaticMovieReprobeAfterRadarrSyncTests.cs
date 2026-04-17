using Xunit;

public sealed class AutomaticMovieReprobeAfterRadarrSyncTests
{
    [Fact]
    public void Automatic_integration_pull_queues_movie_runtime_reprobe_after_radarr_sync()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var content = File.ReadAllText(fullPath);

        Assert.Contains("string.Equals(integration.ServiceKey, \"radarr\"", content);
        Assert.Contains("new BatchRuntimeReprobeRequest(\"Movie\", 5000, false)", content);
        Assert.Contains("Queued movie runtime probe after Radarr sync...", content);
    }
}
