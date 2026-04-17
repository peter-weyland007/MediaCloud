using System.Globalization;
using System.Text.Json;

namespace api;

public readonly record struct LibraryIssueDetailsSortValue(int Priority, double NumericValue, string TextValue);

public static class LibraryIssueDetailsSort
{
    public static LibraryIssueDetailsSortValue Build(string? issueType, string? detailsJson)
    {
        var normalizedIssueType = (issueType ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(detailsJson))
        {
            return new LibraryIssueDetailsSortValue(0, 0, string.Empty);
        }

        try
        {
            using var doc = JsonDocument.Parse(detailsJson);
            var root = doc.RootElement;

            if (string.Equals(normalizedIssueType, "runtime_mismatch", StringComparison.Ordinal))
            {
                var diffMinutes = root.TryGetProperty("diffMinutes", out var diffMinutesElement)
                    ? diffMinutesElement.GetDouble()
                    : 0;
                var diffPercent = root.TryGetProperty("diffPercent", out var diffPercentElement)
                    ? diffPercentElement.GetDouble()
                    : 0;
                var textValue = string.Create(CultureInfo.InvariantCulture, $"{diffMinutes:0.##}m | {diffPercent:0.##}%");
                return new LibraryIssueDetailsSortValue(2, diffMinutes, textValue);
            }

            if (string.Equals(normalizedIssueType, "runtime_probe_failed", StringComparison.Ordinal))
            {
                var filePath = root.TryGetProperty("filePath", out var filePathElement)
                    ? filePathElement.GetString() ?? string.Empty
                    : string.Empty;
                var probeError = root.TryGetProperty("probeError", out var probeErrorElement)
                    ? probeErrorElement.GetString() ?? string.Empty
                    : string.Empty;
                var fileName = Path.GetFileName(filePath).Trim().ToLowerInvariant();
                var error = probeError.Trim().ToLowerInvariant();
                var textValue = string.IsNullOrWhiteSpace(error)
                    ? fileName
                    : string.IsNullOrWhiteSpace(fileName)
                        ? error
                        : $"{fileName} | {error}";
                return new LibraryIssueDetailsSortValue(1, 0, textValue);
            }
        }
        catch
        {
            // Ignore malformed details and fall back to a blank key.
        }

        return new LibraryIssueDetailsSortValue(0, 0, string.Empty);
    }
}
