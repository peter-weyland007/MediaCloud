using web.Components.Shared;
using Xunit;

public sealed class GuidedRemediationChecklistTests
{
    [Fact]
    public void Build_returns_action_needed_for_missing_file_analysis_and_diagnostics()
    {
        var steps = GuidedRemediationChecklist.Build(
            fileInspected: false,
            hasRuntimeMismatch: false,
            hasPlaybackDiagnostics: false,
            hasCompatibilityRecommendation: false,
            safeToQueue: false);

        Assert.Collection(
            steps,
            inspect =>
            {
                Assert.Equal("inspect_file", inspect.Key);
                Assert.Equal("Action needed", inspect.Status);
                Assert.False(inspect.IsComplete);
                Assert.Equal("Analyze file", inspect.PrimaryActionLabel);
            },
            verify =>
            {
                Assert.Equal("verify_version", verify.Key);
                Assert.Equal("Waiting", verify.Status);
            },
            playback =>
            {
                Assert.Equal("playback_evidence", playback.Key);
                Assert.Equal("Action needed", playback.Status);
                Assert.Equal("Pull playback diagnostics", playback.PrimaryActionLabel);
            },
            choose =>
            {
                Assert.Equal("choose_fix", choose.Key);
                Assert.Equal("Waiting", choose.Status);
            },
            verifyResult =>
            {
                Assert.Equal("verify_result", verifyResult.Key);
                Assert.Equal("Waiting", verifyResult.Status);
            });
    }

    [Fact]
    public void Build_returns_ready_safe_fix_step_when_recommendation_can_queue()
    {
        var steps = GuidedRemediationChecklist.Build(
            fileInspected: true,
            hasRuntimeMismatch: false,
            hasPlaybackDiagnostics: true,
            hasCompatibilityRecommendation: true,
            safeToQueue: true);

        var chooseFix = Assert.Single(steps, step => step.Key == "choose_fix");
        Assert.Equal("Ready", chooseFix.Status);
        Assert.Equal("Preview safe fix", chooseFix.PrimaryActionLabel);
        Assert.Equal("Search replacement instead", chooseFix.SecondaryActionLabel);

        var verifyVersion = Assert.Single(steps, step => step.Key == "verify_version");
        Assert.True(verifyVersion.IsComplete);
        Assert.Equal("Complete", verifyVersion.Status);
    }

    [Fact]
    public void Build_returns_manual_review_path_when_safe_queue_is_blocked()
    {
        var steps = GuidedRemediationChecklist.Build(
            fileInspected: true,
            hasRuntimeMismatch: true,
            hasPlaybackDiagnostics: false,
            hasCompatibilityRecommendation: true,
            safeToQueue: false);

        var verifyVersion = Assert.Single(steps, step => step.Key == "verify_version");
        Assert.Equal("Action needed", verifyVersion.Status);
        Assert.Equal("Search correct version", verifyVersion.PrimaryActionLabel);

        var chooseFix = Assert.Single(steps, step => step.Key == "choose_fix");
        Assert.Equal("Ready", chooseFix.Status);
        Assert.Equal("Open conversion review", chooseFix.PrimaryActionLabel);
        Assert.Equal("Search replacement instead", chooseFix.SecondaryActionLabel);
    }
}
