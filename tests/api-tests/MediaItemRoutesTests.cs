using web.Components.Shared;
using Xunit;

public sealed class MediaItemRoutesTests
{
    [Theory]
    [InlineData("Movie", 42, "/media/movies/42")]
    [InlineData("Series", 42, "/media/tv-shows/42")]
    [InlineData("Episode", 42, "/media/tv-shows/42")]
    [InlineData("Album", 42, "/media/music/42")]
    [InlineData("Track", 42, "/media/music/42")]
    public void GetDetailsHref_returns_expected_route_for_media_type(string mediaType, long id, string expected)
    {
        var href = MediaItemRoutes.GetDetailsHref(mediaType, id);

        Assert.Equal(expected, href);
    }

    [Theory]
    [InlineData("Movie", "Movie #42")]
    [InlineData("Series", "Series #42")]
    [InlineData("Episode", "Episode #42")]
    [InlineData("Album", "Album #42")]
    [InlineData("Track", "Track #42")]
    public void GetFallbackDisplayName_returns_media_specific_label(string mediaType, string expected)
    {
        var displayName = MediaItemRoutes.GetFallbackDisplayName(mediaType, 42);

        Assert.Equal(expected, displayName);
    }
}
