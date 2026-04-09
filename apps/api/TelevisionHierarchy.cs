using api.Models;

namespace api;

public static class TelevisionHierarchy
{
    public static LibraryItem? TryFindParentSeries(LibraryItem episode, IReadOnlyList<LibraryItem> candidateSeries)
    {
        if (episode is null) throw new ArgumentNullException(nameof(episode));
        if (candidateSeries is null) throw new ArgumentNullException(nameof(candidateSeries));
        if (!string.Equals(episode.MediaType, "Episode", StringComparison.OrdinalIgnoreCase)) return null;

        var normalizedCandidates = candidateSeries
            .Where(x => string.Equals(x.MediaType, "Series", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (normalizedCandidates.Count == 0) return null;

        var scope = ExtractEpisodeScope(episode.CanonicalKey);
        if (!string.IsNullOrWhiteSpace(scope))
        {
            var byScope = normalizedCandidates.FirstOrDefault(x =>
                string.Equals(BuildSeriesScope(x), scope, StringComparison.OrdinalIgnoreCase));
            if (byScope is not null)
            {
                return byScope;
            }
        }

        var titlePrefix = ExtractSeriesTitleFromEpisodeDisplayTitle(episode.Title);
        if (!string.IsNullOrWhiteSpace(titlePrefix))
        {
            return normalizedCandidates.FirstOrDefault(x =>
                string.Equals((x.Title ?? string.Empty).Trim(), titlePrefix, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private static string? ExtractEpisodeScope(string canonicalKey)
    {
        if (string.IsNullOrWhiteSpace(canonicalKey)) return null;
        const string prefix = "episode:";
        if (!canonicalKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;

        var tail = canonicalKey[prefix.Length..];
        var marker = ":s";
        var markerIndex = tail.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return markerIndex > 0 ? tail[..markerIndex] : null;
    }

    private static string BuildSeriesScope(LibraryItem series)
    {
        if (series.TvdbId.HasValue && series.TvdbId.Value > 0)
        {
            return $"tvdb:{series.TvdbId.Value}";
        }

        return $"title:{NormalizeTitleKey(series.Title)}";
    }

    private static string ExtractSeriesTitleFromEpisodeDisplayTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;
        var marker = " — S";
        var index = title.IndexOf(marker, StringComparison.Ordinal);
        return index > 0 ? title[..index].Trim() : string.Empty;
    }

    private static string NormalizeTitleKey(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;
        return new string(title.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }
}
