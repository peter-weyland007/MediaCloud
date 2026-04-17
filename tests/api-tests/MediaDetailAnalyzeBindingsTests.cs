using Xunit;

public sealed class MediaDetailAnalyzeBindingsTests
{
    [Theory]
    [InlineData("apps/web/Components/Pages/MovieDetails.razor")]
    [InlineData("apps/web/Components/Pages/TvDetails.razor")]
    [InlineData("apps/web/Components/Pages/MusicDetails.razor")]
    public void Detail_pages_bind_analyze_parameter_without_unused_status_message_parameter(string relativePath)
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, relativePath));
        var content = File.ReadAllText(fullPath);

        Assert.DoesNotContain("StatusMessage=\"@_statusMessage\"", content);
        Assert.Contains("OnAnalyze=\"@ProbeRuntimeNowAsync\"", content);
    }
}
