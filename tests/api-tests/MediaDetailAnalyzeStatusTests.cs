using Xunit;

public sealed class MediaDetailAnalyzeStatusTests
{
    [Theory]
    [InlineData("apps/web/Components/Pages/MovieDetails.razor")]
    [InlineData("apps/web/Components/Pages/TvDetails.razor")]
    [InlineData("apps/web/Components/Pages/MusicDetails.razor")]
    public void Detail_pages_preserve_runtime_probe_message_after_reload(string relativePath)
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, relativePath));
        var content = File.ReadAllText(fullPath);

        Assert.Contains("payload?.Message", content);
        Assert.Contains("if (clearStatusMessage)", content);
        Assert.Contains("await LoadItemAsync(clearStatusMessage: false);", content);
    }
}
