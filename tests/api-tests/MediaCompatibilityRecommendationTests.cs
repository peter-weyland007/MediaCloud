using api.Models;
using Xunit;

public sealed class MediaCompatibilityRecommendationTests
{
    private static readonly MediaProfileSettingsResponse DefaultProfile = new(
        PreferredContainer: "mp4",
        PreferredVideoCodec: "h264",
        MaxPreferredResolution: "1080p",
        AllowHevc: true,
        Allow10BitVideo: false,
        PreferredAudioCodec: "aac",
        AllowImageBasedSubtitles: false,
        PreferTextSubtitlesOnly: true,
        MaxPreferredBitrateMbps: 20,
        ActivePresetKey: "broad-plex-compatibility",
        ActivePresetName: "Stable / Broad Compatibility");

    [Fact]
    public void Build_returns_safe_sidecar_preview_for_mkv_with_dts_audio_when_video_is_already_compatible()
    {
        var item = new LibraryItem
        {
            Id = 1497,
            MediaType = "Movie",
            Title = "Dunkirk",
            PrimaryFilePath = "/mnt/media/Dunkirk.mkv",
            PlayabilityScore = "Transcode likely"
        };
        var details = new MediaPlayabilityStoredDetails(
            ContainerNames: ["matroska"],
            VideoCodec: "h264",
            VideoProfile: "High",
            PixelFormat: "yuv420p",
            Width: 1920,
            Height: 1080,
            BitrateBitsPerSecond: 12_000_000,
            AudioCodecs: ["dts"],
            SubtitleCodecs: [],
            Reasons: ["DTS audio is a common Plex transcode trigger."]);
        var diagnostic = new PlaybackDiagnosticEntry
        {
            Decision = "transcode",
            Summary = "Plex had to transcode this playback session (Original)."
        };

        var recommendation = MediaCompatibilityRecommendationEngine.Build(item, details, diagnostic, DefaultProfile);

        Assert.True(recommendation.HasRecommendation);
        Assert.True(recommendation.SafeToQueue);
        Assert.Equal("container_audio_sidecar", recommendation.RecommendationKey);
        Assert.Equal("Worth converting", recommendation.UserDecisionGuidance);
        Assert.Equal("Convert this file", recommendation.BestActionLabel);
        Assert.Equal("Low", recommendation.ConversionRiskLabel);
        Assert.Equal("Stable / Broad Compatibility", recommendation.ActivePresetName);
        Assert.Contains("ffmpeg -y -i", recommendation.CommandPreview);
        Assert.Contains("-c:v copy", recommendation.CommandPreview);
        Assert.Contains("-c:a aac", recommendation.CommandPreview);
        Assert.Contains(recommendation.ComparisonRows, row => row.Label == "Container" && row.InspectedValue == "MATROSKA" && row.SelectedProfileValue == "MP4" && row.Status == "Outside profile");
        Assert.Contains(recommendation.ComparisonRows, row => row.Label == "Audio" && row.InspectedValue == "DTS" && row.SelectedProfileValue == "AAC" && row.Status == "Outside profile");
    }


    [Fact]
    public void Build_flags_iso_source_as_disc_image_without_safe_queue_recommendation()
    {
        var item = new LibraryItem
        {
            Id = 77,
            MediaType = "Movie",
            Title = "Disc Dump",
            PrimaryFilePath = "/mnt/media/Disc Dump.iso",
            PlayabilityScore = "Problematic"
        };
        var details = new MediaPlayabilityStoredDetails(
            ContainerNames: ["iso"],
            VideoCodec: "mpeg2video",
            VideoProfile: "Main",
            PixelFormat: "yuv420p",
            Width: 720,
            Height: 480,
            BitrateBitsPerSecond: 7_000_000,
            AudioCodecs: ["ac3"],
            SubtitleCodecs: ["dvd_subtitle"],
            Reasons: ["ISO packaging should be identified distinctly."]);

        var recommendation = MediaCompatibilityRecommendationEngine.Build(item, details, latestDiagnostic: null, DefaultProfile);

        Assert.True(recommendation.HasRecommendation);
        Assert.False(recommendation.SafeToQueue);
        Assert.Equal("manual_disc_image_review", recommendation.RecommendationKey);
        Assert.Equal("Disc Image Review", recommendation.ReviewDialogTitle);
        Assert.Contains(recommendation.BlockedReasons, reason => reason.Contains("Container", System.StringComparison.OrdinalIgnoreCase) && reason.Contains("ISO", System.StringComparison.OrdinalIgnoreCase));
        Assert.Contains(recommendation.ComparisonRows, row => row.Label == "Container" && row.InspectedValue == "ISO" && row.SelectedProfileValue == "MP4" && row.Status == "Disc image");
        Assert.Empty(recommendation.CommandPreview);
    }

