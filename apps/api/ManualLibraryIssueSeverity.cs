namespace api;

public static class ManualLibraryIssueSeverity
{
    public static string Resolve(string? issueType)
    {
        var normalized = (issueType ?? string.Empty).Trim().ToLowerInvariant();

        return normalized switch
        {
            "device_specific_issue" => "Info",
            "audio_wrong_during_playback" => "High",
            "playback_stall" => "High",
            "resume_starts_over" => "High",
            "manual_issue" => "Warning",
            "seeking_issue" => "Warning",
            "subtitle_unusable" => "Warning",
            _ => "Warning"
        };
    }
}
