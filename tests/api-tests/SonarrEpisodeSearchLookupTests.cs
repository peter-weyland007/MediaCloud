using System.Text.Json;
using api;
using api.Models;
using Xunit;

public sealed class SonarrEpisodeSearchLookupTests
{
    [Fact]
    public void SelectEpisodeId_prefers_tvdb_id_match_when_present()
    {
        var item = new LibraryItem
        {
            MediaType = "Episode",
            Title = "Severance — S02E03 — Who Is Alive?",
            TvdbId = 9003
        };

        using var document = JsonDocument.Parse("""
        [
          { "id": 301, "tvdbId": 9002, "seasonNumber": 2, "episodeNumber": 2 },
          { "id": 302, "tvdbId": 9003, "seasonNumber": 2, "episodeNumber": 3 }
        ]
        """);

        var episodeId = SonarrEpisodeSearchLookup.SelectEpisodeId(item, document.RootElement.EnumerateArray());

        Assert.Equal(302, episodeId);
    }

    [Fact]
    public void SelectEpisodeId_falls_back_to_season_and_episode_from_title()
    {
        var item = new LibraryItem
        {
            MediaType = "Episode",
            Title = "Severance — S02E03 — Who Is Alive?"
        };

        using var document = JsonDocument.Parse("""
        [
          { "id": 301, "seasonNumber": 2, "episodeNumber": 2 },
          { "id": 302, "seasonNumber": 2, "episodeNumber": 3 }
        ]
        """);

        var episodeId = SonarrEpisodeSearchLookup.SelectEpisodeId(item, document.RootElement.EnumerateArray());

        Assert.Equal(302, episodeId);
    }
}
