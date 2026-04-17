using Xunit;

public sealed class StaleMetadataRuntimeReprobeSelectionTests
{
    [Fact]
    public void Batch_runtime_reprobe_targets_stale_probe_metadata_not_just_missing_runtime()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var content = File.ReadAllText(fullPath);

        Assert.Contains("x.PlayabilityCheckedAtUtc == null", content);
        Assert.Contains("x.AudioLanguagesJson == \"[]\"", content);
        Assert.Contains("x.SubtitleLanguagesJson == \"[]\" && x.PlayabilityCheckedAtUtc < x.UpdatedAtUtc", content);
    }
}
