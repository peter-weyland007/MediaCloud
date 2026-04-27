using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using api.Models;

public sealed record MediaPlaybackSmokeTestRequest(int SampleSeconds = 45, int SeekPercent = 35, bool RunSample = true);

public sealed record MediaPlaybackSmokeTestPlanResponse(
    long LibraryItemId,
    string TargetProfileName,
    string RecommendationKey,
    string RecommendationTitle,
    string LikelyDecision,
    string VerdictSeverity,
    string OperatorSummary,
    string WhySummary,
    IReadOnlyList<string> Reasons,
    bool SamplePlanned,
    string SampleCommandPreview,
    string SampleOutputPath,
    int SampleSeconds,
    int SeekPercent,
    int SeekSeconds);

public sealed record MediaPlaybackSmokeTestResponse(
    long LibraryItemId,
    string TargetProfileName,
    string RecommendationKey,
    string RecommendationTitle,
    string LikelyDecision,
    string VerdictSeverity,
    string OperatorSummary,
    string WhySummary,
    IReadOnlyList<string> Reasons,
    bool SamplePlanned,
    string SampleCommandPreview,
    string SampleOutputPath,
    int SampleSeconds,
    int SeekPercent,
    int SeekSeconds,
    bool SampleAttempted,
    bool SampleSucceeded,
    string SampleSummary,
    string SampleProbeSummary,
    int? ExitCode,
    string ErrorMessage,
    DateTimeOffset TestedAtUtc);

internal sealed record MediaPlaybackSmokeProbeResult(double? RuntimeMinutes, int? ExitCode, string Error, string Summary);

public static class MediaPlaybackSmokeTestEngine
{
    public static MediaPlaybackSmokeTestPlanResponse BuildPlan(
        LibraryItem item,
        MediaCompatibilityRecommendationResponse recommendation,
        int sampleSeconds = 45,
        int seekPercent = 35)
    {
        var normalizedSampleSeconds = Math.Clamp(sampleSeconds, 15, 120);
        var normalizedSeekPercent = Math.Clamp(seekPercent, 0, 90);
        var reasons = recommendation.Reasons
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var likelyDecision = "Direct Play likely";
        var verdictSeverity = "success";
        var operatorSummary = "Current file appears close to the active playback target. A smoke sample is optional rather than mandatory.";

        if (recommendation.SafeToQueue)
        {
            likelyDecision = "Direct Stream likely";
            verdictSeverity = "warning";
            operatorSummary = "MediaCloud sees a low-risk compatibility mismatch. Run a short ffmpeg compatibility sample to approximate whether Plex would remux or normalize this file cleanly.";
        }
        else if (string.Equals(recommendation.RecommendationKey, "manual_video_review", StringComparison.OrdinalIgnoreCase))
        {
            likelyDecision = "Transcode required";
            verdictSeverity = "danger";
            operatorSummary = "The active profile points to a real video transcode path. The smoke test will only validate a short sample and should be treated as manual review evidence, not proof that Plex will behave identically.";
        }
        else if (string.Equals(recommendation.RecommendationKey, "manual_subtitle_review", StringComparison.OrdinalIgnoreCase))
        {
            likelyDecision = "Subtitle path risky";
            verdictSeverity = "danger";
            operatorSummary = "Subtitle format is likely to trigger burn-in or force a transcode path. Use the smoke test as evidence before queuing any broader remediation.";
        }
        else if (string.Equals(recommendation.RecommendationKey, "manual_disc_image_review", StringComparison.OrdinalIgnoreCase))
        {
            likelyDecision = "Disc image/manual path";
            verdictSeverity = "danger";
            operatorSummary = "Disc-image packaging sits outside the normal safe remux workflow. Treat this as manual investigation territory.";
        }

        var baseCommand = recommendation.SafeToQueue
            ? recommendation.CommandPreview
            : recommendation.ManualCommandPreview;
        var seekSeconds = CalculateSeekSeconds(item.ActualRuntimeMinutes, normalizedSampleSeconds, normalizedSeekPercent);
        var sampleOutputPath = BuildSampleOutputPath(baseCommand, item.PrimaryFilePath, recommendation.RecommendationKey);
        var sampleCommandPreview = BuildSampleCommand(baseCommand, sampleOutputPath, normalizedSampleSeconds, seekSeconds);
        var samplePlanned = !string.IsNullOrWhiteSpace(sampleCommandPreview) && !string.IsNullOrWhiteSpace(sampleOutputPath);

        return new MediaPlaybackSmokeTestPlanResponse(
            item.Id,
            string.IsNullOrWhiteSpace(recommendation.ActivePresetName) ? "Active media profile" : recommendation.ActivePresetName,
            recommendation.RecommendationKey,
            recommendation.RecommendationTitle,
            likelyDecision,
            verdictSeverity,
            operatorSummary,
            recommendation.WhySummary,
            reasons,
            samplePlanned,
            sampleCommandPreview,
            sampleOutputPath,
            normalizedSampleSeconds,
            normalizedSeekPercent,
            seekSeconds);
    }

