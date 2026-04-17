using api;
using Xunit;

public sealed class ManualLibraryIssueSeverityTests
{
    [Theory]
    [InlineData("device_specific_issue", "Info")]
    [InlineData("subtitle_unusable", "Warning")]
    [InlineData("manual_issue", "Warning")]
    [InlineData("playback_stall", "High")]
    [InlineData("resume_starts_over", "High")]
    [InlineData("audio_wrong_during_playback", "High")]
    [InlineData("unknown_issue", "Warning")]
    public void Resolve_returns_expected_default_severity_for_manual_issue_types(string issueType, string expected)
    {
        Assert.Equal(expected, ManualLibraryIssueSeverity.Resolve(issueType));
    }
}
