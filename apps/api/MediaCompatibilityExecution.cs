using System.Diagnostics;
using System.Text.Json;
using api;
using api.Data;
using api.Models;
using Microsoft.EntityFrameworkCore;

internal sealed record CompatibilityProbeResult(double? RuntimeMinutes, int? ExitCode, string Error, string PlayabilityLabel, string PlayabilitySummary);

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

    private static async Task ProcessQueuedJobsAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaCloudDbContext>();
        var job = await LoadNextQueuedPreviewJobAsync(db, cancellationToken);
        if (job is null)
        {
            return;
        }

        var startedAt = DateTimeOffset.UtcNow;
        job.Status = "Running";
        job.OutcomeSummary = "Running ffmpeg compatibility remediation.";
        job.LastCheckedAtUtc = startedAt;
        job.UpdatedAtUtc = startedAt;
        await db.SaveChangesAsync(cancellationToken);

        var context = DeserializeContext(job.ReleaseContextJson);
        if (context is null || string.IsNullOrWhiteSpace(context.CommandPreview) || string.IsNullOrWhiteSpace(context.OutputPath))
        {
            job.Status = "Failed";
            job.OutcomeSummary = "FFmpeg compatibility job is missing execution context.";
            job.ResultMessage = job.ReleaseContextJson;
            job.FinishedAtUtc = DateTimeOffset.UtcNow;
            job.LastCheckedAtUtc = job.FinishedAtUtc.Value;
            job.UpdatedAtUtc = job.FinishedAtUtc.Value;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        try
        {
            var result = await ExecuteCommandAsync(context.CommandPreview, cancellationToken);
            var finishedAt = DateTimeOffset.UtcNow;
            var outputExists = File.Exists(context.OutputPath);
            var probe = outputExists ? ProbeMediaFile(context.OutputPath) : new CompatibilityProbeResult(null, null, "Output file was not created.", string.Empty, string.Empty);
            var succeeded = result.ExitCode == 0 && outputExists;
            var plexRefresh = succeeded
                ? await PlexRemediationRefresh.TryRefreshLibraryItemAsync(job.LibraryItemId, context.OutputPath, db, services.GetRequiredService<IHttpClientFactory>())
                : new PlexMetadataRefreshResult(false, false, string.Empty);
            job.Status = succeeded ? "Completed" : "Failed";
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
            job.FinishedAtUtc = finishedAt;
            job.LastCheckedAtUtc = finishedAt;
            job.UpdatedAtUtc = finishedAt;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> ExecuteCommandAsync(string commandPreview, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            ArgumentList = { "-lc", commandPreview },
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

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
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
