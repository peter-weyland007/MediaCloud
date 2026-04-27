using api.Models;
using System;
using System.IO;
using System.Threading;
using Xunit;

public sealed class MediaPlaybackSmokeTestEngineTests
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
    public void BuildPlan_returns_direct_stream_likely_for_safe_sidecar_candidate_and_generates_sample_command()
    {
        var item = new LibraryItem
        {
            Id = 1497,
            MediaType = "Movie",
            Title = "Dunkirk",
            PrimaryFilePath = "/mnt/media/Dunkirk.mkv",
            ActualRuntimeMinutes = 106,
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
        var plan = MediaPlaybackSmokeTestEngine.BuildPlan(item, recommendation, sampleSeconds: 45, seekPercent: 35);

        Assert.Equal("Direct Stream likely", plan.LikelyDecision);
        Assert.Equal("warning", plan.VerdictSeverity);
        Assert.True(plan.SamplePlanned);
        Assert.Contains("short ffmpeg compatibility sample", plan.OperatorSummary, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-ss 2226", plan.SampleCommandPreview);
        Assert.Contains("-t 45", plan.SampleCommandPreview);
        Assert.Contains(".playback-smoke-sample.mp4", plan.SampleOutputPath);
        Assert.Contains(plan.Reasons, reason => reason.Contains("transcode", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildPlan_returns_transcode_required_for_manual_video_review_and_uses_manual_command_preview()
    {
        var item = new LibraryItem
        {
            Id = 33,
            MediaType = "Movie",
            Title = "Experimental Movie",
            PrimaryFilePath = "/mnt/media/Experimental.mkv",
            ActualRuntimeMinutes = 120,
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
        var plan = MediaPlaybackSmokeTestEngine.BuildPlan(item, recommendation, sampleSeconds: 30, seekPercent: 25);

        Assert.Equal("Transcode required", plan.LikelyDecision);
        Assert.Equal("danger", plan.VerdictSeverity);
        Assert.True(plan.SamplePlanned);
        Assert.Contains("manual review", plan.OperatorSummary, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-ss 1800", plan.SampleCommandPreview);
        Assert.Contains("-t 30", plan.SampleCommandPreview);
        Assert.Contains("manual-review.playback-smoke-sample.mp4", plan.SampleOutputPath);
    }

    [Fact]
    public async Task RunAsync_handles_quoted_paths_without_shell_execution()
    {
        var sampleDirectory = Path.Combine(Path.GetTempPath(), $"mediacloud smoke \"quoted\" {Guid.NewGuid():N}");
        Directory.CreateDirectory(sampleDirectory);
        var sampleOutputPath = Path.Combine(sampleDirectory, "sample output.mp4");
        var escapedOutputPath = sampleOutputPath.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        var command = $"ffmpeg -y -f lavfi -i testsrc=size=32x32:rate=1 -t 1 -c:v libx264 -pix_fmt yuv420p \"{escapedOutputPath}\"";
        var plan = new MediaPlaybackSmokeTestPlanResponse(
            2,
            "Stable / Broad Compatibility",
            "manual_video_review",
            "Manual review: would require video transcode",
            "Transcode required",
            "danger",
            "Operator summary",
            "Why summary",
            ["Quoted path regression coverage."],
            true,
            command,
            sampleOutputPath,
            1,
            35,
            0);

        var response = await MediaPlaybackSmokeTestEngine.RunAsync(plan, CancellationToken.None);

        Assert.True(response.SampleAttempted);
        Assert.True(response.SampleSucceeded);
        Assert.Equal(string.Empty, response.ErrorMessage);
        Assert.False(File.Exists(sampleOutputPath));

        try
        {
            Directory.Delete(sampleDirectory, recursive: true);
        }
        catch
        {
        }
    }
}
