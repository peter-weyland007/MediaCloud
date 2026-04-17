using api;
using api.Data;
using api.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

public sealed class MediaCompatibilityQueueManagementTests
{
    [Fact]
    public async Task QueuePreviewAsync_reuses_existing_active_job_instead_of_creating_duplicate()
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
            Reason = "existing",
            ReasonCategory = "compatibility",
            Confidence = "medium",
            PolicySummary = "existing",
            NotesHandling = "preview_queue_only",
            ProfileDecision = "sidecar",
            ProfileSummary = "Target MP4",
            Status = "Queued",
            SearchStatus = "NotApplicable",
            BlacklistStatus = "NotApplicable",
            OutcomeSummary = "existing",
            ReleaseSummary = "Queue safe container remux",
            RequestedBy = "admin",
            RequestedAtUtc = new DateTimeOffset(2026, 4, 15, 10, 0, 0, TimeSpan.Zero),
            UpdatedAtUtc = new DateTimeOffset(2026, 4, 15, 10, 0, 0, TimeSpan.Zero)
        });
        await db.SaveChangesAsync();

        var recommendation = BuildRecommendation();
        var result = await MediaCompatibilityQueueManagement.QueuePreviewAsync(db, recommendation, "admin", new DateTimeOffset(2026, 4, 15, 11, 0, 0, TimeSpan.Zero));

        Assert.False(result.Queued);
        Assert.True(result.AlreadyQueued);
        Assert.Equal(1, await db.LibraryRemediationJobs.CountAsync());
        Assert.Equal("A matching FFmpeg compatibility remediation is already queued or running for this item.", result.Message);
    }

    [Fact]
    public async Task RemoveQueuedPreviewJobAsync_removes_only_queued_compatibility_jobs()
    {
        await using var db = CreateDb();
        var queued = new LibraryRemediationJob
        {
            LibraryItemId = 1474,
            ServiceKey = "ffmpeg",
            ServiceDisplayName = "FFmpeg",
            RequestedAction = "container_remux_sidecar",
            CommandName = "ffmpeg-compat-preview",
            IssueType = "playback_compatibility",
            Reason = "queued",
            ReasonCategory = "compatibility",
            Confidence = "medium",
            PolicySummary = "queued",
            NotesHandling = "preview_queue_only",
            ProfileDecision = "sidecar",
            ProfileSummary = "Target MP4",
            Status = "Queued",
            SearchStatus = "NotApplicable",
            BlacklistStatus = "NotApplicable",
            OutcomeSummary = "queued",
            ReleaseSummary = "Queue safe container remux",
            RequestedBy = "admin",
            RequestedAtUtc = new DateTimeOffset(2026, 4, 15, 10, 0, 0, TimeSpan.Zero),
            UpdatedAtUtc = new DateTimeOffset(2026, 4, 15, 10, 0, 0, TimeSpan.Zero)
        };
        db.LibraryRemediationJobs.Add(queued);
        await db.SaveChangesAsync();

        var result = await MediaCompatibilityQueueManagement.RemoveQueuedPreviewJobAsync(db, queued.Id);

        Assert.True(result.Removed);
        Assert.Equal(0, await db.LibraryRemediationJobs.CountAsync());
        Assert.Equal("Removed queued FFmpeg compatibility job.", result.Message);
    }

    [Fact]
    public async Task RemoveQueuedPreviewJobAsync_rejects_nonqueued_jobs()
    {
        await using var db = CreateDb();
        var running = new LibraryRemediationJob
        {
            LibraryItemId = 1474,
            ServiceKey = "ffmpeg",
            ServiceDisplayName = "FFmpeg",
            RequestedAction = "container_remux_sidecar",
            CommandName = "ffmpeg-compat-preview",
            IssueType = "playback_compatibility",
            Reason = "running",
            ReasonCategory = "compatibility",
            Confidence = "medium",
            PolicySummary = "running",
            NotesHandling = "preview_queue_only",
            ProfileDecision = "sidecar",
            ProfileSummary = "Target MP4",
            Status = "Running",
            SearchStatus = "NotApplicable",
            BlacklistStatus = "NotApplicable",
            OutcomeSummary = "running",
            ReleaseSummary = "Queue safe container remux",
            RequestedBy = "admin",
            RequestedAtUtc = new DateTimeOffset(2026, 4, 15, 10, 0, 0, TimeSpan.Zero),
            UpdatedAtUtc = new DateTimeOffset(2026, 4, 15, 10, 0, 0, TimeSpan.Zero)
        };
        db.LibraryRemediationJobs.Add(running);
        await db.SaveChangesAsync();

        var result = await MediaCompatibilityQueueManagement.RemoveQueuedPreviewJobAsync(db, running.Id);

        Assert.False(result.Removed);
        Assert.Equal(1, await db.LibraryRemediationJobs.CountAsync());
        Assert.Equal("Only queued FFmpeg compatibility jobs can be removed from the queue.", result.Message);
    }

    private static MediaCompatibilityRecommendationResponse BuildRecommendation()
        => new(
            1474,
            true,
            true,
            "container_remux_sidecar",
            "Queue safe container remux",
            "medium",
            "why",
            "benefit",
            "risk",
            "output",
            "ffmpeg -i input.mkv output.mp4",
            "Stable / Broad Compatibility",
            "Target MP4",
            [],
            [],
            string.Empty,
            string.Empty,
            [],
            [],
            string.Empty);

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
