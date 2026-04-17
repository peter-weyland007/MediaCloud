namespace web.Components.Shared;

public sealed record GuidedChecklistStep(
    string Key,
    string Title,
    string Status,
    bool IsComplete,
    string Summary,
    string PrimaryActionKey,
    string PrimaryActionLabel,
    string? SecondaryActionKey = null,
    string? SecondaryActionLabel = null);

public static class GuidedRemediationChecklist
{
    public static IReadOnlyList<GuidedChecklistStep> Build(
        bool fileInspected,
        bool hasRuntimeMismatch,
        bool hasPlaybackDiagnostics,
        bool hasCompatibilityRecommendation,
        bool safeToQueue)
    {
        return
        [
            BuildInspectFileStep(fileInspected),
            BuildVerifyVersionStep(fileInspected, hasRuntimeMismatch),
            BuildPlaybackEvidenceStep(hasPlaybackDiagnostics),
            BuildChooseFixStep(fileInspected, hasCompatibilityRecommendation, safeToQueue),
            BuildVerifyResultStep(fileInspected, hasPlaybackDiagnostics)
        ];
    }

    private static GuidedChecklistStep BuildInspectFileStep(bool fileInspected)
        => fileInspected
            ? new(
                "inspect_file",
                "Analyze the file",
                "Complete",
                true,
                "MediaCloud has local file facts to work from.",
                "analyze_file",
                "Analyze file")
            : new(
                "inspect_file",
                "Analyze the file",
                "Action needed",
                false,
                "Probe runtime and media traits before taking bigger remediation actions.",
                "analyze_file",
                "Analyze file");

    private static GuidedChecklistStep BuildVerifyVersionStep(bool fileInspected, bool hasRuntimeMismatch)
    {
        if (!fileInspected)
        {
            return new(
                "verify_version",
                "Verify version / runtime",
                "Waiting",
                false,
                "Check whether this is the correct cut after file analysis finishes.",
                "analyze_file",
                "Analyze file");
        }

        return hasRuntimeMismatch
            ? new(
                "verify_version",
                "Verify version / runtime",
                "Action needed",
                false,
                "Runtime mismatch is still open. Confirm the cut or search for the correct release.",
                "search_correct_version",
                "Search correct version")
            : new(
                "verify_version",
                "Verify version / runtime",
                "Complete",
                true,
                "No active runtime mismatch is blocking remediation right now.",
                "analyze_file",
                "Analyze file");
    }

    private static GuidedChecklistStep BuildPlaybackEvidenceStep(bool hasPlaybackDiagnostics)
        => hasPlaybackDiagnostics
            ? new(
                "playback_evidence",
                "Pull playback evidence",
                "Complete",
                true,
                "Playback diagnostics exist, so MediaCloud has real-world evidence to compare against file traits.",
                "pull_playback_diagnostics",
                "Refresh playback diagnostics")
            : new(
                "playback_evidence",
                "Pull playback evidence",
                "Action needed",
                false,
                "Import Plex/Tautulli evidence before committing to a risky conversion or replacement.",
                "pull_playback_diagnostics",
                "Pull playback diagnostics");

    private static GuidedChecklistStep BuildChooseFixStep(bool fileInspected, bool hasCompatibilityRecommendation, bool safeToQueue)
    {
        if (!fileInspected)
        {
            return new(
                "choose_fix",
                "Choose the remediation path",
                "Waiting",
                false,
                "MediaCloud needs file analysis before it can suggest a fix path.",
                "analyze_file",
                "Analyze file");
        }

        if (!hasCompatibilityRecommendation)
        {
            return new(
                "choose_fix",
                "Choose the remediation path",
                "Waiting",
                false,
                "No compatibility recommendation is available yet. Refresh the evidence and re-check.",
                "analyze_file",
                "Analyze file",
                "pull_playback_diagnostics",
                "Pull playback diagnostics");
        }

        return safeToQueue
            ? new(
                "choose_fix",
                "Choose the remediation path",
                "Ready",
                false,
                "A safe sidecar remediation is available. Preview it before queueing.",
                "open_conversion_review",
                "Preview safe fix",
                "search_replacement",
                "Search replacement instead")
            : new(
                "choose_fix",
                "Choose the remediation path",
                "Ready",
                false,
                "This title needs manual conversion review or a replacement search instead of auto-queueing.",
                "open_conversion_review",
                "Open conversion review",
                "search_replacement",
                "Search replacement instead");
    }

    private static GuidedChecklistStep BuildVerifyResultStep(bool fileInspected, bool hasPlaybackDiagnostics)
        => fileInspected && hasPlaybackDiagnostics
            ? new(
                "verify_result",
                "Verify the result after remediation",
                "Ready",
                false,
                "After any change, rerun file analysis and playback diagnostics to confirm the issue is really resolved.",
                "pull_playback_diagnostics",
                "Refresh playback diagnostics",
                "analyze_file",
                "Analyze file")
            : new(
                "verify_result",
                "Verify the result after remediation",
                "Waiting",
                false,
                "Once MediaCloud has file analysis and playback evidence, use both again after remediation to verify the result.",
                "pull_playback_diagnostics",
                "Refresh playback diagnostics",
                "analyze_file",
                "Analyze file");
}
