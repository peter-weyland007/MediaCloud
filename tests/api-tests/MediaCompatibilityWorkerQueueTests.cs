using api;
using api.Data;
using api.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

public sealed class MediaCompatibilityWorkerQueueTests
{
    [Fact]
    public async Task LoadNextQueuedPreviewJobAsync_returns_oldest_queued_job_without_sqlite_datetimeoffset_orderby_translation()
    {
        await using var db = CreateDb();
        db.LibraryRemediationJobs.Add(new LibraryRemediationJob
        {
            LibraryItemId = 1474,
            ServiceKey = "ffmpeg",
            ServiceDisplayName = "FFmpeg",
            RequestedAction = "container_remux_sidecar",
            CommandName = "ffmpeg-compat-preview",
            IssueType = "playback_compatibility",
            Reason = "newer",
            ReasonCategory = "compatibility",
            Confidence = "medium",
            PolicySummary = "newer",
            NotesHandling = "preview_queue_only",
            ProfileDecision = "sidecar",
            ProfileSummary = "Target MP4",
            Status = "Queued",
            SearchStatus = "NotApplicable",
            BlacklistStatus = "NotApplicable",
            OutcomeSummary = "newer",
            ReleaseSummary = "Queue safe container remux",
            RequestedBy = "admin",
            RequestedAtUtc = new DateTimeOffset(2026, 4, 15, 11, 0, 0, TimeSpan.Zero),
            UpdatedAtUtc = new DateTimeOffset(2026, 4, 15, 11, 0, 0, TimeSpan.Zero)
        });
        db.LibraryRemediationJobs.Add(new LibraryRemediationJob
        {
            LibraryItemId = 1474,
            ServiceKey = "ffmpeg",
            ServiceDisplayName = "FFmpeg",
            RequestedAction = "container_remux_sidecar",
            CommandName = "ffmpeg-compat-preview",
            IssueType = "playback_compatibility",
            Reason = "older",
            ReasonCategory = "compatibility",
            Confidence = "medium",
            PolicySummary = "older",
            NotesHandling = "preview_queue_only",
            ProfileDecision = "sidecar",
            ProfileSummary = "Target MP4",
            Status = "Queued",
            SearchStatus = "NotApplicable",
            BlacklistStatus = "NotApplicable",
            OutcomeSummary = "older",
            ReleaseSummary = "Queue safe container remux",
            RequestedBy = "admin",
            RequestedAtUtc = new DateTimeOffset(2026, 4, 15, 10, 0, 0, TimeSpan.Zero),
            UpdatedAtUtc = new DateTimeOffset(2026, 4, 15, 10, 0, 0, TimeSpan.Zero)
        });
        await db.SaveChangesAsync();

        var next = await MediaCompatibilityExecution.LoadNextQueuedPreviewJobAsync(db, CancellationToken.None);

        Assert.NotNull(next);
        Assert.Equal("older", next!.Reason);
    }

    [Fact]
    public void TryBuildProgressSnapshot_parses_ffmpeg_progress_into_percent_summary_and_eta()
    {
        var snapshot = MediaCompatibilityExecution.TryBuildProgressSnapshot(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["out_time"] = "00:00:30.000000",
                ["speed"] = "2.0x",
                ["progress"] = "continue"
            },
            120d);

        Assert.NotNull(snapshot);
        Assert.Equal(25d, snapshot!.Percent.GetValueOrDefault(), 3);
        Assert.Equal(30d, snapshot.ProcessedSeconds.GetValueOrDefault(), 3);
        Assert.Equal(120d, snapshot.TotalSeconds.GetValueOrDefault(), 3);
        Assert.Equal(45d, snapshot.EtaSeconds.GetValueOrDefault(), 3);
        Assert.Equal("2.0x", snapshot.Speed);
        Assert.Contains("25%", snapshot.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("00:00:30 / 00:02:00", snapshot.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ETA 00:00:45", snapshot.Summary, StringComparison.OrdinalIgnoreCase);
    }

    private static MediaCloudDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<MediaCloudDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        var db = new MediaCloudDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }
}
