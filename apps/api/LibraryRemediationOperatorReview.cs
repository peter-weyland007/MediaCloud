using api.Models;

namespace api;

public static class LibraryRemediationOperatorReviewTracker
{
    private static readonly Dictionary<string, string> OutcomeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["profilereviewed"] = "ProfileReviewed",
        ["profile_reviewed"] = "ProfileReviewed",
        ["sourcereviewed"] = "SourceReviewed",
        ["source_reviewed"] = "SourceReviewed",
        ["metadatareviewed"] = "MetadataReviewed",
        ["metadata_reviewed"] = "MetadataReviewed",
        ["manualtriagecomplete"] = "ManualTriageComplete",
        ["manual_triage_complete"] = "ManualTriageComplete"
    };

    public static bool TryNormalizeOutcome(string? outcome, out string normalizedOutcome)
    {
        var key = (outcome ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            normalizedOutcome = string.Empty;
            return false;
        }

        if (OutcomeMap.TryGetValue(key, out var mapped))
        {
            normalizedOutcome = mapped;
            return true;
        }

        normalizedOutcome = string.Empty;
        return false;
    }

    public static void Apply(LibraryRemediationJob job, string outcome, string actor, DateTimeOffset reviewedAtUtc)
    {
        if (!TryNormalizeOutcome(outcome, out var normalizedOutcome))
        {
            throw new ArgumentException("Unsupported operator review outcome.", nameof(outcome));
        }

        var reviewedBy = string.IsNullOrWhiteSpace(actor) ? "admin" : actor.Trim();
        job.OperatorReviewStatus = normalizedOutcome;
        job.OperatorReviewSummary = BuildSummary(normalizedOutcome);
        job.OperatorReviewedBy = reviewedBy;
        job.OperatorReviewedAtUtc = reviewedAtUtc;
        job.UpdatedAtUtc = reviewedAtUtc;
    }

    public static string BuildSummary(string normalizedOutcome)
        => normalizedOutcome switch
        {
            "ProfileReviewed" => "Operator reviewed profile rules and policy before continuing manual follow-up.",
            "SourceReviewed" => "Operator reviewed source coverage, linkage, and monitoring state before continuing manual follow-up.",
            "MetadataReviewed" => "Operator reviewed runtime/version metadata and evidence before continuing manual follow-up.",
            "ManualTriageComplete" => "Operator completed manual triage for this escalated remediation path.",
            _ => "Operator recorded manual remediation follow-up."
        };
}
