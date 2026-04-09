using Xunit;

public sealed class RuntimeProbeFailurePolicyTests
{
    [Fact]
    public void ShouldSkipAutomaticReprobe_returns_true_for_same_file_path_with_open_failure_issue()
    {
        var detailsJson = RuntimeProbeFailurePolicy.BuildIssueDetailsJson(
            "/media/tv/show/episode.mkv",
            "ffprobe failed",
            1);

        var shouldSkip = RuntimeProbeFailurePolicy.ShouldSkipAutomaticReprobe(
            issueStatus: "Open",
            issueDetailsJson: detailsJson,
            currentFilePath: "/media/tv/show/episode.mkv");

        Assert.True(shouldSkip);
    }

    [Fact]
    public void ShouldSkipAutomaticReprobe_returns_false_when_file_path_changed()
    {
        var detailsJson = RuntimeProbeFailurePolicy.BuildIssueDetailsJson(
            "/media/tv/show/old-episode.mkv",
            "ffprobe failed",
            1);

        var shouldSkip = RuntimeProbeFailurePolicy.ShouldSkipAutomaticReprobe(
            issueStatus: "Open",
            issueDetailsJson: detailsJson,
            currentFilePath: "/media/tv/show/new-episode.mkv");

        Assert.False(shouldSkip);
    }

    [Fact]
    public void ShouldSkipAutomaticReprobe_returns_false_for_resolved_issue()
    {
        var detailsJson = RuntimeProbeFailurePolicy.BuildIssueDetailsJson(
            "/media/tv/show/episode.mkv",
            "ffprobe failed",
            1);

        var shouldSkip = RuntimeProbeFailurePolicy.ShouldSkipAutomaticReprobe(
            issueStatus: "Resolved",
            issueDetailsJson: detailsJson,
            currentFilePath: "/media/tv/show/episode.mkv");

        Assert.False(shouldSkip);
    }
}
