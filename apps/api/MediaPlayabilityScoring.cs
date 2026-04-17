public enum MediaPlayabilityScore
{
    Excellent,
    Good,
    Caution,
    Risky,
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
    int CompatibilityScore,
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
    IReadOnlyList<string> Reasons,
    int CompatibilityScore = 0);

public static class MediaPlayabilityScoring
{
    public static MediaPlayabilityAssessment Evaluate(MediaPlayabilityProbeInfo probe)
    {
        var reasons = new List<string>();
        var compatibilityScore = 100;

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
        var isTenBit = profile.Contains("10", StringComparison.OrdinalIgnoreCase) || pixelFormat.Contains("10", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(videoCodec))
        {
            compatibilityScore -= 20;
            reasons.Add("No primary video codec was detected (-20).");
        }

        compatibilityScore -= EvaluateContainer(primaryContainer, reasons);
        compatibilityScore -= EvaluateVideo(videoCodec, isTenBit, reasons);
        compatibilityScore -= EvaluateAudio(audioCodecs, reasons);
        compatibilityScore -= EvaluateSubtitles(subtitleCodecs, reasons);
        compatibilityScore -= EvaluateBitrateAndResolution(probe.Height, bitrateMbps, reasons);
        compatibilityScore -= EvaluateProbeHealth(probe, reasons);
        compatibilityScore -= EvaluateCombinationRules(primaryContainer, videoCodec, isTenBit, probe.Height, bitrateMbps, audioCodecs, subtitleCodecs, reasons);

        compatibilityScore = Math.Clamp(compatibilityScore, 0, 100);

        var score = compatibilityScore switch
        {
            >= 90 => MediaPlayabilityScore.Excellent,
            >= 75 => MediaPlayabilityScore.Good,
            >= 50 => MediaPlayabilityScore.Caution,
            >= 25 => MediaPlayabilityScore.Risky,
            _ => MediaPlayabilityScore.Problematic
        };

        var summary = score switch
        {
            MediaPlayabilityScore.Excellent => "Broad Plex direct-play compatibility.",
            MediaPlayabilityScore.Good => "Should stream well, with minor compatibility caveats.",
            MediaPlayabilityScore.Caution => "May direct stream or transcode on some Plex clients.",
            MediaPlayabilityScore.Risky => "Likely to create user-visible Plex playback issues on mixed devices.",
            _ => "Poor candidate for broad Plex playback without remediation."
        };

        var label = score switch
        {
            MediaPlayabilityScore.Excellent => "Excellent",
            MediaPlayabilityScore.Good => "Good",
            MediaPlayabilityScore.Caution => "Caution",
            MediaPlayabilityScore.Risky => "Risky",
            _ => "Problematic"
        };

        return new MediaPlayabilityAssessment(score, compatibilityScore, label, summary, reasons);
    }

    private static int EvaluateContainer(string primaryContainer, List<string> reasons)
    {
        if (string.IsNullOrWhiteSpace(primaryContainer)) return 0;
        if (primaryContainer is "mp4" or "mov") return 0;
        if (primaryContainer is "matroska" or "webm" or "mkv")
        {
            reasons.Add("MKV/Matroska container (-5).");
            return 5;
        }

        if (primaryContainer is "avi" or "asf" or "wmv" or "flv" or "mpegts" or "mpeg" or "mpg" or "m2ts")
        {
            reasons.Add($"{primaryContainer.ToUpperInvariant()} container (-15).");
            return 15;
        }

        return 0;
    }

    private static int EvaluateVideo(string videoCodec, bool isTenBit, List<string> reasons)
    {
        var penalty = 0;
        if (videoCodec is "h264" or "avc1")
        {
        }
        else if (videoCodec is "hevc" or "h265")
        {
            penalty += 8;
            reasons.Add("HEVC video (-8).");
        }
        else if (videoCodec is "av1")
        {
            penalty += 20;
            reasons.Add("AV1 video (-20).");
        }
        else if (videoCodec is "vp9")
        {
            penalty += 15;
            reasons.Add("VP9 video (-15).");
        }
        else if (!string.IsNullOrWhiteSpace(videoCodec))
        {
            penalty += 20;
            reasons.Add($"{videoCodec.ToUpperInvariant()} video (-20).");
        }

        if (isTenBit)
        {
            penalty += 10;
            reasons.Add("10-bit video (-10).");
        }

        return penalty;
    }

