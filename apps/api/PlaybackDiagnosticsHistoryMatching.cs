using System.Text.RegularExpressions;

public static class PlaybackDiagnosticsHistoryMatching
{
    private static readonly Regex EpisodeCodeRegex = new(@"\bS(?<season>\d{1,2})E(?<episode>\d{1,3})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SeasonTitleRegex = new(@"\bSeason\s+(?<season>\d{1,2})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<TautulliHistoryItem> SelectBestMatches(IEnumerable<TautulliHistoryItem> candidates, string expectedTitle, int? expectedYear, int maxItems)
        => (candidates ?? [])
            .Where(x => IsLikelyMatch(x, expectedTitle, expectedYear))
            .OrderByDescending(x => x.OccurredAtUtc)
            .Take(Math.Max(1, maxItems))
            .ToList();

    public static bool IsLikelyMatch(TautulliHistoryItem candidate, string expectedTitle, int? expectedYear)
    {
        var candidateTitle = NormalizeTitle(candidate.DisplayTitle);
        var expected = NormalizeTitle(expectedTitle);
        if (string.IsNullOrWhiteSpace(candidateTitle) || string.IsNullOrWhiteSpace(expected))
        {
            return false;
        }

        if (candidateTitle == expected)
        {
            return true;
        }

        if (candidateTitle.Contains(expected, StringComparison.Ordinal)
            || expected.Contains(candidateTitle, StringComparison.Ordinal))
        {
            return true;
        }

        var episode = TryParseEpisodeTitle(expectedTitle);
        if (episode is not null)
        {
            var candidateSeriesTitle = NormalizeTitle(candidate.GrandparentTitle);
            var candidateEpisodeTitle = NormalizeTitle(CandidateEpisodeTitle(candidate));
            if (candidateSeriesTitle != episode.SeriesTitle || candidateEpisodeTitle != episode.EpisodeTitle)
            {
                return false;
            }

            var candidateSeason = candidate.ParentMediaIndex ?? ParseSeasonNumber(candidate.ParentTitle);
            if (candidateSeason.HasValue && candidateSeason.Value != episode.SeasonNumber)
            {
                return false;
            }

            if (candidate.MediaIndex.HasValue && candidate.MediaIndex.Value != episode.EpisodeNumber)
            {
                return false;
            }

            if (!candidateSeason.HasValue && !candidate.MediaIndex.HasValue)
            {
                var originallyAvailableYear = ParseOriginallyAvailableYear(candidate.OriginallyAvailableAt);
                if (!expectedYear.HasValue || !originallyAvailableYear.HasValue || originallyAvailableYear.Value != expectedYear.Value)
                {
                    return false;
                }
            }

            return candidateTitle.Contains(episode.SeriesTitle, StringComparison.Ordinal)
                && candidateTitle.Contains(episode.EpisodeTitle, StringComparison.Ordinal);
        }

        return false;
    }

    public static bool IsLikelyMatch(string candidateTitle, string expectedTitle, int? expectedYear)
        => IsLikelyMatch(new TautulliHistoryItem(string.Empty, string.Empty, DateTimeOffset.MinValue, null, null, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, candidateTitle, string.Empty, string.Empty, null, null, string.Empty), expectedTitle, expectedYear);

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

    private static string CandidateEpisodeTitle(TautulliHistoryItem candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate.GrandparentTitle))
        {
            var grandparent = candidate.GrandparentTitle.Trim();
            var display = candidate.DisplayTitle.Trim();
            var dashedPrefix = grandparent + " - ";
            if (display.StartsWith(dashedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return display[dashedPrefix.Length..];
            }
        }

        return candidate.DisplayTitle;
    }

    private static EpisodeTitleParts? TryParseEpisodeTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var separators = new[] { " — ", " - " };
        foreach (var separator in separators)
        {
            var parts = value.Split(separator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                continue;
            }

            var codeMatch = EpisodeCodeRegex.Match(parts[1]);
            if (!codeMatch.Success)
            {
                continue;
            }

            var seriesTitle = NormalizeTitle(parts[0]);
            var episodeTitle = NormalizeTitle(parts[^1]);
            if (string.IsNullOrWhiteSpace(seriesTitle) || string.IsNullOrWhiteSpace(episodeTitle))
            {
                return null;
            }

            var season = int.Parse(codeMatch.Groups["season"].Value);
            var episode = int.Parse(codeMatch.Groups["episode"].Value);
            return new EpisodeTitleParts(seriesTitle, episodeTitle, season, episode);
        }

        return null;
    }

    private static int? ParseSeasonNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = SeasonTitleRegex.Match(value);
        if (!match.Success)
        {
            return null;
        }

        return int.TryParse(match.Groups["season"].Value, out var season) ? season : null;
    }

    private static int? ParseOriginallyAvailableYear(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, out var parsedDate))
        {
            return parsedDate.Year;
        }

        return value.Length >= 4 && int.TryParse(value[..4], out var parsedYear) ? parsedYear : null;
    }

    private sealed record EpisodeTitleParts(string SeriesTitle, string EpisodeTitle, int SeasonNumber, int EpisodeNumber);
}