    public static MediaPlaybackSmokeTestResponse BuildNotRunResponse(MediaPlaybackSmokeTestPlanResponse plan, string summary)
        => new(
            plan.LibraryItemId,
            plan.TargetProfileName,
            plan.RecommendationKey,
            plan.RecommendationTitle,
            plan.LikelyDecision,
            plan.VerdictSeverity,
            plan.OperatorSummary,
            plan.WhySummary,
            plan.Reasons,
            plan.SamplePlanned,
            plan.SampleCommandPreview,
            plan.SampleOutputPath,
            plan.SampleSeconds,
            plan.SeekPercent,
            plan.SeekSeconds,
            false,
            false,
            summary,
            string.Empty,
            null,
            string.Empty,
            DateTimeOffset.UtcNow);

    public static async Task<MediaPlaybackSmokeTestResponse> RunAsync(MediaPlaybackSmokeTestPlanResponse plan, CancellationToken cancellationToken)
    {
        if (!plan.SamplePlanned)
        {
            return BuildNotRunResponse(plan, "MediaCloud could not build a safe smoke-test command from the current recommendation.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(plan.SampleOutputPath) ?? Path.GetTempPath());
        if (File.Exists(plan.SampleOutputPath))
        {
            try
            {
                File.Delete(plan.SampleOutputPath);
            }
            catch
            {
                return BuildNotRunResponse(plan, "MediaCloud could not clear the prior playback smoke-test sample output path.");
            }
        }

        var result = await ExecuteCommandAsync(plan.SampleCommandPreview, cancellationToken);
        var outputExists = File.Exists(plan.SampleOutputPath);
        var probe = outputExists
            ? ProbeMediaFile(plan.SampleOutputPath)
            : new MediaPlaybackSmokeProbeResult(null, null, "Smoke-test output file was not created.", string.Empty);
        if (outputExists)
        {
            TryDeleteFile(plan.SampleOutputPath);
        }

        var success = result.ExitCode == 0 && outputExists && string.IsNullOrWhiteSpace(probe.Error);
        var summary = success
            ? $"Playback smoke test succeeded. MediaCloud created a {plan.SampleSeconds}s sample and recorded the probe metrics."
            : $"Playback smoke test failed (exit {result.ExitCode}).";
        var error = success
            ? string.Empty
            : FirstNonEmpty(probe.Error, Trim(result.Stderr, 1200), Trim(result.Stdout, 600));

        return new MediaPlaybackSmokeTestResponse(
            plan.LibraryItemId,
            plan.TargetProfileName,
            plan.RecommendationKey,
            plan.RecommendationTitle,
            plan.LikelyDecision,
            plan.VerdictSeverity,
            plan.OperatorSummary,
            plan.WhySummary,
            plan.Reasons,
            plan.SamplePlanned,
            plan.SampleCommandPreview,
            string.Empty,
            plan.SampleSeconds,
            plan.SeekPercent,
            plan.SeekSeconds,
            true,
            success,
            summary,
            probe.Summary,
            result.ExitCode,
            error,
            DateTimeOffset.UtcNow);
    }

    private static int CalculateSeekSeconds(double? runtimeMinutes, int sampleSeconds, int seekPercent)
    {
        if (!runtimeMinutes.HasValue || runtimeMinutes.Value <= 0)
        {
            return 0;
        }

        var totalSeconds = (int)Math.Round(runtimeMinutes.Value * 60d, MidpointRounding.AwayFromZero);
        if (totalSeconds <= sampleSeconds)
        {
            return 0;
        }

        var candidate = (int)Math.Round(totalSeconds * (seekPercent / 100d), MidpointRounding.AwayFromZero);
        return Math.Clamp(candidate, 0, Math.Max(0, totalSeconds - sampleSeconds));
    }

    private static string BuildSampleOutputPath(string commandPreview, string primaryFilePath, string recommendationKey)
    {
        var originalOutput = ExtractQuotedSegments(commandPreview).LastOrDefault();
        var sourcePath = ExtractQuotedSegments(commandPreview).FirstOrDefault();
        var extension = Path.GetExtension(originalOutput);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = Path.GetExtension(primaryFilePath);
        }

        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".mp4";
        }

        var sampleDirectory = Path.Combine(Path.GetTempPath(), "mediacloud", "playback-smoke-tests");
        var fileStem = !string.IsNullOrWhiteSpace(originalOutput)
            ? Path.GetFileNameWithoutExtension(originalOutput)
            : Path.GetFileNameWithoutExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(fileStem))
        {
            fileStem = $"library-item-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        }

        fileStem = Regex.Replace(fileStem, "[^A-Za-z0-9._-]+", "-").Trim('-');
        if (string.IsNullOrWhiteSpace(fileStem))
        {
            fileStem = "sample";
        }

        return Path.Combine(sampleDirectory, $"{fileStem}.playback-smoke-sample{extension}");
    }