    private static int EvaluateAudio(IEnumerable<string> audioCodecs, List<string> reasons)
    {
        var penalty = 0;
        foreach (var codec in audioCodecs)
        {
            if (codec is "aac")
            {
                continue;
            }

            if (codec is "ac3" or "eac3")
            {
                penalty += 2;
                reasons.Add($"{codec.ToUpperInvariant()} audio (-2).");
                continue;
            }

            if (codec is "mp3")
            {
                penalty += 3;
                reasons.Add("MP3 audio (-3).");
                continue;
            }

            if (codec is "flac" or "opus" or "pcm_s16le" or "pcm_s24le")
            {
                penalty += 5;
                reasons.Add($"{codec.ToUpperInvariant()} audio (-5).");
                continue;
            }

            if (codec is "dts" or "dca")
            {
                penalty += 10;
                reasons.Add($"{codec.ToUpperInvariant()} audio (-10).");
                continue;
            }

            if (codec is "truehd")
            {
                penalty += 12;
                reasons.Add("TRUEHD audio (-12).");
                continue;
            }

            penalty += 10;
            reasons.Add($"{codec.ToUpperInvariant()} audio (-10).");
        }

        return penalty;
    }

    private static int EvaluateSubtitles(IEnumerable<string> subtitleCodecs, List<string> reasons)
    {
        var penalty = 0;
        foreach (var codec in subtitleCodecs)
        {
            if (codec is "subrip" or "srt" or "mov_text" or "webvtt")
            {
                continue;
            }

            if (codec is "ass" or "ssa")
            {
                penalty += 5;
                reasons.Add($"{codec.ToUpperInvariant()} subtitles (-5).");
                continue;
            }

            var label = codec switch
            {
                "hdmv_pgs_subtitle" => "PGS",
                "dvd_subtitle" => "VobSub",
                _ => codec.ToUpperInvariant()
            };
            penalty += 15;
            reasons.Add($"{label} subtitles (-15).");
        }

        return penalty;
    }

    private static int EvaluateBitrateAndResolution(int? height, double? bitrateMbps, List<string> reasons)
    {
        var penalty = 0;
        if (height.GetValueOrDefault() > 1080)
        {
            penalty += 10;
            reasons.Add("Above preferred resolution (-10).");
        }

        if (bitrateMbps.HasValue)
        {
            if (height.GetValueOrDefault() >= 2160 && bitrateMbps.Value > 20)
            {
                penalty += 15;
                reasons.Add("4K with high bitrate (-15).");
            }
            else if (bitrateMbps.Value > 20)
            {
                penalty += 5;
                reasons.Add("High bitrate for target resolution (-5).");
            }
        }

        return penalty;
    }

    private static int EvaluateProbeHealth(MediaPlayabilityProbeInfo probe, List<string> reasons)
    {
        if (string.IsNullOrWhiteSpace(probe.VideoCodec) && probe.AudioCodecs.Count == 0 && probe.SubtitleCodecs.Count == 0)
        {
            reasons.Add("Incomplete probe / weak metadata (-5).");
            return 5;
        }

        return 0;
    }

    private static int EvaluateCombinationRules(string primaryContainer, string videoCodec, bool isTenBit, int? height, double? bitrateMbps, IReadOnlyList<string> audioCodecs, IReadOnlyList<string> subtitleCodecs, List<string> reasons)
    {
        var penalty = 0;
        var hasImageSubtitles = subtitleCodecs.Any(x => x is "hdmv_pgs_subtitle" or "dvd_subtitle");
        var hasHighRiskAudio = audioCodecs.Any(x => x is "dts" or "dca" or "truehd");

        if (videoCodec is "hevc" or "h265")
        {
            if (isTenBit)
            {
                penalty += 5;
                reasons.Add("HEVC + 10-bit combo (-5).");
            }

            if (height.GetValueOrDefault() >= 2160 && bitrateMbps.GetValueOrDefault() > 20)
            {
                penalty += 5;
                reasons.Add("4K + HEVC + high bitrate combo (-5).");
            }
        }

        if (primaryContainer is "matroska" or "webm" or "mkv")
        {
            if (hasImageSubtitles)
            {
                penalty += 5;
                reasons.Add("MKV + image subtitles combo (-5).");
            }
        }

        if (hasHighRiskAudio && hasImageSubtitles)
        {
            penalty += 5;
            reasons.Add("High-risk audio + image subtitles combo (-5).");
        }

        return penalty;
    }

    private static IEnumerable<string> SplitNormalizedValues(string? raw)
        => (raw ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize)
            .Where(x => x.Length > 0);

    private static string Normalize(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant();
}
