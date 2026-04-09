using web.Components.Shared;
using Xunit;

public sealed class PlaybackDiagnosticsPresentationTests
{
    [Fact]
    public void BuildClientLabel_returns_distinct_non_empty_parts_in_display_order()
    {
        var entry = new web.Components.Shared.PlaybackDiagnosticDto(
            Id: 1,
            LibraryItemId: 42,
            SourceService: "tautulli",
            SourceDisplayName: "Tautulli",
            ExternalId: "abc",
            OccurredAtUtc: DateTimeOffset.UtcNow,
            ImportedAtUtc: DateTimeOffset.UtcNow,
            StartedAtUtc: null,
            StoppedAtUtc: null,
            UserName: "mark",
            ClientName: "Living Room TV",
            Player: "Living Room TV",
            Product: "Plex for Smart TV",
            Platform: "LG",
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
            HealthLabel: "Healthy",
            Summary: "Direct play succeeded.",
            SuspectedCause: string.Empty,
            ErrorMessage: string.Empty,
            LogSnippet: string.Empty);

        var label = PlaybackDiagnosticsPresentation.BuildClientLabel(entry);

        Assert.Equal("Living Room TV · Plex for Smart TV · LG", label);
    }

    [Fact]
    public void BuildDecisionLabel_falls_back_to_primary_decision_when_stream_decisions_missing()
    {
        var entry = new web.Components.Shared.PlaybackDiagnosticDto(
            Id: 1,
            LibraryItemId: 42,
            SourceService: "plex",
            SourceDisplayName: "Plex",
            ExternalId: "xyz",
            OccurredAtUtc: DateTimeOffset.UtcNow,
            ImportedAtUtc: DateTimeOffset.UtcNow,
            StartedAtUtc: null,
            StoppedAtUtc: null,
            UserName: "mark",
            ClientName: string.Empty,
            Player: string.Empty,
            Product: string.Empty,
            Platform: string.Empty,
            Decision: "transcode",
            TranscodeDecision: string.Empty,
            VideoDecision: string.Empty,
            AudioDecision: string.Empty,
            SubtitleDecision: string.Empty,
            Container: "mkv",
            VideoCodec: "hevc",
            AudioCodec: "truehd",
            SubtitleCodec: string.Empty,
            QualityProfile: "4 Mbps 720p",
            HealthLabel: "Investigate",
            Summary: "Playback required transcode.",
            SuspectedCause: "Client codec mismatch",
            ErrorMessage: string.Empty,
            LogSnippet: string.Empty);

        var label = PlaybackDiagnosticsPresentation.BuildDecisionLabel(entry);

        Assert.Equal("transcode", label);
    }

    [Theory]
    [InlineData("Healthy", "16,185,129")]
    [InlineData("Investigate", "234,179,8")]
    [InlineData("Error", "239,68,68")]
    [InlineData("Unknown", "59,130,246")]
    public void GetHealthBadgeStyle_returns_expected_palette_for_health_label(string healthLabel, string expectedColorFragment)
    {
        var style = PlaybackDiagnosticsPresentation.GetHealthBadgeStyle(healthLabel);

        Assert.Contains(expectedColorFragment, style);
    }
}
