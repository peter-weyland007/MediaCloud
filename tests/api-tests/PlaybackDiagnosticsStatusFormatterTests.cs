using web.Components.Shared;
using Xunit;

public sealed class PlaybackDiagnosticsStatusFormatterTests
{
    [Fact]
    public void BuildPullStatus_reports_found_when_rows_were_imported_or_updated()
    {
        var payload = new web.Components.Shared.PullPlaybackDiagnosticsResponse(1474, 0, 1, 1, true, false, "Tautulli returned matching playback diagnostics for this item.");

        var message = PlaybackDiagnosticsStatusFormatter.BuildPullStatus(payload, "2026-04-15 12:34");

        Assert.StartsWith("Pull succeeded: found matching playback diagnostics.", message);
        Assert.Contains("Imported 0, updated 1, total stored 1.", message);
    }

    [Fact]
    public void BuildPullStatus_reports_no_matches_yet_when_pull_returns_zero_rows()
    {
        var payload = new web.Components.Shared.PullPlaybackDiagnosticsResponse(1474, 0, 0, 0, true, true, "Tautulli has no matching playback diagnostics for this item yet. MediaCloud also checked active Plex sessions and found nothing current to import.");

        var message = PlaybackDiagnosticsStatusFormatter.BuildPullStatus(payload, "2026-04-15 12:35");

        Assert.StartsWith("Pull succeeded: no matching playback diagnostics found yet.", message);
        Assert.Contains("Imported 0, updated 0, total stored 0.", message);
    }
}
