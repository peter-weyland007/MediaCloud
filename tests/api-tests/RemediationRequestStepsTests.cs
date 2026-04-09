using web.Components.Shared;
using Xunit;

public sealed class RemediationRequestStepsTests
{
    [Fact]
    public void Build_returns_sonarr_episode_steps_for_quality_issue()
    {
        var steps = RemediationRequestSteps.Build(
            providerName: "Sonarr",
            itemLabel: "episode",
            issueType: "quality_issue",
            reasonKey: "quality_issue",
            profileStep: "use the current 'HD-1080p' profile because it still allows higher-quality replacements");

        Assert.Equal(3, steps.Count);
        Assert.Equal("ask Sonarr for a better-quality replacement release", steps[0]);
        Assert.Equal("check the profile/policy guardrails first so MediaCloud can block a dumb repeat search when the acquisition profile is the real problem", steps[1]);
        Assert.Equal("use the current 'HD-1080p' profile because it still allows higher-quality replacements", steps[2]);
    }

    [Fact]
    public void Build_returns_issue_log_only_steps_when_no_reason_key_exists()
    {
        var steps = RemediationRequestSteps.Build(
            providerName: "Sonarr",
            itemLabel: "episode",
            issueType: "metadata_issue",
            reasonKey: null,
            profileStep: "re-check the current acquisition profile before queueing the request.");

        Assert.Equal(3, steps.Count);
        Assert.Equal("keep this as an issue log only rather than sending any Sonarr command", steps[0]);
        Assert.Equal("leave the current episode release alone until you review metadata/mapping manually", steps[1]);
        Assert.Equal("re-check the current acquisition profile before queueing the request.", steps[2]);
    }
}
