using api;
using Xunit;

public sealed class LibraryIssueDetailsSortTests
{
    [Fact]
    public void Build_prefers_runtime_difference_for_runtime_mismatch_rows()
    {
        var sort = LibraryIssueDetailsSort.Build(
            "runtime_mismatch",
            "{\"diffMinutes\":42.5,\"diffPercent\":17.3}");

        Assert.Equal(2, sort.Priority);
        Assert.Equal(42.5, sort.NumericValue);
        Assert.Contains("42.5", sort.TextValue);
    }

    [Fact]
    public void Build_uses_file_name_for_runtime_probe_failures()
    {
        var sort = LibraryIssueDetailsSort.Build(
            "runtime_probe_failed",
            "{\"filePath\":\"/media/movies/Alien (1979).mkv\",\"probeError\":\"Permission denied\"}");

        Assert.Equal(1, sort.Priority);
        Assert.Equal("alien (1979).mkv | permission denied", sort.TextValue);
    }
}
