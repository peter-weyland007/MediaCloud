using Xunit;

public sealed class MovieDetailPlaybackSmokeTestRouteSourceTests
{
    [Fact]
    public void Movie_detail_playback_smoke_test_route_and_ui_action_are_wired()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var programPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var webPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/MovieDetails.razor"));

        var programContent = File.ReadAllText(programPath);
        var webContent = File.ReadAllText(webPath);

        Assert.Contains("/api/library/items/{id:long}/playback-smoke-test", programContent);
        Assert.Contains("MediaPlaybackSmokeTestEngine.BuildPlan", programContent);
        Assert.Contains("RunPlaybackSmokeTestAsync", webContent);
        Assert.Contains("Run playback smoke test", webContent);
        Assert.Contains("PLAYBACK SMOKE TEST", webContent);
    }
}
