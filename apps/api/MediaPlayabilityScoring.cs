public enum MediaPlayabilityScore
{
    Excellent,
    Good,
    TranscodeLikely,
    Problematic
}

public sealed record MediaPlayabilityProbeInfo(
    IReadOnlyList<string> ContainerNames,
    string VideoCodec,
    string VideoProfile,
    string PixelFormat,
    int? Width,
    int? Height,
    long? BitrateBitsPerSecond,
    IReadOnlyList<string> AudioCodecs,
    IReadOnlyList<string> SubtitleCodecs);

public sealed record MediaPlayabilityAssessment(
    MediaPlayabilityScore Score,
    string Label,
    string Summary,
    IReadOnlyList<string> Reasons);

public sealed record MediaPlayabilityStoredDetails(
    IReadOnlyList<string> ContainerNames,
    string VideoCodec,
    string VideoProfile,
    string PixelFormat,
    int? Width,
    int? Height,
    long? BitrateBitsPerSecond,
    IReadOnlyList<string> AudioCodecs,
    IReadOnlyList<string> SubtitleCodecs,
    IReadOnlyList<string> Reasons);

public static class MediaPlayabilityScoring
{
    public static MediaPlayabilityAssessment Evaluate(MediaPlayabilityProbeInfo probe)
    {
        var reasons = new List<string>();
        var severeCount = 0;
        var cautionCount = 0;

        var containers = probe.ContainerNames
            .SelectMany(SplitNormalizedValues)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var primaryContainer = containers.FirstOrDefault() ?? string.Empty;
        var videoCodec = Normalize(probe.VideoCodec);
        var profile = Normalize(probe.VideoProfile);
        var pixelFormat = Normalize(probe.PixelFormat);
        var audioCodecs = probe.AudioCodecs.Select(Normalize).Where(x => x.Length > 0).Distinct().ToArray();
        var subtitleCodecs = probe.SubtitleCodecs.Select(Normalize).Where(x => x.Length > 0).Distinct().ToArray();
        var bitrateMbps = probe.BitrateBitsPerSecond.HasValue && probe.BitrateBitsPerSecond.Value > 0
            ? probe.BitrateBitsPerSecond.Value / 1_000_000d
            : (double?)null;

        if (string.IsNullOrWhiteSpace(videoCodec))
        {
            severeCount++;
            reasons.Add("No primary video codec was detected.");
        }

        if (IsProblematicContainer(primaryContainer))
        {
            severeCount++;
            reasons.Add($"{primaryContainer.ToUpperInvariant()} container is a poor Plex cross-device target.");
        }
        else if (IsCautionContainer(primaryContainer))
        {
            cautionCount++;
            reasons.Add($"{primaryContainer.ToUpperInvariant()} container may direct stream instead of direct play on some clients.");
        }

        if (videoCodec is "h264" or "avc1")
        {
            if (profile.Contains("10", StringComparison.OrdinalIgnoreCase) || pixelFormat.Contains("10", StringComparison.OrdinalIgnoreCase))
            {
                cautionCount++;
                reasons.Add("10-bit H.264 is less reliable across Plex clients.");
            }
        }
        else if (videoCodec is "hevc" or "h265")
        {
            cautionCount++;
            reasons.Add("HEVC often plays fine, but older Roku/Xbox/TV clients may transcode it.");

            if (profile.Contains("10", StringComparison.OrdinalIgnoreCase) || pixelFormat.Contains("10", StringComparison.OrdinalIgnoreCase))
            {
                cautionCount++;
                reasons.Add("10-bit HEVC raises transcode risk on weaker clients.");
            }
        }
        else if (videoCodec is "av1" or "vp9")
        {
            severeCount++;
            reasons.Add($"{videoCodec.ToUpperInvariant()} is still inconsistent across mixed Plex clients.");
        }
        else if (!string.IsNullOrWhiteSpace(videoCodec))
        {
            severeCount++;
            reasons.Add($"{videoCodec.ToUpperInvariant()} video usually needs transcoding for broad Plex compatibility.");
        }

        foreach (var codec in audioCodecs)
        {
            if (codec is "aac" or "ac3" or "eac3" or "mp3")
            {
                continue;
            }

            if (codec is "flac" or "opus" or "pcm_s16le" or "pcm_s24le")
            {
                cautionCount++;
                reasons.Add($"{codec.ToUpperInvariant()} audio may require direct stream or transcode on some clients.");
                continue;
            }

            severeCount++;
            reasons.Add($"{codec.ToUpperInvariant()} audio is a common Plex transcode trigger.");
        }

        foreach (var codec in subtitleCodecs)
        {
            if (codec is "subrip" or "srt" or "mov_text" or "webvtt")
            {
                continue;
            }

            if (codec is "ass" or "ssa")
            {
                cautionCount++;
                reasons.Add($"{codec.ToUpperInvariant()} subtitles may need burn-in on some clients.");
                continue;
            }

            severeCount++;
            var label = codec switch
            {
                "hdmv_pgs_subtitle" => "PGS",
                "dvd_subtitle" => "VobSub",
                _ => codec.ToUpperInvariant()
            };
            reasons.Add($"{label} subtitles often force transcoding or burn-in.");
        }

        if (bitrateMbps.HasValue)
        {
            var highBitrateThreshold = (probe.Height ?? 0) >= 2160 ? 35d : 20d;
            if (bitrateMbps.Value > highBitrateThreshold)
            {
                cautionCount++;
                reasons.Add($"Bitrate is {bitrateMbps.Value:0.#} Mbps, which is high for easy streaming to mixed Plex clients.");
            }
        }

        var score = severeCount >= 3
            ? MediaPlayabilityScore.Problematic
            : severeCount >= 1 || cautionCount >= 3
                ? MediaPlayabilityScore.TranscodeLikely
                : cautionCount >= 1
                    ? MediaPlayabilityScore.Good
                    : MediaPlayabilityScore.Excellent;

        var summary = score switch
        {
            MediaPlayabilityScore.Excellent => "Broad Plex direct-play compatibility.",
            MediaPlayabilityScore.Good => "Should stream well, with a few client-specific caveats.",
            MediaPlayabilityScore.TranscodeLikely => "Likely to trigger Plex transcoding on some clients.",
            _ => "Problematic for broad Plex playback without transcoding or remediation."
        };

        return new MediaPlayabilityAssessment(score, score switch
        {
            MediaPlayabilityScore.Excellent => "Excellent",
            MediaPlayabilityScore.Good => "Good",
            MediaPlayabilityScore.TranscodeLikely => "Transcode likely",
            _ => "Problematic"
        }, summary, reasons);
    }

    private static IEnumerable<string> SplitNormalizedValues(string? raw)
        => (raw ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize)
            .Where(x => x.Length > 0);

    private static string Normalize(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant();

    private static bool IsProblematicContainer(string container)
        => container is "avi" or "asf" or "wmv" or "flv" or "mpegts" or "mpeg" or "mpg" or "m2ts";

    private static bool IsCautionContainer(string container)
        => container is "matroska" or "webm" or "mkv";
}
