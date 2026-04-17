using Xunit;

public sealed class MediaPlayabilityScoringTests
{
    [Fact]
    public void Evaluate_returns_excellent_with_score_100_for_h264_mp4_with_safe_audio_and_text_subtitles()
    {
        var assessment = MediaPlayabilityScoring.Evaluate(new MediaPlayabilityProbeInfo(
            ContainerNames: new[] { "mov,mp4,m4a,3gp,3g2,mj2" },
            VideoCodec: "h264",
            VideoProfile: "High",
            PixelFormat: "yuv420p",
            Width: 1920,
            Height: 1080,
            BitrateBitsPerSecond: 8_000_000,
            AudioCodecs: new[] { "aac" },
            SubtitleCodecs: new[] { "subrip" }));

        Assert.Equal(MediaPlayabilityScore.Excellent, assessment.Score);
        Assert.Equal(100, assessment.CompatibilityScore);
        Assert.Contains("Broad Plex direct-play compatibility", assessment.Summary);
        Assert.Empty(assessment.Reasons);
    }

    [Fact]
    public void Evaluate_returns_good_with_expected_score_for_hevc_mkv_aac()
    {
        var assessment = MediaPlayabilityScoring.Evaluate(new MediaPlayabilityProbeInfo(
            ContainerNames: new[] { "matroska,webm" },
            VideoCodec: "hevc",
            VideoProfile: "Main",
            PixelFormat: "yuv420p",
            Width: 1920,
            Height: 1040,
            BitrateBitsPerSecond: 1_222_338,
            AudioCodecs: new[] { "aac" },
            SubtitleCodecs: []));

        Assert.Equal(MediaPlayabilityScore.Good, assessment.Score);
        Assert.Equal(87, assessment.CompatibilityScore);
        Assert.Contains(assessment.Reasons, reason => reason.Contains("HEVC", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_returns_caution_with_expected_score_for_hevc_10bit_text_subtitles()
    {
        var assessment = MediaPlayabilityScoring.Evaluate(new MediaPlayabilityProbeInfo(
            ContainerNames: new[] { "matroska,webm" },
            VideoCodec: "hevc",
            VideoProfile: "Main 10",
            PixelFormat: "yuv420p10le",
            Width: 1920,
            Height: 804,
            BitrateBitsPerSecond: 4_008_838,
            AudioCodecs: new[] { "aac" },
            SubtitleCodecs: new[] { "subrip" }));

        Assert.Equal(MediaPlayabilityScore.Caution, assessment.Score);
        Assert.Equal(72, assessment.CompatibilityScore);
        Assert.Contains(assessment.Reasons, reason => reason.Contains("10-bit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_returns_caution_when_audio_or_subtitle_formats_are_client_unfriendly_but_not_catastrophic()
    {
        var assessment = MediaPlayabilityScoring.Evaluate(new MediaPlayabilityProbeInfo(
            ContainerNames: new[] { "matroska,webm" },
            VideoCodec: "h264",
            VideoProfile: "High",
            PixelFormat: "yuv420p",
            Width: 1920,
            Height: 1080,
            BitrateBitsPerSecond: 10_000_000,
            AudioCodecs: new[] { "dts" },
            SubtitleCodecs: new[] { "hdmv_pgs_subtitle" }));

        Assert.Equal(MediaPlayabilityScore.Caution, assessment.Score);
        Assert.Equal(60, assessment.CompatibilityScore);
        Assert.Contains(assessment.Reasons, reason => reason.Contains("DTS", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(assessment.Reasons, reason => reason.Contains("PGS", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_returns_problematic_for_multiple_major_compatibility_red_flags()
    {
        var assessment = MediaPlayabilityScoring.Evaluate(new MediaPlayabilityProbeInfo(
            ContainerNames: new[] { "avi" },
            VideoCodec: "av1",
            VideoProfile: "Main 10",
            PixelFormat: "yuv420p10le",
            Width: 3840,
            Height: 2160,
            BitrateBitsPerSecond: 42_000_000,
            AudioCodecs: new[] { "truehd" },
            SubtitleCodecs: new[] { "dvd_subtitle" }));

        Assert.Equal(MediaPlayabilityScore.Problematic, assessment.Score);
        Assert.InRange(assessment.CompatibilityScore, 0, 20);
        Assert.True(assessment.Reasons.Count >= 3);
    }
}
