using api;
using Xunit;

public sealed class TelevisionDuplicateKeysTests
{
    [Fact]
    public void BuildTitleYearAliases_includes_title_and_sort_title_forms()
    {
        var aliases = TelevisionDuplicateKeys.BuildTitleYearAliases(
            "The Religion Business",
            "Religion Business",
            2025);

        Assert.Contains("thereligionbusiness:2025", aliases);
        Assert.Contains("religionbusiness:2025", aliases);
    }

    [Fact]
    public void BuildTitleYearAliases_returns_empty_when_year_missing()
    {
        var aliases = TelevisionDuplicateKeys.BuildTitleYearAliases("The Madison", "Madison", null);

        Assert.Empty(aliases);
    }
}
