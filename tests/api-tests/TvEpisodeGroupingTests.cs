using web.Components.Pages;
using Xunit;

public sealed class TvEpisodeGroupingTests
{
    [Fact]
    public void BuildSeasonGroups_groups_episodes_by_season_and_sorts_them()
    {
        var episodes = new[]
        {
            new TvEpisodeGrouping.TvEpisodeListItem(3, "Episode", "Show — S02E02 — B", "show s02e02 b", true, DateTimeOffset.Parse("2026-04-09T12:00:00Z"), 42, 42, "HD", DateTimeOffset.Parse("2026-04-07T12:00:00Z")),
            new TvEpisodeGrouping.TvEpisodeListItem(1, "Episode", "Show — S01E02 — B", "show s01e02 b", true, DateTimeOffset.Parse("2026-04-08T12:00:00Z"), 42, 42, "HD", DateTimeOffset.Parse("2026-04-07T12:00:00Z")),
            new TvEpisodeGrouping.TvEpisodeListItem(2, "Episode", "Show — S01E01 — A", "show s01e01 a", true, DateTimeOffset.Parse("2026-04-07T12:00:00Z"), 42, 42, "HD", DateTimeOffset.Parse("2026-04-07T12:00:00Z")),
            new TvEpisodeGrouping.TvEpisodeListItem(4, "Episode", "Show — S00E01 — Pilot", "show s00e01 pilot", true, DateTimeOffset.Parse("2026-04-01T12:00:00Z"), 42, 42, "HD", DateTimeOffset.Parse("2026-04-07T12:00:00Z"))
        };

        var groups = TvEpisodeGrouping.BuildSeasonGroups(episodes);

        Assert.Collection(groups,
            specials =>
            {
                Assert.Equal(0, specials.SeasonNumber);
                Assert.Equal("Specials", specials.Label);
                Assert.Single(specials.Episodes);
                Assert.Equal("Show — S00E01 — Pilot", specials.Episodes[0].DisplayTitle);
            },
            season1 =>
            {
                Assert.Equal(1, season1.SeasonNumber);
                Assert.Equal("Season 1", season1.Label);
                Assert.Equal(2, season1.Episodes.Count);
                Assert.Equal("Show — S01E01 — A", season1.Episodes[0].DisplayTitle);
                Assert.Equal("Show — S01E02 — B", season1.Episodes[1].DisplayTitle);
            },
            season2 =>
            {
                Assert.Equal(2, season2.SeasonNumber);
                Assert.Equal("Season 2", season2.Label);
                Assert.Single(season2.Episodes);
            });
    }

    [Fact]
    public void BuildSeasonGroups_can_exclude_specials()
    {
        var episodes = new[]
        {
            new TvEpisodeGrouping.TvEpisodeListItem(4, "Episode", "Show — S00E01 — Pilot", "show s00e01 pilot", true, DateTimeOffset.Parse("2026-04-01T12:00:00Z"), 42, 42, "HD", DateTimeOffset.Parse("2026-04-07T12:00:00Z")),
            new TvEpisodeGrouping.TvEpisodeListItem(2, "Episode", "Show — S01E01 — A", "show s01e01 a", true, DateTimeOffset.Parse("2026-04-07T12:00:00Z"), 42, 42, "HD", DateTimeOffset.Parse("2026-04-07T12:00:00Z"))
        };

        var groups = TvEpisodeGrouping.BuildSeasonGroups(episodes, excludeSpecials: true);

        var group = Assert.Single(groups);
        Assert.Equal(1, group.SeasonNumber);
        Assert.Equal("Season 1", group.Label);
    }

    [Fact]
    public void BuildSeasonGroups_places_unparseable_episodes_in_unknown_bucket()
    {
        var episodes = new[]
        {
            new TvEpisodeGrouping.TvEpisodeListItem(1, "Episode", "Show — Behind the Scenes", "show bonus", false, null, null, null, string.Empty, DateTimeOffset.Parse("2026-04-07T12:00:00Z"))
        };

        var groups = TvEpisodeGrouping.BuildSeasonGroups(episodes);

        var group = Assert.Single(groups);
        Assert.Equal(-1, group.SeasonNumber);
        Assert.Equal("Other Episodes", group.Label);
        Assert.Single(group.Episodes);
    }

    [Fact]
    public void CreateExpandedSeasonNumbers_returns_empty_by_default()
    {
        var groups = new[]
        {
            new TvEpisodeGrouping.SeasonGroup(0, "Specials", []),
            new TvEpisodeGrouping.SeasonGroup(1, "Season 1", []),
            new TvEpisodeGrouping.SeasonGroup(2, "Season 2", [])
        };

        var expanded = TvEpisodeGrouping.CreateExpandedSeasonNumbers(groups, expandAll: false);

        Assert.Empty(expanded);
    }

    [Fact]
    public void CreateExpandedSeasonNumbers_returns_all_seasons_when_expand_all_requested()
    {
        var groups = new[]
        {
            new TvEpisodeGrouping.SeasonGroup(0, "Specials", []),
            new TvEpisodeGrouping.SeasonGroup(1, "Season 1", []),
            new TvEpisodeGrouping.SeasonGroup(2, "Season 2", [])
        };

        var expanded = TvEpisodeGrouping.CreateExpandedSeasonNumbers(groups, expandAll: true);

        Assert.Equal(new[] { 0, 1, 2 }, expanded);
    }

    [Fact]
    public void SeasonGroup_reports_available_episode_counts_and_percent()
    {
        var group = new TvEpisodeGrouping.SeasonGroup(
            1,
            "Season 1",
            new[]
            {
                new TvEpisodeGrouping.TvEpisodeListItem(1, "Episode", "Show — S01E01 — A", "show s01e01 a", true, DateTimeOffset.Parse("2026-04-07T12:00:00Z"), 42, 42, "HD", DateTimeOffset.Parse("2026-04-07T12:00:00Z")),
                new TvEpisodeGrouping.TvEpisodeListItem(2, "Episode", "Show — S01E02 — B", "show s01e02 b", false, DateTimeOffset.Parse("2026-04-08T12:00:00Z"), 42, 42, "HD", DateTimeOffset.Parse("2026-04-07T12:00:00Z")),
                new TvEpisodeGrouping.TvEpisodeListItem(3, "Episode", "Show — S01E03 — C", "show s01e03 c", true, DateTimeOffset.Parse("2026-04-09T12:00:00Z"), 42, 42, "HD", DateTimeOffset.Parse("2026-04-07T12:00:00Z"))
            });

        Assert.Equal(3, group.TotalEpisodeCount);
        Assert.Equal(2, group.AvailableEpisodeCount);
        Assert.Equal(66.66666666666667d, group.AvailabilityPercent);
    }
}
