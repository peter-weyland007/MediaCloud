using Xunit;

public sealed class PlaybackDiagnosticsHistoryMatchingTests
{
    [Fact]
    public void IsLikelyMatch_returns_true_for_exact_title_match()
    {
        Assert.True(PlaybackDiagnosticsHistoryMatching.IsLikelyMatch(
            "Fantastic Beasts: The Crimes of Grindelwald",
            "Fantastic Beasts: The Crimes of Grindelwald",
            2018));
    }

    [Fact]
    public void IsLikelyMatch_returns_true_when_full_title_contains_expected_title()
    {
        Assert.True(PlaybackDiagnosticsHistoryMatching.IsLikelyMatch(
            "Fantastic Beasts: The Crimes of Grindelwald (2018)",
            "Fantastic Beasts: The Crimes of Grindelwald",
            2018));
    }

    [Fact]
    public void IsLikelyMatch_returns_false_for_different_movie_titles()
    {
        Assert.False(PlaybackDiagnosticsHistoryMatching.IsLikelyMatch(
            "Fantastic Beasts and Where to Find Them",
            "Fantastic Beasts: The Crimes of Grindelwald",
            2018));
    }
}
