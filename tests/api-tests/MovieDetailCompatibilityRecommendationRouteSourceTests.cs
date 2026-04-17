using Xunit;

public sealed class MovieDetailCompatibilityRecommendationRouteSourceTests
{
    [Fact]
    public void Movie_detail_profile_match_matrix_route_is_exposed_by_the_api()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var programPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var webPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/MovieDetails.razor"));

        var programContent = File.ReadAllText(programPath);
        var webContent = File.ReadAllText(webPath);

        Assert.Contains("/api/library/items/{id:long}/media-compatibility-recommendation", programContent);
        Assert.Contains("MediaProfileSettings.LoadCurrentAsync", programContent);
        Assert.Contains("MediaCompatibilityRecommendationEngine.Build(item, details, latestDiagnostic, settings)", programContent);
        Assert.Contains("/api/library/items/{Id}/media-compatibility-recommendation", webContent);
    }
}
