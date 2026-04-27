using Xunit;

public sealed class MediaItemDetailBodyPanelLayoutTests
{
    [Fact]
    public void Shared_media_detail_body_uses_combined_runtime_and_top_row_language_fields()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Shared/MediaItemDetailBody.razor"));
        var content = File.ReadAllText(fullPath);

        Assert.Contains("REPORTED / ACTUAL RUNTIME", content);
        Assert.DoesNotContain("<p class=\"card-label\">REPORTED RUNTIME</p>", content);
        Assert.DoesNotContain("<p class=\"card-label\">ACTUAL RUNTIME</p>", content);
        Assert.DoesNotContain("<p class=\"card-label\">AVAILABILITY</p>", content);
        Assert.Contains("$\"{FormatRuntime(Item.RuntimeMinutes)} / {FormatRuntime(Item.ActualRuntimeMinutes)}\"", content);
        Assert.Contains("AUDIO LANGUAGES", content);
        Assert.Contains("SUBTITLE LANGUAGES", content);
        Assert.Contains("FILE & IDENTIFIERS", content);
        Assert.Contains("PRIMARY FILE", content);
        Assert.Contains("FlattenMetricsPanel", content);
        Assert.Contains("border-bottom:1px solid rgba(51,65,85,0.65);", content);
        Assert.Contains("<p class=\"card-label\">SOURCE SERVICES</p>", content);
    }
}
