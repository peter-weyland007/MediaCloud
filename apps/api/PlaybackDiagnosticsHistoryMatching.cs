public static class PlaybackDiagnosticsHistoryMatching
{
    public static bool IsLikelyMatch(string candidateTitle, string expectedTitle, int? expectedYear)
    {
        var candidate = NormalizeTitle(candidateTitle);
        var expected = NormalizeTitle(expectedTitle);
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(expected))
        {
            return false;
        }

        if (candidate == expected)
        {
            return true;
        }

        return candidate.Contains(expected, StringComparison.Ordinal)
            || expected.Contains(candidate, StringComparison.Ordinal);
    }

    public static string NormalizeTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || c == ':')
            .ToArray();

        var normalized = new string(chars);
        while (normalized.Contains("  ", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);
        }

        return normalized.Trim();
    }
}
