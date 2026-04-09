using api;
using api.Models;
using Xunit;

public sealed class TelevisionSourceCoverageTests
{
    [Fact]
    public void BuildRows_for_series_includes_sonarr_plex_and_overseerr_slots()
    {
        var item = new LibraryItem
        {
            Id = 42,
            MediaType = "Series",
            Title = "Penny Dreadful",
            TmdbId = 54671,
            TvdbId = 265766
        };

        var integrations = new[]
        {
            new IntegrationConfig { Id = 10, ServiceKey = "sonarr", InstanceName = "Shows", Enabled = true },
            new IntegrationConfig { Id = 11, ServiceKey = "plex", InstanceName = "Plex", Enabled = true },
            new IntegrationConfig { Id = 12, ServiceKey = "overseerr", InstanceName = "Requests", Enabled = true }
        };

        var links = new[]
        {
            new LibraryItemSourceLink { LibraryItemId = 42, IntegrationId = 10, ExternalId = "2", ExternalType = "series" },
            new LibraryItemSourceLink { LibraryItemId = 42, IntegrationId = 11, ExternalId = "ratingKey-99", ExternalType = "show" }
        };

        var rows = TelevisionSourceCoverage.BuildRows(item, integrations, links);

        Assert.Collection(rows,
            sonarr =>
            {
                Assert.Equal("sonarr", sonarr.ServiceKey);
                Assert.True(sonarr.HasSourceLink);
                Assert.Equal("Shows", sonarr.InstanceName);
            },
            plex =>
            {
                Assert.Equal("plex", plex.ServiceKey);
                Assert.True(plex.HasSourceLink);
                Assert.Equal("Plex", plex.InstanceName);
            },
            overseerr =>
            {
                Assert.Equal("overseerr", overseerr.ServiceKey);
                Assert.False(overseerr.HasSourceLink);
                Assert.Equal("Requests", overseerr.InstanceName);
                Assert.Contains("Missing link", overseerr.Note);
            });
    }

    [Fact]
    public void BuildRows_for_episode_omits_overseerr_and_marks_missing_plex_link()
    {
        var item = new LibraryItem
        {
            Id = 84,
            MediaType = "Episode",
            Title = "Penny Dreadful — S01E01",
            TvdbId = 900001
        };

        var integrations = new[]
        {
            new IntegrationConfig { Id = 10, ServiceKey = "sonarr", InstanceName = "Shows", Enabled = true },
            new IntegrationConfig { Id = 11, ServiceKey = "plex", InstanceName = "Plex", Enabled = true },
            new IntegrationConfig { Id = 12, ServiceKey = "overseerr", InstanceName = "Requests", Enabled = true }
        };

        var links = new[]
        {
            new LibraryItemSourceLink { LibraryItemId = 84, IntegrationId = 10, ExternalId = "2001", ExternalType = "episode" }
        };

        var rows = TelevisionSourceCoverage.BuildRows(item, integrations, links);

        Assert.Collection(rows,
            sonarr =>
            {
                Assert.Equal("sonarr", sonarr.ServiceKey);
                Assert.True(sonarr.HasSourceLink);
            },
            plex =>
            {
                Assert.Equal("plex", plex.ServiceKey);
                Assert.False(plex.HasSourceLink);
                Assert.Contains("Missing link", plex.Note);
            });
    }
}
