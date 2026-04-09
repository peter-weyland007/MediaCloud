public sealed record PlaybackDiagnosticProbe(
    string Decision,
    string TranscodeDecision,
    string VideoDecision,
    string AudioDecision,
    string SubtitleDecision,
    string Container,
    string VideoCodec,
    string AudioCodec,
    string SubtitleCodec,
    string QualityProfile,
    string ErrorMessage,
    string LogSnippet,
    string Player,
    string Product,
    string Platform);

public sealed record PlaybackDiagnosticAssessment(
    string HealthLabel,
    string Summary,
    string SuspectedCause,
    bool IsTranscode,
    bool HasError,
    IReadOnlyList<string> Reasons);

public static class PlaybackDiagnosticsAnalyzer
{
    public static PlaybackDiagnosticAssessment Analyze(PlaybackDiagnosticProbe probe)
    {
        var reasons = new List<string>();
        var suspectedCause = string.Empty;

        var transcodeDecision = Normalize(probe.TranscodeDecision);
        var decision = Normalize(probe.Decision);
        var videoDecision = Normalize(probe.VideoDecision);
        var audioDecision = Normalize(probe.AudioDecision);
        var subtitleDecision = Normalize(probe.SubtitleDecision);
        var videoCodec = Normalize(probe.VideoCodec);
        var audioCodec = Normalize(probe.AudioCodec);
        var subtitleCodec = Normalize(probe.SubtitleCodec);
        var errorMessage = (probe.ErrorMessage ?? string.Empty).Trim();
        var logSnippet = (probe.LogSnippet ?? string.Empty).Trim();

        var isTranscode = transcodeDecision.Contains("transcode", StringComparison.Ordinal)
            || decision.Contains("transcode", StringComparison.Ordinal)
            || videoDecision.Contains("transcode", StringComparison.Ordinal)
            || audioDecision.Contains("transcode", StringComparison.Ordinal)
            || subtitleDecision.Contains("transcode", StringComparison.Ordinal);

        var hasError = !string.IsNullOrWhiteSpace(errorMessage)
            || logSnippet.Contains("error", StringComparison.OrdinalIgnoreCase)
            || logSnippet.Contains("failed", StringComparison.OrdinalIgnoreCase);

        if (subtitleDecision.Contains("transcode", StringComparison.Ordinal)
            || subtitleCodec is "hdmv_pgs_subtitle" or "dvd_subtitle" or "pgs")
        {
            reasons.Add(subtitleCodec switch
            {
                "hdmv_pgs_subtitle" or "pgs" => "PGS/image subtitles likely forced subtitle burn-in or full transcode.",
                "dvd_subtitle" => "VobSub/image subtitles likely forced subtitle burn-in or full transcode.",
                _ => "Subtitle handling likely forced burn-in or transcode."
            });
            suspectedCause = reasons[^1];
        }

        if (audioDecision.Contains("transcode", StringComparison.Ordinal)
            || audioCodec is "dts" or "dca" or "truehd" or "flac")
        {
            reasons.Add(audioCodec switch
            {
                "dts" or "dca" => "DTS audio is a common TV-client transcode trigger.",
                "truehd" => "TrueHD audio frequently breaks direct play on TV Plex apps.",
                "flac" => "FLAC audio can require audio transcode on TV Plex clients.",
                _ => "Audio compatibility likely forced transcoding."
            });
            suspectedCause = string.IsNullOrWhiteSpace(suspectedCause) ? reasons[^1] : suspectedCause;
        }

        if (videoDecision.Contains("transcode", StringComparison.Ordinal)
            || videoCodec is "hevc" or "h265" or "av1")
        {
            reasons.Add(videoCodec switch
            {
                "hevc" or "h265" => "HEVC video can fail or transcode on some built-in TV Plex clients.",
                "av1" => "AV1 video support is still inconsistent across TV Plex clients.",
                _ => "Video compatibility likely forced transcoding."
            });
            suspectedCause = string.IsNullOrWhiteSpace(suspectedCause) ? reasons[^1] : suspectedCause;
        }

        if (hasError)
        {
            var explicitMessage = FirstNonEmpty(errorMessage, logSnippet, "Playback failed with a server-side error.");
            return new PlaybackDiagnosticAssessment(
                HealthLabel: "Error",
                Summary: explicitMessage,
                SuspectedCause: explicitMessage,
                IsTranscode: isTranscode,
                HasError: true,
                Reasons: reasons);
        }

        if (!isTranscode)
        {
            return new PlaybackDiagnosticAssessment(
                HealthLabel: "Healthy",
                Summary: "Plex reported direct play for this session on the client.",
                SuspectedCause: string.Empty,
                IsTranscode: false,
                HasError: false,
                Reasons: []);
        }

        var summary = string.IsNullOrWhiteSpace(probe.QualityProfile)
            ? "Plex had to transcode this playback session."
            : $"Plex had to transcode this playback session ({probe.QualityProfile.Trim()}).";

        return new PlaybackDiagnosticAssessment(
            HealthLabel: "Investigate",
            Summary: summary,
            SuspectedCause: string.IsNullOrWhiteSpace(suspectedCause) ? "The client could not direct play one or more media streams." : suspectedCause,
            IsTranscode: true,
            HasError: false,
            Reasons: reasons);
    }

    private static string Normalize(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant();

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;
}
