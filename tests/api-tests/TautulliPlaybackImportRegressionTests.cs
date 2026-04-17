using Xunit;

using System.Text.Json;

public sealed class TautulliPlaybackImportRegressionTests
{
    [Fact]
    public void BuildHistoryQuery_uses_after_unix_timestamp_instead_of_start_date()
    {
        var now = new DateTimeOffset(2026, 4, 14, 18, 32, 40, TimeSpan.Zero);

        var query = TautulliPlaybackImport.BuildHistoryQuery("23929", "movie", hoursBack: 72, maxItems: 10, now);

        Assert.Equal("23929", query["rating_key"]);
        Assert.Equal("movie", query["media_type"]);
        Assert.Equal("10", query["length"]);
        Assert.Equal("date", query["order_column"]);
        Assert.Equal("desc", query["order_dir"]);
        Assert.Equal("1775932360", query["after"]);
        Assert.False(query.ContainsKey("start_date"));
    }

    [Fact]
    public void BuildSourceMessage_reports_checked_tautulli_and_plex_when_no_rows_were_imported()
    {
        var message = TautulliPlaybackImport.BuildSourceMessage(usedTautulli: true, usedPlex: true, imported: 0, updated: 0);

        Assert.Equal("Tautulli has no matching playback diagnostics for this item yet. MediaCloud also checked active Plex sessions and found nothing current to import.", message);
    }

    [Fact]
    public void ReadStringLike_returns_number_values_as_strings_for_tautulli_history_fields()
    {
        using var doc = JsonDocument.Parse("""
        {
          "rating_key": 23929,
          "row_id": 714
        }
        """);

        Assert.Equal("23929", TautulliPlaybackImport.ReadStringLike(doc.RootElement, "rating_key"));
        Assert.Equal("714", TautulliPlaybackImport.ReadStringLike(doc.RootElement, "row_id"));
    }
}
