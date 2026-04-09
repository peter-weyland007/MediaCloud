using System.Text.RegularExpressions;

namespace web.Components.Pages;

public static partial class TvEpisodeGrouping
{
    public static IReadOnlyList<SeasonGroup> BuildSeasonGroups(IEnumerable<TvEpisodeListItem> episodes, bool excludeSpecials = false)
    {
        var groups = episodes
            .Select(x => new { Episode = x, SeasonNumber = ExtractSeasonNumber(x) })
            .Where(x => !excludeSpecials || x.SeasonNumber != 0)
            .GroupBy(x => x.SeasonNumber)
            .OrderBy(x => x.Key)
            .Select(group => new SeasonGroup(
                group.Key,
                GetSeasonLabel(group.Key),
                group.Select(x => x.Episode).OrderBy(x => x.SortTitle, StringComparer.OrdinalIgnoreCase).ToList()))
            .ToList();

        return groups;
    }

    public static int ExtractSeasonNumber(TvEpisodeListItem episode)
    {
        if (episode is null)
        {
            return -1;
        }

        var candidate = string.IsNullOrWhiteSpace(episode.SortTitle) ? episode.DisplayTitle : episode.SortTitle;
        var match = SeasonEpisodeRegex().Match(candidate ?? string.Empty);
        if (!match.Success)
        {
            return -1;
        }

        return int.TryParse(match.Groups[1].Value, out var seasonNumber) ? seasonNumber : -1;
    }

    public static IReadOnlyList<int> CreateExpandedSeasonNumbers(IEnumerable<SeasonGroup> groups, bool expandAll)
        => expandAll
            ? groups.Select(x => x.SeasonNumber).Distinct().OrderBy(x => x).ToList()
            : [];

    public static string GetSeasonLabel(int seasonNumber)
        => seasonNumber switch
        {
            0 => "Specials",
            < 0 => "Other Episodes",
            _ => $"Season {seasonNumber}"
        };

    public sealed record TvEpisodeListItem(
        long Id,
        string MediaType,
        string DisplayTitle,
        string SortTitle,
        bool IsAvailable,
        DateTimeOffset? SourceUpdatedAtUtc,
        double? RuntimeMinutes,
        double? ActualRuntimeMinutes,
        string QualityProfile,
        DateTimeOffset UpdatedAtUtc);

    public sealed record SeasonGroup(int SeasonNumber, string Label, IReadOnlyList<TvEpisodeListItem> Episodes)
    {
        public int TotalEpisodeCount => Episodes.Count;
        public int AvailableEpisodeCount => Episodes.Count(x => x.IsAvailable);
        public double AvailabilityPercent => TotalEpisodeCount == 0 ? 0 : (AvailableEpisodeCount * 100d) / TotalEpisodeCount;
    }

    [GeneratedRegex(@"s(\d{1,2})e\d{1,3}", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonEpisodeRegex();
}
