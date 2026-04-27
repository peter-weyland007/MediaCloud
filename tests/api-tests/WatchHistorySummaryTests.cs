using api;
using api.Models;
using Xunit;

public sealed class WatchHistorySummaryTests
{
    [Fact]
    public void Build_returns_no_evidence_summary_when_no_watch_history_exists()
    {
        var summary = StoredWatchHistorySummaryBuilder.Build(42, Array.Empty<WatchHistoryEntry>());

        Assert.Equal(42, summary.LibraryItemId);
        Assert.Equal(0, summary.TotalCount);
        Assert.Equal(0, summary.DirectPlayCount);
        Assert.Equal(0, summary.TranscodeCount);
        Assert.Equal(0, summary.ErrorCount);
        Assert.Equal(0, summary.DistinctUserCount);
        Assert.Equal("NoEvidence", summary.State);
    }

    [Fact]
    public void Build_counts_direct_play_transcode_and_distinct_users_from_internal_history()
    {
        var rows = new[]
        {
            new WatchHistoryEntry
            {
                LibraryItemId = 42,
                SourceService = "tautulli",
                ExternalId = "a",
                OccurredAtUtc = new DateTimeOffset(2026, 4, 20, 1, 0, 0, TimeSpan.Zero),
                ImportedAtUtc = new DateTimeOffset(2026, 4, 20, 2, 0, 0, TimeSpan.Zero),
                UserName = "Mark",
                ClientName = "Living Room TV",
                Decision = "direct play",
                TranscodeDecision = string.Empty,
                ErrorMessage = string.Empty
            },
            new WatchHistoryEntry
            {
                LibraryItemId = 42,
                SourceService = "tautulli",
                ExternalId = "b",
                OccurredAtUtc = new DateTimeOffset(2026, 4, 21, 1, 0, 0, TimeSpan.Zero),
                ImportedAtUtc = new DateTimeOffset(2026, 4, 21, 2, 0, 0, TimeSpan.Zero),
                UserName = "Mark",
                ClientName = "Living Room TV",
                Decision = "transcode",
                TranscodeDecision = "transcode",
                ErrorMessage = string.Empty
            },
            new WatchHistoryEntry
            {
                LibraryItemId = 42,
                SourceService = "plex",
                ExternalId = "c",
                OccurredAtUtc = new DateTimeOffset(2026, 4, 22, 1, 0, 0, TimeSpan.Zero),
                ImportedAtUtc = new DateTimeOffset(2026, 4, 22, 2, 0, 0, TimeSpan.Zero),
                UserName = "Guest",
                ClientName = "Bedroom Roku",
                Decision = "copy",
                TranscodeDecision = string.Empty,
                ErrorMessage = "playback failed"
            }
        };

        var summary = StoredWatchHistorySummaryBuilder.Build(42, rows);

        Assert.Equal(3, summary.TotalCount);
        Assert.Equal(1, summary.DirectPlayCount);
        Assert.Equal(1, summary.TranscodeCount);
        Assert.Equal(1, summary.ErrorCount);
        Assert.Equal(2, summary.DistinctUserCount);
        Assert.Equal("Guest", summary.LastWatchedBy);
        Assert.Equal("Living Room TV", summary.TopClient);
        Assert.Equal("IssuesObserved", summary.State);
    }
}
