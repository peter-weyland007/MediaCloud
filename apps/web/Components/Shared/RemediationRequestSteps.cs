namespace web.Components.Shared;

public static class RemediationRequestSteps
{
    public static IReadOnlyList<string> Build(
        string providerName,
        string itemLabel,
        string issueType,
        string? reasonKey,
        string profileStep)
    {
        var provider = string.IsNullOrWhiteSpace(providerName) ? "the arr app" : providerName.Trim();
        var normalizedItemLabel = string.IsNullOrWhiteSpace(itemLabel) ? "item" : itemLabel.Trim().ToLowerInvariant();
        var normalizedIssueType = (issueType ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedReasonKey = string.IsNullOrWhiteSpace(reasonKey) ? null : reasonKey.Trim().ToLowerInvariant();
        var normalizedProfileStep = string.IsNullOrWhiteSpace(profileStep)
            ? "re-check the current acquisition profile before queueing the request."
            : profileStep.Trim();

        if (normalizedReasonKey is null)
        {
            return
            [
                $"keep this as an issue log only rather than sending any {provider} command",
                $"leave the current {normalizedItemLabel} release alone until you review metadata/mapping manually",
                normalizedProfileStep
            ];
        }

        var actionStep = normalizedReasonKey switch
        {
            "wrong_language" => $"ask {provider} for a replacement that better matches the expected audio/subtitle language",
            "playback_failure" => $"ask {provider} for a replacement because playback evidence points at a bad file rather than a client-only problem",
            "wrong_version" => $"ask {provider} for the correct {normalizedItemLabel} cut/version instead of the current release",
            "quality_issue" => $"ask {provider} for a better-quality replacement release",
            "corrupt_file" => $"ask {provider} for a replacement because this file looks corrupt or unplayable",
            _ => $"ask {provider} for a replacement based on the selected issue type"
        };

        var safeguardStep = normalizedIssueType switch
        {
            "corrupt_file" or "wrong_version" => "try blacklist-first remediation when MediaCloud has enough release history to identify the current bad release",
            "audio_language_mismatch" or "subtitle_missing" or "subtitle_language_mismatch" or "quality_issue" => "check the profile/policy guardrails first so MediaCloud can block a dumb repeat search when the acquisition profile is the real problem",
            "audio_out_of_sync" or "playback_failure" => "check the latest diagnostics first so MediaCloud does not replace the file for a pure Plex/client problem",
            _ => "record the selected issue type and notes so the remediation request is traceable in history"
        };

        return [actionStep, safeguardStep, normalizedProfileStep];
    }
}
