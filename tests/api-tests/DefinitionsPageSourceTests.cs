using Xunit;

public sealed class DefinitionsPageSourceTests
{
    [Fact]
    public void App_registers_required_mud_popover_provider_for_tooltips()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/App.razor"));
        var content = File.ReadAllText(fullPath);

        Assert.Contains("<MudPopoverProvider", content);
    }

    [Fact]
    public void Nav_menu_places_definitions_link_directly_under_dashboard()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Layout/NavMenu.razor"));
        var content = File.ReadAllText(fullPath);

        Assert.Contains("<NavLink class=\"nav-link\" href=\"definitions\">Definitions</NavLink>", content);
        Assert.True(content.IndexOf("Dashboard", StringComparison.Ordinal) < content.IndexOf("Definitions", StringComparison.Ordinal));
        Assert.True(content.IndexOf("Definitions", StringComparison.Ordinal) < content.IndexOf("Issues", StringComparison.Ordinal));
    }

    [Fact]
    public void Definitions_page_starts_fully_collapsed_and_uses_row_badges_instead_of_section_pills()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/Definitions.razor"));
        var content = File.ReadAllText(fullPath);

        Assert.Contains("private readonly HashSet<string> _expandedSections = [];", content);
        Assert.DoesNotContain("definition-chip", content);
        Assert.Contains("definition-row-badge", content);
        Assert.Contains("Recommended", content);
        Assert.Contains("Acceptable", content);
        Assert.Contains("Legacy", content);
        Assert.Contains("MudTooltip", content);
        Assert.Contains("ⓘ", content);
    }
}
