using System.Text.Json;
using api;
using Xunit;

public sealed class OverseerrTelevisionMetadataResolverTests
{
    [Fact]
    public void Resolve_uses_detail_payload_when_request_list_lacks_series_title()
    {
        using var requestDoc = JsonDocument.Parse("""
        {
          "id": 446,
          "type": "tv",
          "media": {
            "mediaType": "tv",
            "tmdbId": 304340,
            "tvdbId": 472538,
            "imdbId": null
          }
        }
        """);

        using var detailDoc = JsonDocument.Parse("""
        {
          "id": 304340,
          "name": "The Religion Business",
          "firstAirDate": "2025-09-13",
          "overview": "A seven-part documentary series.",
          "externalIds": {
            "imdbId": "tt21070076",
            "tvdbId": 472538
          }
        }
        """);

        var metadata = OverseerrTelevisionMetadataResolver.Resolve(requestDoc.RootElement, detailDoc.RootElement);

        Assert.Equal("The Religion Business", metadata.Title);
        Assert.Equal("The Religion Business", metadata.SortTitle);
        Assert.Equal(2025, metadata.Year);
        Assert.Equal(304340, metadata.TmdbId);
        Assert.Equal(472538, metadata.TvdbId);
        Assert.Equal("tt21070076", metadata.ImdbId);
        Assert.Equal("A seven-part documentary series.", metadata.Overview);
    }

    [Fact]
    public void NeedsDetailLookup_returns_true_when_request_payload_would_create_unknown_series()
    {
        var metadata = new OverseerrTelevisionMetadata("Unknown", "Unknown", null, 304340, 472538, string.Empty, string.Empty);

        Assert.True(OverseerrTelevisionMetadataResolver.NeedsDetailLookup(metadata));
    }
}
