using System.Net;
using System.Net.Http;
using api;
using api.Models;
using Xunit;

public sealed class PlexRemediationRefreshTests
{
    [Fact]
    public void BuildMetadataRefreshUrl_targets_plex_item_refresh_endpoint()
    {
        var url = PlexRemediationRefresh.BuildMetadataRefreshUrl("http://plex:32400/", "1474");

        Assert.Equal("http://plex:32400/library/metadata/1474/refresh?force=1", url);
    }

    [Fact]
    public void BuildSectionRefreshUrl_targets_plex_section_refresh_endpoint_with_folder_path()
    {
        var url = PlexRemediationRefresh.BuildSectionRefreshUrl(
            "http://plex:32400/",
            "5",
            "/Volumes/Media/Movies/28 Days Later (2002) (tt0289043)");

        Assert.Equal("http://plex:32400/library/sections/5/refresh?path=%2FVolumes%2FMedia%2FMovies%2F28%20Days%20Later%20%282002%29%20%28tt0289043%29", url);
    }

    [Fact]
    public async Task RefreshMetadataAsync_sends_item_refresh_then_folder_scan_and_reports_success()
    {
        var integration = new IntegrationConfig
        {
            ServiceKey = "plex",
            BaseUrl = "http://plex:32400",
            AuthType = "ApiKey",
            ApiKey = "secret"
        };
        var item = new LibraryItem
        {
            Id = 1474,
            Title = "28 Days Later",
            PlexRatingKey = "1474"
        };
        using var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.ToString() == "http://plex:32400/library/metadata/1474")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<MediaContainer><Video ratingKey=\"1474\" librarySectionID=\"5\" /></MediaContainer>")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = new HttpClient(handler);

        var result = await PlexRemediationRefresh.RefreshMetadataAsync(
            item,
            integration,
            client,
            "/Volumes/Media/Movies/28 Days Later (2002) (tt0289043)/28 Days Later (2002) - (tt0289043) [AAC | x265].mkv.compat.mp4");

        Assert.True(result.Attempted);
        Assert.True(result.Success);
        Assert.Equal("Requested Plex metadata refresh for this movie. Requested Plex library scan for the remediated file folder.", result.Message);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(HttpMethod.Put, handler.Requests[0].Method);
        Assert.Equal("http://plex:32400/library/metadata/1474/refresh?force=1", handler.Requests[0].RequestUri!.ToString());
        Assert.Equal(HttpMethod.Get, handler.Requests[1].Method);
        Assert.Equal("http://plex:32400/library/metadata/1474", handler.Requests[1].RequestUri!.ToString());
        Assert.Equal(HttpMethod.Get, handler.Requests[2].Method);
        Assert.Equal("http://plex:32400/library/sections/5/refresh", handler.Requests[2].RequestUri!.GetLeftPart(UriPartial.Path));
        Assert.Contains(Uri.EscapeDataString("/Volumes/Media/Movies/28 Days Later (2002) (tt0289043)"), handler.Requests[2].RequestUri!.Query);
        Assert.All(handler.Requests, request => Assert.Equal("secret", request.Headers.GetValues("X-Plex-Token").Single()));
    }

    [Fact]
    public async Task RefreshMetadataAsync_reports_http_failure_without_throwing()
    {
        var integration = new IntegrationConfig
        {
            ServiceKey = "plex",
            BaseUrl = "http://plex:32400",
            AuthType = "ApiKey",
            ApiKey = "secret"
        };
        var item = new LibraryItem
        {
            Id = 1474,
            Title = "28 Days Later",
            PlexRatingKey = "1474"
        };
        using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("gateway down")
        });
        using var client = new HttpClient(handler);

        var result = await PlexRemediationRefresh.RefreshMetadataAsync(item, integration, client, "/Volumes/Media/Movies/28 Days Later (2002) (tt0289043)/28 Days Later.compat.mp4");

        Assert.True(result.Attempted);
        Assert.False(result.Success);
        Assert.Equal("Plex refresh failed: gateway down", result.Message);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder = responder;
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(CloneRequest(request));
            return Task.FromResult(_responder(request));
        }

        private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return clone;
        }
    }
}
