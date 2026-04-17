using api;
using Xunit;

public sealed class TautulliApiRequestBuilderTests
{
    [Fact]
    public void BuildGetRequest_sets_browser_like_user_agent_for_cloudflare_fronted_tautulli()
    {
        var request = TautulliApiRequestBuilder.BuildGetRequest(
            "https://plexdash.lv426.cloud",
            "secret-key",
            "get_history",
            new Dictionary<string, string?> { ["media_type"] = "movie" });

        Assert.Equal("GET", request.Method.Method);
        Assert.Contains("Mozilla/5.0", request.Headers.UserAgent.ToString());
        Assert.Contains("cmd=get_history", request.RequestUri!.Query);
    }
}
