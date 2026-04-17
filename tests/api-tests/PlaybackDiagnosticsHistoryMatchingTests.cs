using api;
using Xunit;

public sealed class PlaybackDiagnosticsHistoryMatchingTests
{
    [Fact]
    public void SelectBestMatches_finds_movie_rows_from_broad_history_without_search_filter()
    {
        var rows = new[]
        {
            new TautulliHistoryItem("1", "999", new DateTimeOffset(2026, 4, 7, 1, 0, 0, TimeSpan.Zero), null, null, "Darkmatter5", "TV 2025", "TV 2025", "Plex for Samsung", "Samsung", "direct play", string.Empty, "Dunkirk", string.Empty, string.Empty, null, null, string.Empty),
            new TautulliHistoryItem("2", "18033", new DateTimeOffset(2026, 4, 8, 1, 0, 0, TimeSpan.Zero), null, null, "Darkmatter5", "TV 2025", "TV 2025", "Plex for Samsung", "Samsung", "direct play", string.Empty, "Fantastic Beasts and Where to Find Them", string.Empty, string.Empty, null, null, string.Empty),
            new TautulliHistoryItem("3", "18033", new DateTimeOffset(2026, 4, 9, 1, 0, 0, TimeSpan.Zero), null, null, "Darkmatter5", "XBOX", "XBOX", "Plex for Xbox", "Xbox", "transcode", string.Empty, "Fantastic Beasts and Where to Find Them", string.Empty, string.Empty, null, null, string.Empty)
        };

        var matches = PlaybackDiagnosticsHistoryMatching.SelectBestMatches(rows, "Fantastic Beasts and Where to Find Them", 2016, 2);

        Assert.Equal(2, matches.Count);
        Assert.All(matches, x => Assert.Contains("Fantastic Beasts", x.DisplayTitle));
        Assert.Equal("3", matches[0].ExternalId);
    }

    [Fact]
    public void SelectBestMatches_matches_episode_rows_by_series_and_episode_title_even_when_expected_title_contains_sxxexx()
    {
        var rows = new[]
        {
            new TautulliHistoryItem("1", "24061", new DateTimeOffset(2026, 4, 8, 1, 0, 0, TimeSpan.Zero), null, null, "Darkmatter5", "TV 2025", "TV 2025", "Plex for Samsung", "Samsung", "direct play", string.Empty, "The Curse of Oak Island - A Sacred Symbol", "Season 13", "The Curse of Oak Island", 21, 13, "2026-04-08"),
            new TautulliHistoryItem("2", "24050", new DateTimeOffset(2026, 4, 7, 1, 0, 0, TimeSpan.Zero), null, null, "Darkmatter5", "TV 2025", "TV 2025", "Plex for Samsung", "Samsung", "direct play", string.Empty, "The Curse of Oak Island - The Sands of Time", "Season 13", "The Curse of Oak Island", 20, 13, "2026-04-01")
        };

        var matches = PlaybackDiagnosticsHistoryMatching.SelectBestMatches(rows, "The Curse of Oak Island — S13E21 — A Sacred Symbol", 2026, 5);

        Assert.Single(matches);
        Assert.Equal("1", matches[0].ExternalId);
    }

    [Fact]
    public void SelectBestMatches_rejects_episode_rows_when_season_number_does_not_match()
    {
        var rows = new[]
        {
            new TautulliHistoryItem("1", "24061", new DateTimeOffset(2026, 4, 8, 1, 0, 0, TimeSpan.Zero), null, null, "Darkmatter5", "TV 2025", "TV 2025", "Plex for Samsung", "Samsung", "direct play", string.Empty, "The Curse of Oak Island - A Sacred Symbol", "Season 12", "The Curse of Oak Island", 21, 12, "2026-04-08")
        };

        var matches = PlaybackDiagnosticsHistoryMatching.SelectBestMatches(rows, "The Curse of Oak Island — S13E21 — A Sacred Symbol", 2026, 5);

        Assert.Empty(matches);
    }

    [Fact]
    public void SelectBestMatches_rejects_episode_rows_when_episode_number_does_not_match()
    {
        var rows = new[]
        {
            new TautulliHistoryItem("1", "24061", new DateTimeOffset(2026, 4, 8, 1, 0, 0, TimeSpan.Zero), null, null, "Darkmatter5", "TV 2025", "TV 2025", "Plex for Samsung", "Samsung", "direct play", string.Empty, "The Curse of Oak Island - A Sacred Symbol", "Season 13", "The Curse of Oak Island", 20, 13, "2026-04-08")
        };

        var matches = PlaybackDiagnosticsHistoryMatching.SelectBestMatches(rows, "The Curse of Oak Island — S13E21 — A Sacred Symbol", 2026, 5);

        Assert.Empty(matches);
    }

    [Fact]
    public void SelectBestMatches_matches_episode_rows_without_season_or_episode_indexes_when_originally_available_at_year_matches()
    {
        var rows = new[]
        {
            new TautulliHistoryItem("1", "24061", new DateTimeOffset(2026, 4, 8, 1, 0, 0, TimeSpan.Zero), null, null, "Darkmatter5", "TV 2025", "TV 2025", "Plex for Samsung", "Samsung", "direct play", string.Empty, "The Curse of Oak Island - A Sacred Symbol", string.Empty, "The Curse of Oak Island", null, null, "2026-04-08")
        };

        var matches = PlaybackDiagnosticsHistoryMatching.SelectBestMatches(rows, "The Curse of Oak Island — S13E21 — A Sacred Symbol", 2026, 5);

        Assert.Single(matches);
        Assert.Equal("1", matches[0].ExternalId);
    }

    [Fact]
    public void SelectBestMatches_rejects_episode_rows_without_season_or_episode_indexes_when_originally_available_at_year_does_not_match()
    {
        var rows = new[]
        {
            new TautulliHistoryItem("1", "24061", new DateTimeOffset(2026, 4, 8, 1, 0, 0, TimeSpan.Zero), null, null, "Darkmatter5", "TV 2025", "TV 2025", "Plex for Samsung", "Samsung", "direct play", string.Empty, "The Curse of Oak Island - A Sacred Symbol", string.Empty, "The Curse of Oak Island", null, null, "2025-04-08")
        };

        var matches = PlaybackDiagnosticsHistoryMatching.SelectBestMatches(rows, "The Curse of Oak Island — S13E21 — A Sacred Symbol", 2026, 5);

        Assert.Empty(matches);
    }
}
