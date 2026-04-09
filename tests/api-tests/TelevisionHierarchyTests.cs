using api;
using api.Models;
using Xunit;

public sealed class TelevisionHierarchyTests
{
    [Fact]
    public void TryFindParentSeries_prefers_series_with_matching_tvdb_scope_prefix()
    {
        var episode = new LibraryItem
        {
            MediaType = "Episode",
            CanonicalKey = "episode:tvdb:396390:s01:e01",
            Title = "1883 — S01E01 — 1883"
        };

        var series = new[]
        {
            new LibraryItem { Id = 2204, MediaType = "Series", CanonicalKey = "series:tvdb:396390", Title = "1883" },
            new LibraryItem { Id = 9999, MediaType = "Series", CanonicalKey = "series:title:1883", Title = "1883 (duplicate)" }
        };

        var parent = TelevisionHierarchy.TryFindParentSeries(episode, series);

        Assert.NotNull(parent);
        Assert.Equal(2204, parent!.Id);
    }

    [Fact]
    public void TryFindParentSeries_falls_back_to_title_prefix_when_episode_scope_is_title_based()
    {
        var episode = new LibraryItem
        {
            MediaType = "Episode",
            CanonicalKey = "episode:title:startrekpicard:s01:e01",
            Title = "Star Trek: Picard — S01E01 — Remembrance"
        };

        var series = new[]
        {
            new LibraryItem { Id = 77, MediaType = "Series", CanonicalKey = "series:title:startrekpicard", Title = "Star Trek: Picard" }
        };

        var parent = TelevisionHierarchy.TryFindParentSeries(episode, series);

        Assert.NotNull(parent);
        Assert.Equal(77, parent!.Id);
    }
}
