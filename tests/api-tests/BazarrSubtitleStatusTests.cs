using api.Models;
using Xunit;

public sealed class BazarrSubtitleStatusTests
{
    [Fact]
    public void ResolveTarget_uses_radarr_movie_link_for_movie_items()
    {
        var item = new LibraryItem
        {
            Id = 41,
            MediaType = "Movie",
            Title = "Alien"
        };

        var target = BazarrSubtitleStatusResolver.ResolveTarget(item, [
            new BazarrSourceLinkCandidate("plex", "movie", "123"),
            new BazarrSourceLinkCandidate("radarr", "movie", "9001")
        ]);

        Assert.NotNull(target);
        Assert.Equal("movie", target!.TargetKind);
        Assert.Equal(9001, target.ExternalId);
    }

    [Fact]
    public void BuildMovieStatus_reads_available_and_missing_subtitles_from_bazarr_payload()
    {
        var payload = """
        {
          "data": [
            {
              "radarrId": 9001,
              "monitored": true,
              "subtitles": [
                { "name": "English", "forced": false, "hi": false, "path": "/subs/alien.en.srt" },
                { "name": "Spanish", "forced": true, "hi": false, "path": "/subs/alien.es.forced.srt" }
              ],
              "missing_subtitles": [
                { "name": "French", "forced": false, "hi": true }
              ]
            }
          ]
        }
        """;

        var status = BazarrSubtitleStatusResolver.BuildMovieStatus(41, "Bazarr", "Default", 9001, payload);

        Assert.True(status.HasMatch);
        Assert.Equal("Missing", status.Status);
        Assert.True(status.Monitored);
        Assert.Contains("English", status.AvailableSubtitles);
        Assert.Contains("Spanish (forced)", status.AvailableSubtitles);
        Assert.Contains("French (HI)", status.MissingSubtitles);
        Assert.Equal("Bazarr reports 2 available subtitle tracks and 1 missing subtitle target.", status.Summary);
    }

    [Fact]
    public void BuildSeriesStatus_reports_missing_episode_count_from_bazarr_payload()
    {
        var payload = """
        {
          "data": [
            {
              "sonarrSeriesId": 501,
              "episodeFileCount": 24,
              "episodeMissingCount": 3,
              "monitored": true,
              "title": "The Expanse"
            }
          ]
        }
        """;

        var status = BazarrSubtitleStatusResolver.BuildSeriesStatus(77, "Bazarr", "Default", 501, payload);

        Assert.True(status.HasMatch);
        Assert.Equal("Missing", status.Status);
        Assert.Equal(3, status.MissingEpisodeCount);
        Assert.Equal("Bazarr reports subtitle gaps in 3 episodes out of 24 tracked episodes.", status.Summary);
    }
}
