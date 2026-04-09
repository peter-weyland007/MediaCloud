using api.Models;
using Xunit;

public sealed class TelevisionGroupingTests
{
    [Fact]
    public void BuildEpisodeScopePrefixForSeries_prefers_tvdb_scope_when_available()
    {
        var series = new LibraryItem
        {
            MediaType = "Series",
            TvdbId = 12345,
            Title = "12 Monkeys"
        };

        var prefix = TelevisionGrouping.BuildEpisodeScopePrefixForSeries(series);

        Assert.Equal("episode:tvdb:12345:", prefix);
    }

    [Fact]
    public void BuildEpisodeScopePrefixForSeries_falls_back_to_normalized_title_scope()
    {
        var series = new LibraryItem
        {
            MediaType = "Series",
            TvdbId = null,
            Title = "Star Trek: Picard"
        };

        var prefix = TelevisionGrouping.BuildEpisodeScopePrefixForSeries(series);

        Assert.Equal("episode:title:startrekpicard:", prefix);
    }

    [Fact]
    public void BuildEpisodeScopePrefixForSeries_does_not_replace_title_based_episode_listing_fallback()
    {
        var series = new LibraryItem
        {
            MediaType = "Series",
            TvdbId = 396390,
            Title = "1883",
            SortTitle = "1883"
        };

        var scopePrefix = TelevisionGrouping.BuildEpisodeScopePrefixForSeries(series);
        var titlePrefix = $"{series.Title} — S";
        var sortPrefix = $"{series.SortTitle} s";

        Assert.Equal("episode:tvdb:396390:", scopePrefix);
        Assert.Equal("1883 — S", titlePrefix);
        Assert.Equal("1883 s", sortPrefix);
    }
}
