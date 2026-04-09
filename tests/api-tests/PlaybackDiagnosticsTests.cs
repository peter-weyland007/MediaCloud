using Xunit;

public sealed class PlaybackDiagnosticsTests
{
    [Fact]
    public void Analyze_returns_healthy_direct_play_summary_when_session_direct_plays_cleanly()
    {
        var assessment = PlaybackDiagnosticsAnalyzer.Analyze(new PlaybackDiagnosticProbe(
            Decision: "direct play",
            TranscodeDecision: "direct play",
            VideoDecision: "direct play",
            AudioDecision: "direct play",
            SubtitleDecision: string.Empty,
            Container: "mkv",
            VideoCodec: "h264",
            AudioCodec: "ac3",
            SubtitleCodec: string.Empty,
            QualityProfile: "Original",
            ErrorMessage: string.Empty,
            LogSnippet: string.Empty,
            Player: "Living Room TV",
            Product: "Plex for Smart TV",
            Platform: "LG"));

        Assert.Equal("Healthy", assessment.HealthLabel);
        Assert.False(assessment.HasError);
        Assert.False(assessment.IsTranscode);
        Assert.Contains("direct play", assessment.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(assessment.Reasons);
    }

    [Fact]
    public void Analyze_flags_subtitle_burn_in_as_likely_cause_when_subtitles_force_transcode()
    {
        var assessment = PlaybackDiagnosticsAnalyzer.Analyze(new PlaybackDiagnosticProbe(
            Decision: "transcode",
            TranscodeDecision: "transcode",
            VideoDecision: "transcode",
            AudioDecision: "direct play",
            SubtitleDecision: "transcode",
            Container: "mkv",
            VideoCodec: "h264",
            AudioCodec: "ac3",
            SubtitleCodec: "hdmv_pgs_subtitle",
            QualityProfile: "4 Mbps 720p",
            ErrorMessage: string.Empty,
            LogSnippet: string.Empty,
            Player: "Living Room TV",
            Product: "Plex for Smart TV",
            Platform: "LG"));

        Assert.Equal("Investigate", assessment.HealthLabel);
        Assert.True(assessment.IsTranscode);
        Assert.Contains("subtitle", assessment.SuspectedCause, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(assessment.Reasons, reason => reason.Contains("PGS", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_promotes_explicit_playback_error_message_above_generic_transcode_hints()
    {
        var assessment = PlaybackDiagnosticsAnalyzer.Analyze(new PlaybackDiagnosticProbe(
            Decision: "transcode",
            TranscodeDecision: "transcode",
            VideoDecision: "transcode",
            AudioDecision: "transcode",
            SubtitleDecision: string.Empty,
            Container: "mkv",
            VideoCodec: "hevc",
            AudioCodec: "truehd",
            SubtitleCodec: string.Empty,
            QualityProfile: "2 Mbps 720p",
            ErrorMessage: "Conversion failed. The transcoder exited due to an error.",
            LogSnippet: "ERROR - [Transcode] Conversion failed while opening output stream.",
            Player: "Bedroom TV",
            Product: "Plex for Android (TV)",
            Platform: "Android"));

        Assert.Equal("Error", assessment.HealthLabel);
        Assert.True(assessment.HasError);
        Assert.Contains("transcoder exited", assessment.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Conversion failed", assessment.SuspectedCause, StringComparison.OrdinalIgnoreCase);
    }
}
