using Xunit;

public sealed class SonarrEpisodeRuntimeSyncTests
{
    [Fact]
    public void ResolveActualRuntimeMinutes_keeps_existing_runtime_when_file_path_is_unchanged()
    {
        var result = SonarrEpisodeRuntimeSync.ResolveActualRuntimeMinutes(
            hasFile: true,
            resolvedFilePath: "/media/tv/show/episode.mkv",
            previousFilePath: "/media/tv/show/episode.mkv",
            existingActualRuntimeMinutes: 42.5);

        Assert.Equal(42.5, result);
    }

    [Fact]
    public void ResolveActualRuntimeMinutes_clears_runtime_when_file_path_changes()
    {
        var result = SonarrEpisodeRuntimeSync.ResolveActualRuntimeMinutes(
            hasFile: true,
            resolvedFilePath: "/media/tv/show/new-episode.mkv",
            previousFilePath: "/media/tv/show/old-episode.mkv",
            existingActualRuntimeMinutes: 42.5);

        Assert.Null(result);
    }

    [Fact]
    public void ResolveActualRuntimeMinutes_clears_runtime_when_episode_has_no_file()
    {
        var result = SonarrEpisodeRuntimeSync.ResolveActualRuntimeMinutes(
            hasFile: false,
            resolvedFilePath: string.Empty,
            previousFilePath: "/media/tv/show/episode.mkv",
            existingActualRuntimeMinutes: 42.5);

        Assert.Null(result);
    }
}
