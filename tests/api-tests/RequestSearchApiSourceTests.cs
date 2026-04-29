using Xunit;

public sealed class RequestSearchApiSourceTests
{
    [Fact]
    public void Request_search_endpoint_and_contract_exist()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var programPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var content = File.ReadAllText(programPath);

        Assert.Contains("app.MapGet(\"/api/requests/search\"", content);
        Assert.Contains("string? q", content);
        Assert.Contains("string? mediaType", content);
        Assert.Contains("SearchRequestCandidatesAsync(", content);
        Assert.Contains("RequireAuthorization()", content);
        Assert.Contains("public record RequestSearchResultDto(", content);
        Assert.Contains("string MediaType", content);
        Assert.Contains("int TmdbId", content);
        Assert.Contains("string Title", content);
        Assert.Contains("int? Year", content);
        Assert.Contains("string PosterPath", content);
        Assert.Contains("string Overview", content);
        Assert.Contains("bool AlreadyRequested", content);
        Assert.Contains("bool AlreadyInLibrary", content);
        Assert.Contains("bool AlreadyInArr", content);
        Assert.Contains("bool CanRequest", content);
        Assert.Contains("bool InOverseerrMedia", content);
        Assert.Contains("bool CanReconcileRequest", content);
        Assert.Contains("string StatusSummary", content);
    }

    [Fact]
    public void Request_search_marks_overseerr_media_presence_separately_from_request_records()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var programPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var content = File.ReadAllText(programPath);

        Assert.Contains("var inOverseerrMedia = OverseerrMediaExists(row);", content);
        Assert.Contains("var canReconcileRequest = !alreadyRequested && alreadyInArr && !inOverseerrMedia;", content);
        Assert.Contains("Present in Overseerr media but missing a request record.", content);
    }
}
