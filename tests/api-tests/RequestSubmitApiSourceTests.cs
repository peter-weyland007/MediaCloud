using Xunit;

public sealed class RequestSubmitApiSourceTests
{
    [Fact]
    public void Request_submit_endpoint_and_contract_exist()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var programPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var content = File.ReadAllText(programPath);

        Assert.Contains("app.MapPost(\"/api/requests\"", content);
        Assert.Contains("public record CreateMediaRequestRequest(", content);
        Assert.Contains("string MediaType", content);
        Assert.Contains("int TmdbId", content);
        Assert.Contains("public record CreateMediaRequestResponse(", content);
        Assert.Contains("bool Success", content);
        Assert.Contains("string Message", content);
        Assert.Contains("bool CreatedOverseerrRequest", content);
        Assert.Contains("bool RefreshedOverseerr", content);
        Assert.Contains("bool RefreshedDownstream", content);
    }

    [Fact]
    public void Request_submit_supports_movie_and_television_overseerr_creation_and_refresh()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var programPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var content = File.ReadAllText(programPath);

        Assert.Contains("EnsureMovieRequestedInOverseerrAsync", content);
        Assert.Contains("EnsureTelevisionRequestedInOverseerrAsync", content);
        Assert.Contains("normalizedMediaType == \"movie\"", content);
        Assert.Contains("normalizedMediaType == \"tv\"", content);
        Assert.Contains("await GetRequestSubmissionStateAsync(normalizedMediaType, request.TmdbId, db, httpClientFactory)", content);
        Assert.Contains("!requestState.CanRequest && !requestState.CanReconcileRequest", content);
        Assert.Contains("requestState.InOverseerrMedia", content);
        Assert.Contains("Already present in Overseerr media with no request record to create.", content);
        Assert.Contains("No seasons available to request", content);
        Assert.Contains("IsSuccessfulOverseerrRequestResponse", content);
        Assert.Contains("Created Overseerr request to reconcile downstream state.", content);
        Assert.Contains("await SyncSingleIntegrationNowAsync(overseerr", content);
        Assert.Contains("await SyncSingleIntegrationNowAsync(downstreamIntegration", content);
    }
}
