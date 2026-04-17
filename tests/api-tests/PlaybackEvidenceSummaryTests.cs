using System;
using api;
using api.Models;
using Xunit;

public sealed class PlaybackEvidenceSummaryTests
{
    [Fact]
    public void Build_returns_empty_summary_when_no_playback_evidence_exists()
    {
        var summary = PlaybackEvidenceSummaryBuilder.Build(42, Array.Empty<PlaybackDiagnosticEntry>());

        Assert.Equal(42, summary.LibraryItemId);
        Assert.Equal(0, summary.TotalCount);
        Assert.Equal("NoEvidence", summary.EvidenceState);
        Assert.Equal(0, summary.DirectPlayCount);
        Assert.Equal(0, summary.FullTranscodeCount);
        Assert.Equal(0, summary.AudioTranscodeCount);
        Assert.Equal(0, summary.SubtitleIssueCount);
        Assert.Equal(0, summary.FailureCount);
        Assert.Equal(0, summary.RepeatedIssueKindCount);
        Assert.False(summary.HasRepeatedIssues);
        Assert.Null(summary.LastImportedAtUtc);
        Assert.Null(summary.LastOccurredAtUtc);
    }

    [Fact]
    public void Build_counts_direct_play_and_single_playback_issue_without_marking_it_repeated()
    {
        var now = new DateTimeOffset(2026, 4, 13, 14, 30, 0, TimeSpan.Zero);
        var rows = new[]
        {
            new PlaybackDiagnosticEntry
            {
                Id = 1,
                LibraryItemId = 42,
                ImportedAtUtc = now.AddMinutes(-30),
                OccurredAtUtc = now.AddHours(-2),
                HealthLabel = "Healthy",
                Decision = "direct play"
            },
            new PlaybackDiagnosticEntry
            {
                Id = 2,
                LibraryItemId = 42,
                ImportedAtUtc = now,
                OccurredAtUtc = now.AddHours(-1),
                HealthLabel = "Investigate",
                Decision = "transcode",
                TranscodeDecision = "transcode",
                AudioDecision = "transcode",
                AudioCodec = "dts"
            }
        };

        var summary = PlaybackEvidenceSummaryBuilder.Build(42, rows);

        Assert.Equal(2, summary.TotalCount);
        Assert.Equal(1, summary.DirectPlayCount);
        Assert.Equal(1, summary.FullTranscodeCount);
        Assert.Equal(1, summary.AudioTranscodeCount);
        Assert.Equal(0, summary.SubtitleIssueCount);
        Assert.Equal(0, summary.FailureCount);
        Assert.Equal(0, summary.RepeatedIssueKindCount);
        Assert.False(summary.HasRepeatedIssues);
        Assert.Equal("IssuesObserved", summary.EvidenceState);
        Assert.Equal(now, summary.LastImportedAtUtc);
        Assert.Equal(now.AddHours(-1), summary.LastOccurredAtUtc);
        Assert.True(summary.Summary.Contains("audio transcode", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_marks_repeated_issue_patterns_when_failures_and_subtitle_transcodes_repeat()
    {
        var now = new DateTimeOffset(2026, 4, 13, 18, 0, 0, TimeSpan.Zero);
        var rows = new[]
        {
            new PlaybackDiagnosticEntry
            {
                Id = 1,
                LibraryItemId = 55,
                ImportedAtUtc = now.AddMinutes(-20),
                OccurredAtUtc = now.AddHours(-4),
                HealthLabel = "Error",
                Decision = "transcode",
                TranscodeDecision = "transcode",
                SubtitleDecision = "transcode",
                SubtitleCodec = "hdmv_pgs_subtitle",
                ErrorMessage = "Conversion failed"
            },
            new PlaybackDiagnosticEntry
            {
                Id = 2,
                LibraryItemId = 55,
                ImportedAtUtc = now.AddMinutes(-10),
                OccurredAtUtc = now.AddHours(-2),
                HealthLabel = "Investigate",
                Decision = "transcode",
                TranscodeDecision = "transcode",
                SubtitleDecision = "transcode",
                SubtitleCodec = "hdmv_pgs_subtitle"
            },
            new PlaybackDiagnosticEntry
            {
                Id = 3,
                LibraryItemId = 55,
                ImportedAtUtc = now,
                OccurredAtUtc = now.AddHours(-1),
                HealthLabel = "Error",
                Decision = "transcode",
                TranscodeDecision = "transcode",
                ErrorMessage = "Playback stopped unexpectedly"
            }
        };

        var summary = PlaybackEvidenceSummaryBuilder.Build(55, rows);

        Assert.Equal(3, summary.TotalCount);
        Assert.Equal(3, summary.FullTranscodeCount);
        Assert.Equal(2, summary.SubtitleIssueCount);
        Assert.Equal(2, summary.FailureCount);
        Assert.Equal(3, summary.RepeatedIssueKindCount);
        Assert.True(summary.HasRepeatedIssues);
        Assert.Equal("RepeatedIssues", summary.EvidenceState);
        Assert.True(summary.Summary.Contains("repeated", StringComparison.OrdinalIgnoreCase));
    }
}
