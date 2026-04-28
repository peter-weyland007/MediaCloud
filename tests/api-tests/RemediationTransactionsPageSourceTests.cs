using Xunit;

public sealed class RemediationTransactionsPageSourceTests
{
    [Fact]
    public void Nav_menu_exposes_admin_remediation_transactions_link()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var navPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Layout/NavMenu.razor"));
        var content = File.ReadAllText(navPath);

        Assert.Contains("href=\"remediation\"", content);
        Assert.Contains(">Remediation</NavLink>", content);
        Assert.Contains("AuthorizeView Roles=\"Admin\"", content);

        Assert.Contains("<NavLink class=\"nav-link\" href=\"issues\">Issues</NavLink>\n                <NavLink class=\"nav-link\" href=\"remediation\">Remediation</NavLink>", content);
    }

    [Fact]
    public void Remediation_transactions_page_exists_as_admin_control_plane()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/RemediationTransactions.razor"));
        var content = File.ReadAllText(pagePath);

        Assert.Contains("@page \"/remediation\"", content);
        Assert.Contains("@attribute [Authorize(Roles = \"Admin\")]", content);
        Assert.Contains("Remediation transactions", content);
        Assert.Contains("/api/remediation-jobs?", content);
        Assert.Contains("LibraryRemediationTransactionPageDto", content);
        Assert.Contains("ToggleSortAsync(\"requested\")", content);
        Assert.Contains("ToggleSortAsync(\"item\")", content);
        Assert.Contains("ToggleSortAsync(\"service\")", content);
        Assert.Contains("ToggleSortAsync(\"status\")", content);
        Assert.Contains("MediaItemRoutes.GetDetailsHref", content);
    }
}