    private static string BuildSampleCommand(string commandPreview, string outputPath, int sampleSeconds, int seekSeconds)
    {
        if (string.IsNullOrWhiteSpace(commandPreview) || string.IsNullOrWhiteSpace(outputPath))
        {
            return string.Empty;
        }

        var updatedOutput = ReplaceLastQuotedSegment(commandPreview, outputPath);
        if (string.IsNullOrWhiteSpace(updatedOutput))
        {
            return string.Empty;
        }

        var inputIndex = updatedOutput.IndexOf(" -i ", StringComparison.OrdinalIgnoreCase);
        if (inputIndex <= 0)
        {
            return updatedOutput;
        }

        var prefix = updatedOutput[..inputIndex];
        var suffix = updatedOutput[inputIndex..];
        return $"{prefix} -ss {seekSeconds} -t {sampleSeconds}{suffix}";
    }

    private static string ReplaceLastQuotedSegment(string commandPreview, string replacement)
    {
        var parsed = ParseCommandPreview(commandPreview);
        if (parsed.Arguments.Count == 0)
        {
            return string.Empty;
        }

        parsed.Arguments[^1] = replacement;
        return BuildCommandString(parsed.Arguments);
    }

    private static IReadOnlyList<string> ExtractQuotedSegments(string commandPreview)
        => ParseCommandPreview(commandPreview).QuotedSegments;

    private static (List<string> Arguments, List<string> QuotedSegments) ParseCommandPreview(string commandPreview)
    {
        var arguments = new List<string>();
        var quotedSegments = new List<string>();
        if (string.IsNullOrWhiteSpace(commandPreview))
        {
            return (arguments, quotedSegments);
        }

        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        var tokenWasQuoted = false;

        for (var index = 0; index < commandPreview.Length; index++)
        {
            var ch = commandPreview[index];
            if (ch == '\\' && index + 1 < commandPreview.Length)
            {
                var next = commandPreview[index + 1];
                if (next == '"' || next == '\\')
                {
                    current.Append(next);
                    index++;
                    continue;
                }
            }

            if (ch == '"')
            {
                inQuotes = !inQuotes;
                tokenWasQuoted = true;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(ch))
            {
                FlushToken();
                continue;
            }

            current.Append(ch);
        }

        FlushToken();
        return (arguments, quotedSegments);

        void FlushToken()
        {
            if (current.Length == 0)
            {
                tokenWasQuoted = false;
                return;
            }

            var token = current.ToString();
            arguments.Add(token);
            if (tokenWasQuoted)
            {
                quotedSegments.Add(token);
            }

            current.Clear();
            tokenWasQuoted = false;
        }
    }

