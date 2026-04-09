using System.Text.Json;

public static class RuntimeProbeFailurePolicy
{
    public static string BuildIssueDetailsJson(string filePath, string probeError, int? exitCode)
        => JsonSerializer.Serialize(new
        {
            filePath = filePath ?? string.Empty,
            probeError = probeError ?? string.Empty,
            exitCode
        });

    public static bool ShouldSkipAutomaticReprobe(string? issueStatus, string? issueDetailsJson, string? currentFilePath)
    {
        if (!string.Equals(issueStatus, "Open", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var failedPath = TryGetFailedFilePath(issueDetailsJson);
        if (string.IsNullOrWhiteSpace(failedPath) || string.IsNullOrWhiteSpace(currentFilePath))
        {
            return false;
        }

        return string.Equals(failedPath.Trim(), currentFilePath.Trim(), StringComparison.Ordinal);
    }

    public static string? TryGetFailedFilePath(string? issueDetailsJson)
    {
        if (string.IsNullOrWhiteSpace(issueDetailsJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(issueDetailsJson);
            return document.RootElement.TryGetProperty("filePath", out var filePathElement)
                ? filePathElement.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
