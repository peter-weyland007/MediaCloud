using System;
using api;
using Xunit;

public sealed class UserReportedIssueDetailsTests
{
    [Fact]
    public void Create_normalizes_reported_playback_issue_metadata()
    {
        var now = new DateTimeOffset(2026, 4, 13, 20, 15, 0, TimeSpan.Zero);
        var details = UserReportedIssueDetailsFactory.Create(
            notes: "Playback stalls after 10 minutes.",
            affectedClient: " Living Room TV ",
            affectedDevice: " Samsung Q90 ",
            origin: " operator ",
            flaggedAtUtc: now,
            availableAudioLanguages: new[] { "eng" },
            availableSubtitleLanguages: new[] { "eng", "spa" });

        Assert.Equal("Playback stalls after 10 minutes.", details.Notes);
        Assert.Equal("Living Room TV", details.AffectedClient);
        Assert.Equal("Samsung Q90", details.AffectedDevice);
        Assert.Equal("operator", details.Origin);
        Assert.Equal(now, details.FlaggedAtUtc);
        Assert.Single(details.AvailableAudioLanguages);
        Assert.Equal(2, details.AvailableSubtitleLanguages.Count);
    }

    [Fact]
    public void Serialize_and_parse_round_trip_preserves_client_device_and_notes()
    {
        var details = UserReportedIssueDetailsFactory.Create(
            notes: "Resume starts over on Roku.",
            affectedClient: "Roku",
            affectedDevice: "Bedroom TV",
            origin: "manual",
            flaggedAtUtc: new DateTimeOffset(2026, 4, 13, 20, 30, 0, TimeSpan.Zero),
            availableAudioLanguages: new[] { "eng" },
            availableSubtitleLanguages: Array.Empty<string>());

        var json = UserReportedIssueDetailsJson.Serialize(details);
        var parsed = UserReportedIssueDetailsJson.Parse(json);

        Assert.Equal("Resume starts over on Roku.", parsed.Notes);
        Assert.Equal("Roku", parsed.AffectedClient);
        Assert.Equal("Bedroom TV", parsed.AffectedDevice);
        Assert.Equal("manual", parsed.Origin);
    }

    [Fact]
    public void Parse_supports_legacy_manual_issue_payloads_with_notes_only()
    {
        const string legacyJson = """
            {
              "notes": "Subtitles are wrong.",
              "origin": "manual"
            }
            """;

        var parsed = UserReportedIssueDetailsJson.Parse(legacyJson);

        Assert.Equal("Subtitles are wrong.", parsed.Notes);
        Assert.Equal("manual", parsed.Origin);
        Assert.Equal(string.Empty, parsed.AffectedClient);
        Assert.Equal(string.Empty, parsed.AffectedDevice);
    }
}
