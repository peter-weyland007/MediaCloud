using api;
using Xunit;

public sealed class MediaCompatibilityCompletionMessageTests
{
    [Fact]
    public void BuildCompletionSummary_includes_plex_refresh_success_message()
    {
        var summary = MediaCompatibilityExecution.BuildCompletionSummary(
            success: true,
            outputPath: "/Volumes/Media/Movies/28 Days Later/28 Days Later.compat.mp4",
            exitCode: 0,
            plexRefresh: new PlexMetadataRefreshResult(true, true, "Requested Plex metadata refresh for this movie."));

        Assert.Equal("FFmpeg compatibility remediation completed. Output: /Volumes/Media/Movies/28 Days Later/28 Days Later.compat.mp4 Requested Plex metadata refresh for this movie.", summary);
    }

    [Fact]
    public void BuildCompletionSummary_includes_plex_refresh_failure_without_marking_job_failed()
    {
        var summary = MediaCompatibilityExecution.BuildCompletionSummary(
            success: true,
            outputPath: "/Volumes/Media/Movies/28 Days Later/28 Days Later.compat.mp4",
            exitCode: 0,
            plexRefresh: new PlexMetadataRefreshResult(true, false, "Plex refresh failed: gateway down"));

        Assert.Equal("FFmpeg compatibility remediation completed. Output: /Volumes/Media/Movies/28 Days Later/28 Days Later.compat.mp4 Plex refresh failed: gateway down", summary);
    }
}
