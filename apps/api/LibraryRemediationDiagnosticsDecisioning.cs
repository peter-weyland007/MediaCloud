using api.Models;

namespace api;

public static class LibraryRemediationDiagnosticsDecisioning
{
    public static LibraryRemediationIntent ApplyLatestDiagnostic(LibraryRemediationIntent intent, PlaybackDiagnosticEntry? latestDiagnostic)
    {
        if (latestDiagnostic is null)
        {
            return intent;
        }

        var issueType = (intent.IssueType ?? string.Empty).Trim().ToLowerInvariant();
        if (issueType is not ("playback_failure" or "quality_issue"))
        {
            return intent;
        }

        var suspectedCause = (latestDiagnostic.SuspectedCause ?? string.Empty).Trim();
        var errorMessage = (latestDiagnostic.ErrorMessage ?? string.Empty).Trim();
        var combined = $"{suspectedCause} {errorMessage} {latestDiagnostic.Summary}".Trim().ToLowerInvariant();

        if (LooksLikeClientLimitation(combined, latestDiagnostic))
        {
            return intent with
            {
                RequestedAction = "manual_review",
                Confidence = "medium",
                ShouldSearchNow = false,
                ShouldBlacklistCurrentRelease = false,
                NeedsManualReview = true,
                PolicySummary = "Playback diagnostics point to a client/profile tuning issue rather than a bad media file. Review client/profile tuning before replacing media."
            };
        }

        if (LooksLikeRealMediaFailure(combined))
        {
            return intent with
            {
                ShouldSearchNow = true,
                ShouldBlacklistCurrentRelease = true,
                NeedsManualReview = false,
                PolicySummary = "Playback diagnostics point to a likely bad media file, so replacement remediation should proceed."
            };
        }

        return intent;
    }

    private static bool LooksLikeClientLimitation(string combined, PlaybackDiagnosticEntry latestDiagnostic)
    {
        var subtitleTranscode = string.Equals(latestDiagnostic.SubtitleDecision, "transcode", StringComparison.OrdinalIgnoreCase);
        var noHardError = string.IsNullOrWhiteSpace(latestDiagnostic.ErrorMessage);
        return noHardError && (
            combined.Contains("client limitation") ||
            combined.Contains("tv app") ||
            combined.Contains("weak client") ||
            combined.Contains("profile tuning") ||
            combined.Contains("subtitle") ||
            combined.Contains("pgs") ||
            combined.Contains("truehd") ||
            subtitleTranscode);
    }

    private static bool LooksLikeRealMediaFailure(string combined)
        => combined.Contains("corrupt")
           || combined.Contains("input/output error")
           || combined.Contains("i/o error")
           || combined.Contains("crc")
           || combined.Contains("read error")
           || combined.Contains("broken file")
           || combined.Contains("damaged");
}
