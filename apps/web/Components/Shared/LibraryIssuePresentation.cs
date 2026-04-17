using System.Globalization;
using System.Text;

namespace web.Components.Shared;

public static class LibraryIssuePresentation
{
    public static string GetIssueTypeLabel(string? issueType)
    {
        var value = (issueType ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unknown";
        }

        var normalized = value.Replace('-', ' ').Replace('_', ' ');
        var builder = new StringBuilder(normalized.Length);
        var previousWasSpace = false;
        foreach (var ch in normalized)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                    previousWasSpace = true;
                }

                continue;
            }

            builder.Append(char.ToLowerInvariant(ch));
            previousWasSpace = false;
        }

        var collapsed = builder.ToString().Trim();
        if (string.IsNullOrWhiteSpace(collapsed))
        {
            return "Unknown";
        }

        return string.Create(CultureInfo.InvariantCulture, $"{char.ToUpperInvariant(collapsed[0])}{collapsed[1..]}");
    }

    public static int ResolveJumpPageIndex(string? requestedPage, int totalPages)
    {
        var safeTotalPages = Math.Max(totalPages, 1);
        if (!int.TryParse(requestedPage, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pageNumber))
        {
            return 0;
        }

        return Math.Clamp(pageNumber, 1, safeTotalPages) - 1;
    }
}
