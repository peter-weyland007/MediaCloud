using api.Data;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api;

public sealed record MediaCompatibilityQueueResult(long JobId, bool Queued, bool AlreadyQueued, string Message);
public sealed record MediaCompatibilityQueueRemovalResult(long JobId, bool Removed, string Message);

public static class MediaCompatibilityQueueManagement
{
    public static async Task<MediaCompatibilityQueueResult> QueuePreviewAsync(MediaCloudDbContext db, MediaCompatibilityRecommendationResponse recommendation, string actor, DateTimeOffset now)
    {
        var existing = await db.LibraryRemediationJobs
            .Where(x => x.LibraryItemId == recommendation.LibraryItemId)
            .Where(x => x.ServiceKey == "ffmpeg")
            .Where(x => x.CommandName == "ffmpeg-compat-preview")
            .Where(x => x.RequestedAction == recommendation.RecommendationKey)
            .Where(x => x.Status == "Queued" || x.Status == "Running")
            .ToListAsync();

        var activeJob = existing
            .OrderByDescending(x => x.RequestedAtUtc)
            .FirstOrDefault();

        if (activeJob is not null)
        {
            return new MediaCompatibilityQueueResult(activeJob.Id, false, true, "A matching FFmpeg compatibility remediation is already queued or running for this item.");
        }

        var job = MediaCompatibilityRecommendationEngine.BuildPreviewJob(recommendation, actor, now);
        db.LibraryRemediationJobs.Add(job);
        await db.SaveChangesAsync();
        return new MediaCompatibilityQueueResult(job.Id, true, false, "Queued safe FFmpeg compatibility remediation job. MediaCloud will execute it in the background and keep the original file untouched.");
    }

    public static async Task<MediaCompatibilityQueueRemovalResult> RemoveQueuedPreviewJobAsync(MediaCloudDbContext db, long jobId)
    {
        var job = await db.LibraryRemediationJobs.FirstOrDefaultAsync(x => x.Id == jobId);
        if (job is null)
        {
            return new MediaCompatibilityQueueRemovalResult(jobId, false, "FFmpeg compatibility job not found.");
        }

        var isQueuedPreviewJob = string.Equals(job.ServiceKey, "ffmpeg", StringComparison.OrdinalIgnoreCase)
            && string.Equals(job.CommandName, "ffmpeg-compat-preview", StringComparison.OrdinalIgnoreCase)
            && string.Equals(job.Status, "Queued", StringComparison.OrdinalIgnoreCase);

        if (!isQueuedPreviewJob)
        {
            return new MediaCompatibilityQueueRemovalResult(jobId, false, "Only queued FFmpeg compatibility jobs can be removed from the queue.");
        }

        db.LibraryRemediationJobs.Remove(job);
        await db.SaveChangesAsync();
        return new MediaCompatibilityQueueRemovalResult(jobId, true, "Removed queued FFmpeg compatibility job.");
    }
}