    private static string BuildCommandString(IReadOnlyList<string> arguments)
        => string.Join(" ", arguments.Select(QuoteCommandArgumentIfNeeded));

    private static string QuoteCommandArgumentIfNeeded(string argument)
    {
        if (string.IsNullOrEmpty(argument))
        {
            return "\"\"";
        }

        if (argument.Any(char.IsWhiteSpace) || argument.Contains('"', StringComparison.Ordinal) || argument.Contains('\\', StringComparison.Ordinal))
        {
            return $"\"{argument.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
        }

        return argument;
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> ExecuteCommandAsync(string command, CancellationToken cancellationToken)
    {
        var parsed = ParseCommandPreview(command);
        if (parsed.Arguments.Count == 0)
        {
            throw new InvalidOperationException("Playback smoke-test command was empty.");
        }

        var psi = new ProcessStartInfo
        {
            FileName = parsed.Arguments[0],
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in parsed.Arguments.Skip(1))
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi);
        if (process is null)
        {
            throw new InvalidOperationException("Failed to start ffmpeg playback smoke-test process.");
        }

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
        });

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static MediaPlaybackSmokeProbeResult ProbeMediaFile(string filePath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ffprobe",
            Arguments = $"-v error -show_entries format=duration,size,format_name -of json \"{filePath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            return new MediaPlaybackSmokeProbeResult(null, null, "Failed to start ffprobe for playback smoke-test output.", string.Empty);
        }

        if (!process.WaitForExit(5000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return new MediaPlaybackSmokeProbeResult(null, null, "ffprobe timed out while checking playback smoke-test output.", string.Empty);
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return new MediaPlaybackSmokeProbeResult(null, process.ExitCode, string.IsNullOrWhiteSpace(stderr) ? $"ffprobe exited with code {process.ExitCode}." : stderr, string.Empty);
        }

        try
        {
            using var doc = JsonDocument.Parse(stdout);
            var format = doc.RootElement.TryGetProperty("format", out var formatNode) ? formatNode : default;
            var duration = format.ValueKind != JsonValueKind.Undefined && format.TryGetProperty("duration", out var durationNode) && durationNode.ValueKind == JsonValueKind.String && double.TryParse(durationNode.GetString(), out var parsedDuration)
                ? parsedDuration / 60d
                : (double?)null;
            var size = format.ValueKind != JsonValueKind.Undefined && format.TryGetProperty("size", out var sizeNode) && sizeNode.ValueKind == JsonValueKind.String && long.TryParse(sizeNode.GetString(), out var parsedSize)
                ? parsedSize
                : (long?)null;
            var formatName = format.ValueKind != JsonValueKind.Undefined && format.TryGetProperty("format_name", out var formatNameNode)
                ? formatNameNode.GetString() ?? string.Empty
                : string.Empty;
            var summary = $"Sample probe: {(duration.HasValue ? $"{duration.Value * 60d:0.#} sec" : "unknown duration")}, {(size.HasValue ? $"{size.Value / 1_000_000d:0.##} MB" : "unknown size")}, format {FirstNonEmpty(formatName, "unknown")}.";
            return new MediaPlaybackSmokeProbeResult(duration, process.ExitCode, string.Empty, summary);
        }
        catch (Exception ex)
        {
            return new MediaPlaybackSmokeProbeResult(null, process.ExitCode, ex.Message, string.Empty);
        }
    }

    private static void TryDeleteFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return;
        }

        try
        {
            File.Delete(filePath);
        }
        catch
        {
        }
    }

    private static string Trim(string value, int maxLength)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Length <= maxLength ? value : value[..maxLength];

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;
}
