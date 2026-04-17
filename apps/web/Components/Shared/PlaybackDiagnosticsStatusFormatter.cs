namespace web.Components.Shared;

public static class PlaybackDiagnosticsStatusFormatter
{
    public static string BuildPullStatus(PullPlaybackDiagnosticsResponse? payload, string checkedAtLabel)
    {
        var importedCount = payload?.ImportedCount ?? 0;
        var updatedCount = payload?.UpdatedCount ?? 0;
        var totalCount = payload?.TotalCount ?? 0;
        var sourceMessage = payload?.Message ?? "Playback diagnostics pull complete.";
        var outcome = importedCount + updatedCount > 0
            ? "Pull succeeded: found matching playback diagnostics."
            : "Pull succeeded: no matching playback diagnostics found yet.";

        return $"{outcome} Checked at {checkedAtLabel}. Imported {importedCount}, updated {updatedCount}, total stored {totalCount}. {sourceMessage}";
    }
}
