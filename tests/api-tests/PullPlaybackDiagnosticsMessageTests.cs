using Xunit;

public sealed class PullPlaybackDiagnosticsMessageTests
{
    [Fact]
    public void Uses_checked_tautulli_message_when_integration_exists_but_no_matches_found()
    {
        var usedTautulli = true;
        var usedPlex = false;
        var imported = 0;
        var updated = 0;

        var message = TautulliPlaybackImport.BuildSourceMessage(usedTautulli, usedPlex, imported, updated);

        Assert.Equal("Tautulli has no matching playback diagnostics for this item yet.", message);
    }
}
