using Xunit;

public sealed class RequestsNavigationSourceTests
{
    [Fact]
    public void Sidebar_groups_media_content_separately_from_operations()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var navPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Layout/NavMenu.razor"));
        var navContent = File.ReadAllText(navPath);

        Assert.Contains("<span>Media</span>", navContent);
        Assert.Contains("<span>Operations</span>", navContent);
        Assert.Contains("href=\"media/movies\"", navContent);
        Assert.Contains("href=\"media/tv-shows\"", navContent);
        Assert.Contains("href=\"media/music\"", navContent);
        Assert.Contains("href=\"media/recent\"", navContent);
        Assert.Contains("href=\"media/requests\"", navContent);
        Assert.Contains("href=\"issues\"", navContent);
        Assert.Contains("href=\"remediation\"", navContent);
        Assert.True(navContent.IndexOf("<span>Media</span>", StringComparison.Ordinal) < navContent.IndexOf("href=\"media/movies\"", StringComparison.Ordinal));
        Assert.True(navContent.IndexOf("href=\"media/music\"", StringComparison.Ordinal) < navContent.IndexOf("<span>Operations</span>", StringComparison.Ordinal));
        Assert.True(navContent.IndexOf("<span>Operations</span>", StringComparison.Ordinal) < navContent.IndexOf("href=\"media/recent\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Sidebar_persists_group_expansion_state_in_local_storage()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var navPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Layout/NavMenu.razor"));
        var navContent = File.ReadAllText(navPath);

        Assert.Contains("IJSRuntime", navContent);
        Assert.Contains("import", navContent);
        Assert.Contains("nav-menu-state.js", navContent);
        Assert.Contains("mediaExpanded", navContent);
        Assert.Contains("operationsExpanded", navContent);
        Assert.Contains("LoadMenuStateAsync", navContent);
        Assert.Contains("PersistMenuStateAsync", navContent);
        Assert.Contains("IAsyncDisposable", navContent);
    }

    [Fact]
    public void Requests_page_route_exists()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/Requests.razor"));
        var content = File.ReadAllText(pagePath);

        Assert.Contains("@page \"/media/requests\"", content);
        Assert.Contains("<PageTitle>Requests</PageTitle>", content);
        Assert.Contains("Request new movies or shows through Overseerr", content);
    }
}
