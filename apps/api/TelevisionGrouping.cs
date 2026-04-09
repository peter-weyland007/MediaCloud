using api.Models;
using System.Text;

public static class TelevisionGrouping
{
    public static string BuildEpisodeScopePrefixForSeries(LibraryItem series)
    {
        if (series is null) throw new ArgumentNullException(nameof(series));

        if (series.TvdbId.HasValue && series.TvdbId.Value > 0)
        {
            return $"episode:tvdb:{series.TvdbId.Value}:";
        }

        var normalizedTitle = NormalizeTitleKey(series.Title);
        return $"episode:title:{normalizedTitle}:";
    }

    private static string NormalizeTitleKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.Length == 0 ? "unknown" : builder.ToString();
    }
}
