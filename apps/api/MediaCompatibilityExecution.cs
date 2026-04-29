using System.Diagnostics;
using System.Text.Json;
using api;
using api.Data;
using api.Models;
using Microsoft.EntityFrameworkCore;

internal sealed record CompatibilityProbeResult(double? RuntimeMinutes, int? ExitCode, string Error, string PlayabilityLabel, string PlayabilitySummary);
public sealed record MediaCompatibilityJobProgress(double? Percent, double? ProcessedSeconds, double? TotalSeconds, double? EtaSeconds, string Speed, string Summary, bool IsComplete);

public static class MediaCompatibilityExecution
{
    public static async Task RunLoopAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ProcessQueuedJobsAsync(services, cancellationToken);
            }
            catch
            {
                // compatibility worker should never crash the host
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public static async Task<LibraryRemediationJob?> LoadNextQueuedPreviewJobAsync(MediaCloudDbContext db, CancellationToken cancellationToken)
    {
        var queuedJobs = await db.LibraryRemediationJobs
            .Where(x => x.ServiceKey == "ffmpeg" && x.CommandName == "ffmpeg-compat-preview" && x.Status == "Queued")
            .ToListAsync(cancellationToken);

        return queuedJobs
            .OrderBy(x => x.RequestedAtUtc)
            .FirstOrDefault();
    }

    public static async Task ReconcileStaleRunningPreviewJobsAsync(MediaCloudDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var staleJobs = await db.LibraryRemediationJobs
            .Where(x => x.ServiceKey == "ffmpeg"
                        && x.CommandName == "ffmpeg-compat-preview"
                        && x.Status == "Running"
                        && x.FinishedAtUtc == null)
            .ToListAsync(cancellationToken);

        if (staleJobs.Count == 0)
        {
            return;
        }

        foreach (var job in staleJobs)
        {
            var context = DeserializeContext(job.ReleaseContextJson);
            var outputPath = context?.OutputPath ?? string.Empty;
            var outputExists = !string.IsNullOrWhiteSpace(outputPath) && File.Exists(outputPath);
            var reachedCompletion = job.ProgressPercent.HasValue && job.ProgressPercent.Value >= 100d;

            if (!outputExists)
            {
                job.Status = "Queued";
                job.ProgressPercent = null;
                job.OutcomeSummary = "FFmpeg remediation was requeued after app restart interrupted the previous attempt.";
                job.ResultMessage = "Previous FFmpeg attempt was interrupted by app restart before a finished output was detected.";
            }
            else if (!reachedCompletion)
            {
                TryDeleteFile(outputPath);
                job.Status = "Queued";
                job.ProgressPercent = null;
                job.OutcomeSummary = "FFmpeg remediation was requeued after app restart interrupted the previous attempt and partial sidecar output was cleared.";
                job.ResultMessage = "Previous FFmpeg attempt was interrupted by app restart. MediaCloud removed the partial sidecar output before requeueing.";
            }
            else
            {
                job.Status = "Failed";
                job.OutcomeSummary = "FFmpeg remediation was interrupted by app restart after ffmpeg reached 100%. Review the sidecar output before rerunning.";
                job.ResultMessage = "App restart interrupted the running FFmpeg worker after progress had reached 100%. MediaCloud left the detected sidecar output in place for manual review.";
            }

            job.ProgressUpdatedAtUtc = now;
            job.LastCheckedAtUtc = now;
            job.UpdatedAtUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task ProcessQueuedJobsAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaCloudDbContext>();
        await ReconcileStaleRunningPreviewJobsAsync(db, DateTimeOffset.UtcNow, cancellationToken);
        var job = await LoadNextQueuedPreviewJobAsync(db, cancellationToken);
        if (job is null)
        {
            return;
        }

        var startedAt = DateTimeOffset.UtcNow;
        job.Status = "Running";
        job.OutcomeSummary = "Starting ffmpeg compatibility remediation.";
        job.ProgressPercent = null;
        job.ProgressUpdatedAtUtc = startedAt;
        job.LastCheckedAtUtc = startedAt;
        job.UpdatedAtUtc = startedAt;
        await db.SaveChangesAsync(cancellationToken);

        var context = DeserializeContext(job.ReleaseContextJson);
        if (context is null || string.IsNullOrWhiteSpace(context.CommandPreview) || string.IsNullOrWhiteSpace(context.OutputPath))
        {
            job.Status = "Failed";
            job.OutcomeSummary = "FFmpeg compatibility job is missing execution context.";
            job.ResultMessage = job.ReleaseContextJson;
            job.ProgressUpdatedAtUtc = DateTimeOffset.UtcNow;
            job.FinishedAtUtc = DateTimeOffset.UtcNow;
            job.LastCheckedAtUtc = job.FinishedAtUtc.Value;
            job.UpdatedAtUtc = job.FinishedAtUtc.Value;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        try
        {
            var totalDurationSeconds = GetMediaDurationSeconds(context.InputPath);
            var lastProgressPersistAt = DateTimeOffset.MinValue;
            var result = await ExecuteCommandAsync(
                context.CommandPreview,
                totalDurationSeconds,
                async progress =>
                {
                    var now = DateTimeOffset.UtcNow;
                    var shouldPersist = progress.IsComplete
                        || lastProgressPersistAt == DateTimeOffset.MinValue
                        || now - lastProgressPersistAt >= TimeSpan.FromSeconds(1);
                    if (!shouldPersist)
                    {
                        return;
                    }

                    job.ProgressPercent = progress.Percent;
                    job.ProgressUpdatedAtUtc = now;
                    job.OutcomeSummary = progress.Summary;
                    job.LastCheckedAtUtc = now;
                    job.UpdatedAtUtc = now;
                    lastProgressPersistAt = now;
                    await db.SaveChangesAsync(cancellationToken);
                },
                cancellationToken);
            var finishedAt = DateTimeOffset.UtcNow;
            var outputExists = File.Exists(context.OutputPath);
            var probe = outputExists ? ProbeMediaFile(context.OutputPath) : new CompatibilityProbeResult(null, null, "Output file was not created.", string.Empty, string.Empty);
            var succeeded = result.ExitCode == 0 && outputExists;
            var plexRefresh = succeeded
                ? await PlexRemediationRefresh.TryRefreshLibraryItemAsync(job.LibraryItemId, context.OutputPath, db, services.GetRequiredService<IHttpClientFactory>())
                : new PlexMetadataRefreshResult(false, false, string.Empty);
            job.Status = succeeded ? "Completed" : "Failed";
            job.ProgressPercent = succeeded ? 100d : job.ProgressPercent;
            job.ProgressUpdatedAtUtc = finishedAt;
            job.OutcomeSummary = BuildCompletionSummary(succeeded, context.OutputPath, result.ExitCode, plexRefresh);
            job.ResultMessage = BuildResultMessage(context, result, probe, plexRefresh);
            job.FinishedAtUtc = finishedAt;
            job.LastCheckedAtUtc = finishedAt;
            job.UpdatedAtUtc = finishedAt;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            var finishedAt = DateTimeOffset.UtcNow;
            job.Status = "Failed";
            job.OutcomeSummary = "FFmpeg compatibility remediation failed unexpectedly.";
            job.ResultMessage = ex.Message.Length > 1500 ? ex.Message[..1500] : ex.Message;
            job.ProgressUpdatedAtUtc = finishedAt;
            job.FinishedAtUtc = finishedAt;
            job.LastCheckedAtUtc = finishedAt;
            job.UpdatedAtUtc = finishedAt;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> ExecuteCommandAsync(
        string commandPreview,
        double? totalDurationSeconds,
        Func<MediaCompatibilityJobProgress, Task>? onProgress,
        CancellationToken cancellationToken)
    {
        var executableCommand = BuildProgressAwareCommand(commandPreview);
        var psi = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            ArgumentList = { "-lc", executableCommand },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            throw new InvalidOperationException("Failed to start ffmpeg compatibility process.");
        }

        using var registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
        });

        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var stdoutLines = new List<string>();
        var progressValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync();
            if (line is null)
            {
                break;
            }

            stdoutLines.Add(line);
            var equalsIndex = line.IndexOf('=');
            if (equalsIndex <= 0)
            {
                continue;
            }

            var key = line[..equalsIndex].Trim();
            var value = line[(equalsIndex + 1)..].Trim();
            progressValues[key] = value;
            if (!string.Equals(key, "progress", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var snapshot = TryBuildProgressSnapshot(progressValues, totalDurationSeconds);
            progressValues.Clear();
            if (snapshot is not null && onProgress is not null)
            {
                await onProgress(snapshot);
            }
        }

        await process.WaitForExitAsync(cancellationToken);
        var stderr = await stderrTask;
        var stdout = string.Join(Environment.NewLine, stdoutLines.TakeLast(80));
        return (process.ExitCode, stdout, stderr);
    }

    public static string BuildCompletionSummary(bool success, string outputPath, int exitCode, PlexMetadataRefreshResult plexRefresh)
    {
        var baseMessage = success
            ? $"FFmpeg compatibility remediation completed. Output: {outputPath}"
            : $"FFmpeg compatibility remediation failed (exit {exitCode}).";

        return string.IsNullOrWhiteSpace(plexRefresh.Message)
            ? baseMessage
            : $"{baseMessage} {plexRefresh.Message}";
    }

    private static string BuildResultMessage(MediaCompatibilityExecutionContext context, (int ExitCode, string Stdout, string Stderr) result, CompatibilityProbeResult probe, PlexMetadataRefreshResult plexRefresh)
    {
        var stderr = Trim(result.Stderr, 1000);
        var stdout = Trim(result.Stdout, 500);
        return JsonSerializer.Serialize(new
        {
            context.InputPath,
            context.OutputPath,
            result.ExitCode,
            stdout,
            stderr,
            probeRuntimeMinutes = probe.RuntimeMinutes,
            probeExitCode = probe.ExitCode,
            probeError = probe.Error,
            probeLabel = probe.PlayabilityLabel,
            probeSummary = probe.PlayabilitySummary,
            plexRefreshAttempted = plexRefresh.Attempted,
            plexRefreshSuccess = plexRefresh.Success,
            plexRefreshMessage = plexRefresh.Message
        });
    }

    private static MediaCompatibilityExecutionContext? DeserializeContext(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<MediaCompatibilityExecutionContext>(json);
        }
        catch
        {
            return null;
        }
    }

    private static string Trim(string value, int maxLength)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Length <= maxLength ? value : value[..maxLength];

    private static string BuildProgressAwareCommand(string commandPreview)
    {
        if (string.IsNullOrWhiteSpace(commandPreview))
        {
            return commandPreview;
        }

        var trimmed = commandPreview.TrimStart();
        if (!trimmed.StartsWith("ffmpeg", StringComparison.OrdinalIgnoreCase))
        {
            return commandPreview;
        }

        var leadingWhitespaceLength = commandPreview.Length - trimmed.Length;
        var leadingWhitespace = leadingWhitespaceLength > 0 ? commandPreview[..leadingWhitespaceLength] : string.Empty;
        var remainder = trimmed["ffmpeg".Length..].TrimStart();
        return $"{leadingWhitespace}ffmpeg -progress pipe:1 -nostats {remainder}";
    }

    public static MediaCompatibilityJobProgress? TryBuildProgressSnapshot(IReadOnlyDictionary<string, string> progressValues, double? totalDurationSeconds)
    {
        if (progressValues is null || progressValues.Count == 0)
        {
            return null;
        }

        var processedSeconds = TryParseProgressTimeSeconds(progressValues);
        var speed = progressValues.TryGetValue("speed", out var speedValue) ? speedValue.Trim() : string.Empty;
        var percent = totalDurationSeconds.HasValue && totalDurationSeconds.Value > 0d && processedSeconds.HasValue
            ? Math.Clamp((processedSeconds.Value / totalDurationSeconds.Value) * 100d, 0d, 100d)
            : (double?)null;
        var speedMultiplier = TryParseSpeedMultiplier(speed);
        var etaSeconds = totalDurationSeconds.HasValue && processedSeconds.HasValue && speedMultiplier.HasValue && speedMultiplier.Value > 0d
            ? Math.Max(0d, (totalDurationSeconds.Value - processedSeconds.Value) / speedMultiplier.Value)
            : (double?)null;
        var isComplete = progressValues.TryGetValue("progress", out var progressState)
            && string.Equals(progressState, "end", StringComparison.OrdinalIgnoreCase);

        if (!processedSeconds.HasValue && string.IsNullOrWhiteSpace(speed) && !isComplete)
        {
            return null;
        }

        return new MediaCompatibilityJobProgress(
            percent,
            processedSeconds,
            totalDurationSeconds,
            etaSeconds,
            speed,
            BuildProgressSummary(percent, processedSeconds, totalDurationSeconds, etaSeconds, speed, isComplete),
            isComplete);
    }

    private static string BuildProgressSummary(double? percent, double? processedSeconds, double? totalDurationSeconds, double? etaSeconds, string speed, bool isComplete)
    {
        if (isComplete)
        {
            return "FFmpeg reached the end of the remux. MediaCloud is validating the output now.";
        }

        var parts = new List<string>();
        if (percent.HasValue)
        {
            parts.Add($"{Math.Round(percent.Value)}%");
        }

        if (processedSeconds.HasValue && totalDurationSeconds.HasValue)
        {
            parts.Add($"{FormatDuration(processedSeconds.Value)} / {FormatDuration(totalDurationSeconds.Value)}");
        }
        else if (processedSeconds.HasValue)
        {
            parts.Add($"Processed {FormatDuration(processedSeconds.Value)}");
        }

        if (!string.IsNullOrWhiteSpace(speed))
        {
            parts.Add(speed);
        }

        if (etaSeconds.HasValue)
        {
            parts.Add($"ETA {FormatDuration(etaSeconds.Value)}");
        }

        return parts.Count == 0
            ? "FFmpeg compatibility remediation is running."
            : $"Remuxing… {string.Join(" · ", parts)}";
    }

    private static string FormatDuration(double totalSeconds)
    {
        var safeSeconds = Math.Max(0d, totalSeconds);
        var time = TimeSpan.FromSeconds(safeSeconds);
        return time.TotalHours >= 1d
            ? $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}"
            : $"00:{time.Minutes:00}:{time.Seconds:00}";
    }

    private static double? TryParseProgressTimeSeconds(IReadOnlyDictionary<string, string> progressValues)
    {
        if (progressValues.TryGetValue("out_time", out var outTime)
            && TimeSpan.TryParse(outTime, out var parsedTime))
        {
            return parsedTime.TotalSeconds;
        }

        if (progressValues.TryGetValue("out_time_us", out var outTimeUs)
            && double.TryParse(outTimeUs, out var parsedMicroseconds))
        {
            return parsedMicroseconds / 1_000_000d;
        }

        if (progressValues.TryGetValue("out_time_ms", out var outTimeMs)
            && double.TryParse(outTimeMs, out var parsedMaybeMicroseconds))
        {
            return parsedMaybeMicroseconds / 1_000_000d;
        }

        return null;
    }

    private static double? TryParseSpeedMultiplier(string speed)
    {
        if (string.IsNullOrWhiteSpace(speed))
        {
            return null;
        }

        var normalized = speed.Trim();
        if (normalized.EndsWith("x", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^1];
        }

        return double.TryParse(normalized, out var parsed) ? parsed : null;
    }

    private static double? GetMediaDurationSeconds(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        return ProbeMediaFile(filePath).RuntimeMinutes is { } runtimeMinutes
            ? runtimeMinutes * 60d
            : null;
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

    private static CompatibilityProbeResult ProbeMediaFile(string filePath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ffprobe",
            Arguments = $"-v error -show_entries format=duration,format_name,bit_rate:stream=codec_type,codec_name,profile,width,height,pix_fmt -of json \"{filePath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        if (proc is null)
        {
            return new CompatibilityProbeResult(null, null, "Failed to start ffprobe process.", string.Empty, string.Empty);
        }

        if (!proc.WaitForExit(5000))
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            return new CompatibilityProbeResult(null, null, "ffprobe timed out after 5s.", string.Empty, string.Empty);
        }

        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        var exitCode = proc.ExitCode;
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return new CompatibilityProbeResult(null, exitCode, string.IsNullOrWhiteSpace(stderr) ? $"ffprobe exited with code {exitCode}." : stderr, string.Empty, string.Empty);
        }

        try
        {
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            var format = root.TryGetProperty("format", out var formatNode) ? formatNode : default;
            var streams = root.TryGetProperty("streams", out var streamNode) && streamNode.ValueKind == JsonValueKind.Array
                ? streamNode.EnumerateArray().ToArray()
                : [];
            var duration = TryGetDouble(format, "duration");
            var runtimeMinutes = duration.HasValue ? duration.Value / 60d : (double?)null;
            var containerNames = GetString(format, "format_name")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
            var videoStream = streams.FirstOrDefault(x => string.Equals(GetString(x, "codec_type"), "video", StringComparison.OrdinalIgnoreCase));
            var audioCodecs = streams.Where(x => string.Equals(GetString(x, "codec_type"), "audio", StringComparison.OrdinalIgnoreCase)).Select(x => GetString(x, "codec_name") ?? string.Empty).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var subtitleCodecs = streams.Where(x => string.Equals(GetString(x, "codec_type"), "subtitle", StringComparison.OrdinalIgnoreCase)).Select(x => GetString(x, "codec_name") ?? string.Empty).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var probeInfo = new MediaPlayabilityProbeInfo(
                containerNames,
                GetString(videoStream, "codec_name") ?? string.Empty,
                GetString(videoStream, "profile") ?? string.Empty,
                GetString(videoStream, "pix_fmt") ?? string.Empty,
                TryGetInt(videoStream, "width"),
                TryGetInt(videoStream, "height"),
                TryGetLong(format, "bit_rate"),
                audioCodecs,
                subtitleCodecs);
            var assessment = MediaPlayabilityScoring.Evaluate(probeInfo);
            return new CompatibilityProbeResult(runtimeMinutes, exitCode, string.Empty, assessment.Label, assessment.Summary);
        }
        catch (Exception ex)
        {
            return new CompatibilityProbeResult(null, exitCode, ex.Message, string.Empty, string.Empty);
        }
    }

    private static string? GetString(JsonElement element, string propertyName)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()
            : null;

    private static int? TryGetInt(JsonElement element, string propertyName)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var parsed)
            ? parsed
            : null;

    private static long? TryGetLong(JsonElement element, string propertyName)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value) && value.TryGetInt64(out var parsed)
            ? parsed
            : null;

    private static double? TryGetDouble(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var parsed))
        {
            return parsed;
        }

        return value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), out parsed) ? parsed : null;
    }
}
