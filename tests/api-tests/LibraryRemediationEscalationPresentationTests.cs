using web.Components.Shared;
using Xunit;

public sealed class LibraryRemediationEscalationPresentationTests
{
    [Fact]
    public void Build_returns_escalation_view_after_two_matching_retries()
    {
        var original = BuildJob(
            id: 80,
            issueType: "wrong_language",
            loopbackStatus: "Recommended",
            requestedAtUtc: new DateTimeOffset(2026, 4, 16, 0, 0, 0, TimeSpan.Zero));
        var retryOne = BuildJob(
            id: 81,
            issueType: "wrong_language",
            loopbackStatus: "NotNeeded",
            requestedAtUtc: new DateTimeOffset(2026, 4, 16, 1, 0, 0, TimeSpan.Zero));
        var retryTwo = BuildJob(
            id: 82,
            issueType: "wrong_language",
            loopbackStatus: "NotNeeded",
            requestedAtUtc: new DateTimeOffset(2026, 4, 16, 2, 0, 0, TimeSpan.Zero));

        var view = LibraryRemediationEscalationPresentation.Build(original, [original, retryOne, retryTwo]);

        Assert.True(view.ShowEscalation);
        Assert.Equal(2, view.RetryAttemptCount);
        Assert.Contains("forcing operator review", view.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(view.RecommendedSteps, step => step.Contains("language", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(view.RecommendedActions, action => action.ActionKey == "open_settings");
        Assert.Contains(view.RecommendedActions, action => action.ActionKey == "review_source_coverage");
        Assert.Contains(view.RecommendedOutcomes, outcome => outcome.OutcomeKey == "ProfileReviewed");
        Assert.Contains(view.RecommendedOutcomes, outcome => outcome.OutcomeKey == "SourceReviewed");
    }

    [Fact]
    public void Build_returns_none_when_retry_limit_has_not_been_reached()
    {
        var original = BuildJob(
            id: 90,
            issueType: "runtime_mismatch",
            loopbackStatus: "Recommended",
            requestedAtUtc: new DateTimeOffset(2026, 4, 16, 0, 0, 0, TimeSpan.Zero));
        var retryOne = BuildJob(
            id: 91,
            issueType: "runtime_mismatch",
            loopbackStatus: "NotNeeded",
            requestedAtUtc: new DateTimeOffset(2026, 4, 16, 1, 0, 0, TimeSpan.Zero));

        var view = LibraryRemediationEscalationPresentation.Build(original, [original, retryOne]);

        Assert.False(view.ShowEscalation);
        Assert.Empty(view.RecommendedSteps);
        Assert.Empty(view.RecommendedActions);
    }

    [Fact]
    public void Build_returns_none_when_loopback_is_not_recommended()
    {
        var job = BuildJob(
            id: 100,
            issueType: "wrong_language",
            loopbackStatus: "Standby",
            requestedAtUtc: new DateTimeOffset(2026, 4, 16, 0, 0, 0, TimeSpan.Zero));

        var view = LibraryRemediationEscalationPresentation.Build(job, [job]);

        Assert.False(view.ShowEscalation);
    }

    [Fact]
    public void Build_returns_handled_view_when_operator_review_was_recorded()
    {
        var original = BuildJob(
            id: 110,
            issueType: "runtime_mismatch",
            loopbackStatus: "Recommended",
            requestedAtUtc: new DateTimeOffset(2026, 4, 16, 0, 0, 0, TimeSpan.Zero),
            operatorReviewStatus: "MetadataReviewed",
            operatorReviewSummary: "Operator reviewed runtime evidence and metadata mapping.",
            operatorReviewedBy: "mark",
            operatorReviewedAtUtc: new DateTimeOffset(2026, 4, 16, 3, 0, 0, TimeSpan.Zero));
        var retryOne = BuildJob(
            id: 111,
            issueType: "runtime_mismatch",
            loopbackStatus: "NotNeeded",
            requestedAtUtc: new DateTimeOffset(2026, 4, 16, 1, 0, 0, TimeSpan.Zero));
        var retryTwo = BuildJob(
            id: 112,
            issueType: "runtime_mismatch",
            loopbackStatus: "NotNeeded",
            requestedAtUtc: new DateTimeOffset(2026, 4, 16, 2, 0, 0, TimeSpan.Zero));

        var view = LibraryRemediationEscalationPresentation.Build(original, [original, retryOne, retryTwo]);

        Assert.True(view.ShowEscalation);
        Assert.True(view.IsOperatorReviewed);
        Assert.Contains("operator review recorded", view.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mark", view.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(view.RecommendedActions);
        Assert.Empty(view.RecommendedOutcomes);
    }

    private static web.Components.Shared.LibraryRemediationJobDto BuildJob(
        long id,
        string issueType,
        string loopbackStatus,
        DateTimeOffset requestedAtUtc,
        string operatorReviewStatus = "",
        string operatorReviewSummary = "",
        string operatorReviewedBy = "",
        DateTimeOffset? operatorReviewedAtUtc = null)
        => new(
            id,
            1474,
            null,
            "radarr",
            "Radarr",
            22,
            "search_replacement",
            "MoviesSearch",
            9001,
            issueType,
            issueType,
            string.Empty,
            "test",
            "high",
            true,
            true,
            false,
            true,
            false,
            "policy",
            "notes",
            string.Empty,
            string.Empty,
            "VerificationFailed",
            "Completed",
            "Succeeded",
            "outcome",
            "message",
            314,
            "Completed",
            "Provider MoviesSearch command 314 is completed.",
            requestedAtUtc,
            "Failed",
            "verification failed",
            "{}",
            requestedAtUtc,
            loopbackStatus,
            "loopback",
            "release",
            operatorReviewStatus,
            operatorReviewSummary,
            operatorReviewedBy,
            operatorReviewedAtUtc,
            "admin",
            requestedAtUtc,
            requestedAtUtc,
            requestedAtUtc,
            requestedAtUtc);
}
