using Xunit;

public sealed class MovieDetailsSourcePanelClarityTests
{
    [Fact]
    public void Movie_details_source_panel_uses_explicit_labels_for_matched_item_and_external_id()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/MovieDetails.razor"));
        var content = File.ReadAllText(fullPath);

        Assert.Contains("Matched item", content);
        Assert.Contains("BuildMatchedItemLabel", content);
        Assert.Contains("BuildMatchedItemSummary", content);
        Assert.Contains("BuildPrimaryMatchedItemSummary", content);
    }

    [Fact]
    public void Movie_details_source_panel_formats_matched_item_with_id_in_parentheses()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/MovieDetails.razor"));
        var content = File.ReadAllText(fullPath);

        Assert.DoesNotContain("FormatSourceLinkSummary", content);
        Assert.Contains("$\"{title} ({externalId})\"", content);
    }

    [Fact]
    public void Movie_details_source_panel_uses_operator_friendly_action_and_sync_labels()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/MovieDetails.razor"));
        var content = File.ReadAllText(fullPath);

        Assert.Contains("Last synced", content);
        Assert.Contains("\"SYNC\"", content);
        Assert.DoesNotContain("Sync from {source.DisplayName}", content);
        Assert.DoesNotContain("Refresh this service", content);
    }

    [Fact]
    public void Movie_details_source_panel_uses_badge_for_match_and_mismatch_state_instead_of_monitoring_row_text()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/MovieDetails.razor"));
        var content = File.ReadAllText(fullPath);

        Assert.Contains("\"Matched\"", content);
        Assert.Contains("\"Mismatch\"", content);
        Assert.DoesNotContain("Monitoring:", content);
        Assert.DoesNotContain("Present in Overseerr", content);
        Assert.DoesNotContain("In sync — monitored in Radarr", content);
    }

    [Fact]
    public void Movie_details_source_panel_only_shows_instance_name_when_it_differs_from_service_name()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/MovieDetails.razor"));
        var content = File.ReadAllText(fullPath);

        Assert.Contains("BuildServiceHeader(source)", content);
        Assert.DoesNotContain("@source.DisplayName (@source.InstanceName)", content);
    }
}
