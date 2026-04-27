using Xunit;

public sealed class MovieDetailsRemediationPanelSourceTests
{
    [Fact]
    public void Movie_details_page_surfaces_file_remediation_tab_with_a_simplified_answer_first_flow()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/MovieDetails.razor"));
        var content = File.ReadAllText(pagePath);

        Assert.DoesNotContain("<MudTabPanel Text=\"Remediation Panel\">", content);
        Assert.DoesNotContain("@RenderLegacyRemediationContent()", content);
        Assert.Contains("<MudTabPanel Text=\"File Remediation\">", content);
        Assert.DoesNotContain("<MudTabPanel Text=\"Remediation v2\">", content);
        Assert.Contains("@RenderRemediationV2SimpleContent()", content);
        Assert.Contains("protected override Task OnParametersSetAsync()", content);
        Assert.Contains("=> LoadItemAsync();", content);
        var remediationV2Slice = content[(content.IndexOf("private RenderFragment RenderRemediationV2SimpleContent()", StringComparison.Ordinal))..Math.Min(content.Length, content.IndexOf("private async Task LoadItemAsync", StringComparison.Ordinal))];

        Assert.Contains("REMEDIATION STATUS", content);
        Assert.Contains("PROFILE COMPARISON", content);
        Assert.Contains("REMEDIATION STEPS", content);
        Assert.Contains("WHY", content);
        Assert.Contains("RECENT REMEDIATION HISTORY", content);
        Assert.Contains("Last refreshed:", remediationV2Slice);
        Assert.Contains("_remediationHistoryRefreshedAtUtc.HasValue", remediationV2Slice);
        Assert.Contains("FormatIssueTimestamp(_remediationHistoryRefreshedAtUtc.Value)", remediationV2Slice);
        Assert.Contains("Refresh status", remediationV2Slice);
        Assert.Contains("Refreshing...", remediationV2Slice);
        Assert.Contains("RefreshRemediationHistoryAsync", remediationV2Slice);
        Assert.Contains("class=\"movie-remediation-status-row\"", remediationV2Slice);
        Assert.Contains("<MudTooltip Arrow=\"true\" Placement=\"Placement.Top\" Text=\"@GetRemediationHistoryStatusDefinition(job)\">", remediationV2Slice);
        Assert.Contains("<MudTooltip Arrow=\"true\" Placement=\"Placement.Top\" Text=\"@GetRemediationHistoryIntegrationStepDefinition(job)\">", remediationV2Slice);
        Assert.Contains("<MudTooltip Arrow=\"true\" Placement=\"Placement.Top\" Text=\"@GetRemediationHistoryVerificationDefinition(job)\">", remediationV2Slice);
        Assert.Contains("<MudTooltip Arrow=\"true\" Placement=\"Placement.Top\" Text=\"@GetRemediationHistoryProviderStateDefinition(job)\">", remediationV2Slice);
        Assert.Contains("<MudTooltip Arrow=\"true\" Placement=\"Placement.Top\" Text=\"@GetRemediationHistoryDownloadTypeDefinition(job)\">", remediationV2Slice);
        Assert.Contains("GetRemediationHistoryPillDefinition(", content);
        Assert.Contains("movie-remediation-status-pill @GetRemediationHistoryStatusPillClass(job.Status)", remediationV2Slice);
        Assert.Contains("GetRemediationHistoryStatusPillClass(job.Status)", remediationV2Slice);
        Assert.Contains("GetRemediationHistoryStatusPillClass(job.SearchStatus)", remediationV2Slice);
        Assert.Contains("GetRemediationHistoryStatusPillClass(job.VerificationStatus)", remediationV2Slice);
        Assert.Contains("GetRemediationHistoryStatusPillClass(job.ProviderCommandStatus)", remediationV2Slice);
        Assert.Contains("Integration step:", remediationV2Slice);
        Assert.Contains("Verification:", remediationV2Slice);
        Assert.Contains("Provider state:", remediationV2Slice);
        Assert.Contains("scrollIntoView({ behavior: 'smooth', block: 'start' });", content);
        Assert.Contains("window.scrollBy({ top: -96, behavior: 'auto' });", content);
        Assert.Contains("setTimeout(() => card.classList.remove('movie-remediation-card--focus'), 2400);", content);
        Assert.DoesNotContain(".movie-remediation-card--focus {\n    .movie-remediation-card--focus {", content);
        Assert.Contains("grid-template-columns:1fr 1fr 2fr; gap:0.75rem; align-items:stretch;", remediationV2Slice);
        Assert.DoesNotContain("grid-template-columns:repeat(4, minmax(0, 1fr));", remediationV2Slice);
        Assert.True(remediationV2Slice.IndexOf("REMEDIATION STEPS", StringComparison.Ordinal) < remediationV2Slice.IndexOf("PROFILE COMPARISON", StringComparison.Ordinal));
        Assert.True(remediationV2Slice.IndexOf("PROFILE COMPARISON", StringComparison.Ordinal) < remediationV2Slice.IndexOf("REMEDIATION STATUS", StringComparison.Ordinal));
        Assert.Contains("id=\"remediation-v2-profile-comparison\" style=\"border:1px solid rgba(51,65,85,0.65); border-radius:0.75rem; padding:0.8rem; background:rgba(2,6,23,0.35); display:grid; gap:0.1rem; align-content:start; height:100%;\"", remediationV2Slice);
        Assert.Contains("border:1px solid rgba(51,65,85,0.65); border-radius:0.75rem; padding:0.8rem; background:rgba(2,6,23,0.35); display:grid; gap:0.55rem; align-content:start; height:100%;", remediationV2Slice);
        Assert.DoesNotContain(">Open full history<", remediationV2Slice);
        Assert.Contains(">Item<", remediationV2Slice);
        Assert.Contains(">Actual<", remediationV2Slice);
        Assert.Contains(">Preferred<", remediationV2Slice);
        Assert.Contains("display:grid; gap:0.1rem; align-content:start;", remediationV2Slice);
        Assert.Contains("table-layout:fixed; align-self:start;", remediationV2Slice);
        Assert.Contains("padding:0.35rem 0.6rem 0.35rem 0;", remediationV2Slice);
        Assert.Contains("var comparisonRows = GetVisibleRemediationV2ProfileComparisonRows();", remediationV2Slice);
        Assert.Contains("index < comparisonRows.Count - 1 ? \"border-bottom:1px solid rgba(30,41,59,0.6);\" : string.Empty", remediationV2Slice);
        Assert.Contains("FormatRemediationV2ComparisonValue(row.Label, row.InspectedValue)", remediationV2Slice);
        Assert.Contains("FormatRemediationV2ComparisonValue(row.Label, row.SelectedProfileValue)", remediationV2Slice);
        Assert.Contains("var actionSteps = GetRemediationV2ActionSteps();", remediationV2Slice);
        Assert.Contains("index < actionSteps.Count - 1 ? \"border-bottom:1px solid rgba(30,41,59,0.6);\" : string.Empty", remediationV2Slice);
        Assert.Contains("<table", remediationV2Slice);
        Assert.Contains("<th scope=\"col\"", remediationV2Slice);
        Assert.Contains(">Step<", remediationV2Slice);
        Assert.Contains(">Action<", remediationV2Slice);
        Assert.Contains("text-decoration:@(step.Completed ? \"line-through\" : \"none\")", remediationV2Slice);
        Assert.Contains("Analyze this file", content);
        Assert.Contains("Run scan", remediationV2Slice);
        Assert.Contains("OnClick=\"ScanDetectMetricsAsync\"", remediationV2Slice);
        Assert.Contains("Queue a compatibility fix", content);
        Assert.Contains("Review the profile comparison", content);
        Assert.Contains("Choose which action to take", content);
        Assert.Contains("Request better file", remediationV2Slice);
        Assert.Contains("RequestBetterFileAsync", remediationV2Slice);
        Assert.Contains("await LoadCompatibilityJobsAsync();", content);
        Assert.Contains("Approved from Remediation v2 because file is outside the preferred playback profile.", content);
        Assert.Contains("Leave as-is", remediationV2Slice);
        Assert.Contains("Convert anyway", remediationV2Slice);
        Assert.Contains("View comparison", remediationV2Slice);
        Assert.Contains("OnClick=\"FocusProfileComparisonAsync\"", remediationV2Slice);
        Assert.Contains("else if (string.Equals(step.Label, \"Review the profile comparison\", StringComparison.Ordinal))", remediationV2Slice);
        Assert.DoesNotContain("else if (step.IsCurrent && string.Equals(step.Label, \"Review the profile comparison\", StringComparison.Ordinal))", remediationV2Slice);
        Assert.Contains("Show more...", remediationV2Slice);
        Assert.Contains("Show less", remediationV2Slice);
        Assert.Contains("ToggleRemediationV2ProfileComparisonExpand", remediationV2Slice);
        Assert.Contains("GetRemediationV2BestActionLabel()", content);
        Assert.Contains("GetRemediationV2CurrentFileRiskLabel()", content);
        Assert.Contains("GetRemediationV2ConversionRiskLabel()", content);
        Assert.Contains("GetRemediationV2WhySummary()", content);
        Assert.Contains("BEST ACTION", content);
        Assert.Contains("CURRENT FILE RISK", content);
        Assert.Contains("CONVERSION RISK", content);
        Assert.DoesNotContain("grid-template-columns:repeat(auto-fit, minmax(180px, 1fr));", remediationV2Slice);
        Assert.DoesNotContain("border:1px solid rgba(59,130,246,0.35); border-radius:0.9rem; padding:1rem; background:rgba(15,23,42,0.72); display:grid; gap:0.85rem;", content);
        Assert.Contains("This would need a full video conversion", content);
        Assert.Contains("This looks like a lower-risk compatibility fix", content);
        Assert.Contains("\"mov,mp4,m4a,3gp,3g2,mj2\" => \"MP4\"", content);
        Assert.DoesNotContain("VIEW DETAILS", content[(content.IndexOf("<MudTabPanel Text=\"File Remediation\">", StringComparison.Ordinal))..Math.Min(content.Length, content.IndexOf("<MudTabPanel Text=\"Media File Issues\">", StringComparison.Ordinal))]);
        Assert.DoesNotContain("remediation-v2-details", content);
        Assert.DoesNotContain("return _compatibilityRecommendation.WhySummary;", content);
        Assert.DoesNotContain("WHY MEDIACLOUD CHOSE THIS", content[(content.IndexOf("<MudTabPanel Text=\"File Remediation\">", StringComparison.Ordinal))..Math.Min(content.Length, content.IndexOf("<MudTabPanel Text=\"Media File Issues\">", StringComparison.Ordinal))]);
        Assert.DoesNotContain("WHAT MEDIACLOUD USED", content[(content.IndexOf("<MudTabPanel Text=\"File Remediation\">", StringComparison.Ordinal))..Math.Min(content.Length, content.IndexOf("<MudTabPanel Text=\"Media File Issues\">", StringComparison.Ordinal))]);
    }

    [Fact]
    public void Movie_details_remediation_tab_is_not_wrapped_in_a_collapsible_panel()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/MovieDetails.razor"));
        var content = File.ReadAllText(pagePath);

        Assert.DoesNotContain("private bool _remediationExpanded;", content);
        Assert.DoesNotContain("private void ToggleRemediationPanel()", content);
    }

    [Fact]
    public void Movie_details_remediation_walkthrough_surfaces_recommendation_evidence_and_queue_actions()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/MovieDetails.razor"));
        var content = File.ReadAllText(pagePath);
        var remediationV2Slice = content[(content.IndexOf("private RenderFragment RenderRemediationV2SimpleContent()", StringComparison.Ordinal))..Math.Min(content.Length, content.IndexOf("private async Task LoadItemAsync", StringComparison.Ordinal))];

        Assert.Contains("Scan detect metrics", content);
        Assert.Contains("ScanDetectMetricsAsync", content);
        Assert.Contains("GetRecommendedActionSummary()", content);
        Assert.Contains("GetCompatibilityMismatchRows()", content);
        Assert.DoesNotContain("GetCompatibilityChangeSummaries()", content);
        Assert.DoesNotContain("WHAT NEEDS TO CHANGE", content);
        Assert.Contains("GetRecommendedCommandPreview()", content);
        Assert.DoesNotContain("Add ffmpeg command to queue", content);
        Assert.Contains("RECENT REMEDIATION HISTORY", content);
        Assert.DoesNotContain("View remediation history", content);
        Assert.DoesNotContain("<strong>Verification:</strong>", content);
        Assert.DoesNotContain("<strong>Next check:</strong>", content);
        Assert.DoesNotContain("<strong>Integration step:</strong>", content);
        Assert.Contains("RefreshRemediationHistoryAsync", content);
        Assert.Contains("Refresh status", content);
        Assert.Contains("Refreshing...", content);
        Assert.Contains("_remediationHistoryRefreshedAtUtc = DateTimeOffset.Now;", content);
        Assert.Contains("Provider state:", remediationV2Slice);
        Assert.Contains("Download type:", remediationV2Slice);
        Assert.Contains("GetRemediationHistoryDownloadTypeLabel(job)", remediationV2Slice);
        Assert.Contains("GetRemediationHistoryDownloadTypePillClass(job)", remediationV2Slice);
        Assert.Contains("IsRemediationHistoryDownloadPhase(job.SearchStatus)", content);
        Assert.Contains("Integration step:", remediationV2Slice);
        Assert.Contains("Verification:", remediationV2Slice);
        Assert.DoesNotContain("class=\"movie-remediation-why-compare\"", remediationV2Slice);
        Assert.DoesNotContain("movie-compatibility-table", remediationV2Slice);
        Assert.DoesNotContain("TARGET VS FILE", remediationV2Slice);
        Assert.DoesNotContain("_compatibilityRecommendation?.Reasons", remediationV2Slice);
    }

    [Fact]
    public void Movie_details_remediation_tab_uses_an_inner_scroll_shell()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/MovieDetails.razor"));
        var content = File.ReadAllText(pagePath);

        Assert.Contains("class=\"movie-remediation-scroll-shell\"", content);
        Assert.Contains(".movie-remediation-scroll-shell {", content);
        Assert.Contains("overflow-y: auto;", content);
        Assert.Contains("max-height: calc(100vh - 13rem);", content);
    }

    [Fact]
    public void Movie_details_media_file_issues_tab_uses_matching_scroll_and_fill_shells()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/MovieDetails.razor"));
        var content = File.ReadAllText(pagePath);

        Assert.Contains("class=\"movie-issues-scroll-shell\"", content);
        Assert.Contains("class=\"movie-issues-panel-fill\"", content);
        Assert.Contains(".movie-issues-scroll-shell {", content);
        Assert.Contains(".movie-issues-panel-fill {", content);
        Assert.Contains("overflow-y: auto;", content);
        Assert.Contains("max-height: calc(100vh - 13rem);", content);
        Assert.Contains("flex: 1 1 auto;", content);
        Assert.Contains("min-height: 0;", content);
    }
}