    [Fact]
    public void Build_returns_manual_review_when_video_target_would_require_full_transcode()
    {
        var item = new LibraryItem
        {
            Id = 33,
            MediaType = "Movie",
            Title = "Experimental Movie",
            PrimaryFilePath = "/mnt/media/Experimental.mkv",
            PlayabilityScore = "Problematic"
        };
        var details = new MediaPlayabilityStoredDetails(
            ContainerNames: ["matroska"],
            VideoCodec: "av1",
            VideoProfile: "Main",
            PixelFormat: "yuv420p10le",
            Width: 3840,
            Height: 2160,
            BitrateBitsPerSecond: 28_000_000,
            AudioCodecs: ["aac"],
            SubtitleCodecs: [],
            Reasons: ["AV1 is still inconsistent across mixed Plex clients."]);
        var diagnostic = new PlaybackDiagnosticEntry
        {
            Decision = "transcode",
            Summary = "Playback required transcode."
        };

        var recommendation = MediaCompatibilityRecommendationEngine.Build(item, details, diagnostic, DefaultProfile);

        Assert.True(recommendation.HasRecommendation);
        Assert.False(recommendation.SafeToQueue);
        Assert.Equal("manual_video_review", recommendation.RecommendationKey);
        Assert.Equal(string.Empty, recommendation.CommandPreview);
        Assert.Equal("Manual Conversion Review", recommendation.ReviewDialogTitle);
        Assert.Equal("Better to request a new file", recommendation.UserDecisionGuidance);
        Assert.Equal("Request better file", recommendation.BestActionLabel);
        Assert.Equal("High", recommendation.ConversionRiskLabel);
        Assert.Contains("video codec is AV1", recommendation.UserDecisionSummary, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("video transcode", recommendation.ReviewOperatorWarning, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains(recommendation.BlockedReasons, reason => reason.Contains("Video codec", System.StringComparison.OrdinalIgnoreCase));
        Assert.Contains(recommendation.BlockedReasons, reason => reason.Contains("Resolution", System.StringComparison.OrdinalIgnoreCase));
        Assert.Contains(recommendation.ManualPlanSteps, step => step.Contains("ffmpeg", System.StringComparison.OrdinalIgnoreCase));
        Assert.Contains(".manual-review.mp4", recommendation.ManualCommandPreview);
        Assert.Contains(recommendation.ComparisonRows, row => row.Label == "Video codec" && row.InspectedValue == "AV1" && row.Status == "Outside profile");
        Assert.Contains(recommendation.ComparisonRows, row => row.Label == "Resolution" && row.InspectedValue == "3840×2160" && row.SelectedProfileValue == "1080P max" && row.Status == "Outside profile");
    }

    [Fact]
    public void Build_manual_video_review_prioritizes_subtitle_mismatch_in_user_summary_and_normalizes_pgs_display()
    {
        var item = new LibraryItem
        {
            Id = 1298,
            MediaType = "Movie",
            Title = "28 Years Later",
            PrimaryFilePath = "/mnt/media/28 Years Later.m2ts",
            PlayabilityScore = "Problematic"
        };
        var details = new MediaPlayabilityStoredDetails(
            ContainerNames: ["mpegts"],
            VideoCodec: "hevc",
            VideoProfile: "Main 10",
            PixelFormat: "yuv420p10le",
            Width: 3840,
            Height: 2160,
            BitrateBitsPerSecond: 89_500_000,
            AudioCodecs: ["truehd", "ac3", "dts"],
            SubtitleCodecs: ["hdmv_pgs_subtitle"],
            Reasons: ["10-bit HEVC raises compatibility risk.", "PGS subtitles often force burn-in."]);

        var recommendation = MediaCompatibilityRecommendationEngine.Build(item, details, latestDiagnostic: null, DefaultProfile);

        Assert.Equal("manual_video_review", recommendation.RecommendationKey);
        Assert.Contains("subtitles are PGS instead of Text subtitles only", recommendation.UserDecisionSummary, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains(recommendation.ComparisonRows, row => row.Label == "Subtitles" && row.InspectedValue == "PGS" && row.SelectedProfileValue == "Text subtitles only" && row.Status == "Outside profile");
    }

    [Fact]
    public void Build_manual_review_job_records_operator_follow_up_without_queueing_worker_execution()
    {
        var item = new LibraryItem
        {
            Id = 44,
            MediaType = "Movie",
            Title = "The Abyss",
            PrimaryFilePath = "/mnt/media/The Abyss.mkv",
            PlayabilityScore = "Problematic"
        };
        var details = new MediaPlayabilityStoredDetails(
            ContainerNames: ["matroska"],
            VideoCodec: "hevc",
            VideoProfile: "Main 10",
            PixelFormat: "yuv420p10le",
            Width: 1920,
            Height: 802,
            BitrateBitsPerSecond: 5_470_000,
            AudioCodecs: ["aac"],
            SubtitleCodecs: ["hdmv_pgs_subtitle"],
            Reasons: ["10-bit HEVC raises compatibility risk.", "PGS subtitles often force burn-in."]);

        var recommendation = MediaCompatibilityRecommendationEngine.Build(item, details, latestDiagnostic: null, DefaultProfile);
        var now = new DateTimeOffset(2026, 4, 13, 15, 0, 0, System.TimeSpan.Zero);

        var job = MediaCompatibilityRecommendationEngine.BuildManualReviewJob(recommendation, "mark", now);

        Assert.Equal("ffmpeg", job.ServiceKey);
        Assert.Equal("ffmpeg-manual-review", job.CommandName);
        Assert.Equal("BlockedManualReview", job.Status);
        Assert.True(job.NeedsManualReview);
        Assert.Contains("Manual Conversion Review", job.ReleaseSummary);
        Assert.Contains("manual-review", job.ResultMessage, System.StringComparison.OrdinalIgnoreCase);
    }
}
