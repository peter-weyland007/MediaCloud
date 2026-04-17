using Xunit;

public sealed class DetailAnalyzeTimestampPresentationTests
{
    [Fact]
    public void Analyze_button_shows_last_analyzed_timestamp_instead_of_runtime_reprobe_message()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Shared/MediaItemDetailBody.razor"));
        var content = File.ReadAllText(fullPath);

        Assert.Contains("Last analyzed:", content);
        Assert.Contains("FormatTimestamp(Item.PlayabilityCheckedAtUtc)", content);
        Assert.DoesNotContain("@StatusMessage", content);
    }
}
