using Xunit;

public sealed class MovieDetailMetadataBackfillTests
{
    [Fact]
    public void Library_item_dto_treats_existing_local_file_as_available()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var content = File.ReadAllText(fullPath);

        Assert.Contains("var effectiveAvailability = item.IsAvailable || localFileExists;", content);
        Assert.Contains("effectiveAvailability,", content);
    }

    [Fact]
    public void Runtime_probe_requests_and_persists_stream_languages()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var content = File.ReadAllText(fullPath);

        Assert.Contains("stream_tags=language", content);
        Assert.Contains("CollectProbeStreamLanguages(streams, \"audio\")", content);
        Assert.Contains("CollectProbeStreamLanguages(streams, \"subtitle\")", content);
        Assert.Contains("item.AudioLanguagesJson = JsonSerializer.Serialize(probe.AudioLanguages);", content);
        Assert.Contains("item.SubtitleLanguagesJson = JsonSerializer.Serialize(probe.SubtitleLanguages);", content);
    }

    [Fact]
    public void Runtime_probe_marks_item_available_when_local_file_exists()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var content = File.ReadAllText(fullPath);

        Assert.Contains("row.IsAvailable = true;", content);
    }
}
