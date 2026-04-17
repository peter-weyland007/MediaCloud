using api;
using Xunit;

public sealed class WatchHistoryMetricsTests
{
    [Fact]
    public void BuildSummary_aggregates_movie_watch_history_metrics()
    {
        var now = new DateTimeOffset(2026, 4, 9, 12, 0, 0, TimeSpan.Zero);
        var events = new[]
        {
            new WatchHistoryEvent(now.AddHours(-6), "Mark", "Apple TV", "Movie A", false, false),
            new WatchHistoryEvent(now.AddHours(-5), "Sarah", "Living Room TV", "Movie A", true, false),
            new WatchHistoryEvent(now.AddHours(-1), "Mark", "Apple TV", "Movie A", false, false)
        };

        var summary = WatchHistoryMetrics.BuildSummary(events, "movie");

        Assert.Equal(3, summary.TotalPlays);
        Assert.Equal(2, summary.DistinctUsers);
        Assert.Equal(2, summary.DirectPlayCount);
        Assert.Equal(1, summary.TranscodeCount);
        Assert.Equal(0, summary.ErrorCount);
        Assert.Equal("Mark", summary.LastWatchedBy);
        Assert.Equal("Movie A", summary.LastWatchedTitle);
        Assert.Equal("Apple TV", summary.TopClient);
        Assert.Equal("Movie A", summary.TopWatchedTitle);
    }

    [Fact]
    public void BuildSummary_aggregates_series_metrics_from_episode_titles()
    {
        var now = new DateTimeOffset(2026, 4, 9, 12, 0, 0, TimeSpan.Zero);
        var events = new[]
        {
            new WatchHistoryEvent(now.AddDays(-3), "Mark", "Roku", "S01E01 - Pilot", false, false),
            new WatchHistoryEvent(now.AddDays(-2), "Mark", "Roku", "S01E02 - The Door", true, false),
            new WatchHistoryEvent(now.AddDays(-1), "Jen", "iPad", "S01E02 - The Door", false, true),
            new WatchHistoryEvent(now, "Jen", "iPad", "S01E03 - Fallout", false, false)
        };

        var summary = WatchHistoryMetrics.BuildSummary(events, "series");

        Assert.Equal(4, summary.TotalPlays);
        Assert.Equal(2, summary.DistinctUsers);
        Assert.Equal(3, summary.DirectPlayCount);
        Assert.Equal(1, summary.TranscodeCount);
        Assert.Equal(1, summary.ErrorCount);
        Assert.Equal("Jen", summary.LastWatchedBy);
        Assert.Equal("S01E03 - Fallout", summary.LastWatchedTitle);
        Assert.Equal("S01E02 - The Door", summary.TopWatchedTitle);
        Assert.Equal("Roku", summary.TopClient);
    }

    [Fact]
    public void BuildSummary_returns_zeroed_metrics_when_no_history_exists()
    {
        var summary = WatchHistoryMetrics.BuildSummary(Array.Empty<WatchHistoryEvent>(), "episode");

        Assert.Equal(0, summary.TotalPlays);
        Assert.Equal(0, summary.DistinctUsers);
        Assert.Equal(0, summary.DirectPlayCount);
        Assert.Equal(0, summary.TranscodeCount);
        Assert.Equal(0, summary.ErrorCount);
        Assert.Null(summary.LastWatchedAtUtc);
        Assert.Equal("—", summary.LastWatchedBy);
        Assert.Equal("—", summary.LastWatchedTitle);
        Assert.Equal("—", summary.TopClient);
        Assert.Equal("—", summary.TopWatchedTitle);
    }
}
