using Xunit;

public sealed class RemediationTransactionsApiSourceTests
{
    [Fact]
    public void Remediation_transactions_api_returns_paged_payload_expected_by_the_blazor_page()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var programPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/RemediationTransactions.razor"));

        var programContent = File.ReadAllText(programPath);
        var pageContent = File.ReadAllText(pagePath);

        Assert.Contains("app.MapGet(\"/api/remediation-jobs\"", programContent);
        Assert.Contains("string? q, string? mediaType, string? service, string? status, string? verificationStatus, string? requestedBy, string? sortBy, string? sortDir, int? pageIndex, int? pageSize", programContent);
        Assert.Contains("RequireAuthorization(\"AdminOnly\")", programContent);
        Assert.Contains("new LibraryRemediationTransactionPageResponse(rows, totalCount, normalizedPageIndex, normalizedPageSize, availableServices, availableStatuses, availableVerificationStatuses, availableRequestedBy)", programContent);
        Assert.Contains("public record LibraryRemediationTransactionPageResponse", programContent);
        Assert.Contains("public record LibraryRemediationTransactionDto", programContent);

        Assert.Contains("private record LibraryRemediationTransactionPageDto(IReadOnlyList<LibraryRemediationTransactionDto> Items, int TotalCount, int PageIndex, int PageSize, IReadOnlyList<string> AvailableServices, IReadOnlyList<string> AvailableStatuses, IReadOnlyList<string> AvailableVerificationStatuses, IReadOnlyList<string> AvailableRequestedBy);", pageContent);
        Assert.Contains("private record LibraryRemediationTransactionDto(", pageContent);
    }
}
