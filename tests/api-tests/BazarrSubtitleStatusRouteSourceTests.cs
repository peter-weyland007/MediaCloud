using Xunit;

public sealed class BazarrSubtitleStatusRouteSourceTests
{
    [Fact]
    public void Api_exposes_bazarr_subtitle_status_route_for_library_items()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var programPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var content = File.ReadAllText(programPath);

        Assert.Contains("/api/library/items/{id:long}/bazarr-subtitles", content);
        Assert.Contains("BazarrSubtitleStatusResolver.ResolveTarget", content);
        Assert.Contains("\"movie\" => \"movies\"", content);
        Assert.Contains("\"episode\" => \"episodes\"", content);
        Assert.Contains("\"series\" => \"series\"", content);
        Assert.Contains("BazarrSubtitleStatusResolver.BuildMovieStatus", content);
        Assert.Contains("BazarrSubtitleStatusResolver.BuildSeriesStatus", content);
    }
}
