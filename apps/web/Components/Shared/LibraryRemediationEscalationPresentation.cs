namespace web.Components.Shared;

public sealed record LibraryRemediationEscalationAction(
    string ActionKey,
    string Label,
    string Summary);

public sealed record LibraryRemediationEscalationOutcome(
    string OutcomeKey,
    string Label,
    string Summary);

public sealed record LibraryRemediationEscalationView(
    bool ShowEscalation,
    string Title,
    string Summary,
    int RetryAttemptCount,
    IReadOnlyList<string> RecommendedSteps,
    IReadOnlyList<LibraryRemediationEscalationAction> RecommendedActions,
    IReadOnlyList<LibraryRemediationEscalationOutcome> RecommendedOutcomes,
    bool IsOperatorReviewed);

public static class LibraryRemediationEscalationPresentation
{
    public const int MaxRepeatSearchAttempts = 2;

    public static LibraryRemediationEscalationView Build(
        LibraryRemediationJobDto job,
        IEnumerable<LibraryRemediationJobDto> relatedJobs)
    {
        if (!string.Equals(job.RequestedAction, "search_replacement", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(job.LoopbackStatus, "Recommended", StringComparison.OrdinalIgnoreCase))
        {
            return None();
        }

        var retryAttemptCount = relatedJobs
            .Where(other => other.Id != job.Id)
            .Where(other => string.Equals(other.RequestedAction, "search_replacement", StringComparison.OrdinalIgnoreCase))
            .Where(other => string.Equals(NormalizeIssueKey(other), NormalizeIssueKey(job), StringComparison.OrdinalIgnoreCase))
            .Where(other => other.RequestedAtUtc >= job.RequestedAtUtc)
            .Count();

        if (retryAttemptCount < MaxRepeatSearchAttempts)
        {
            return None();
        }

        if (!string.IsNullOrWhiteSpace(job.OperatorReviewStatus))
        {
            return new(
                true,
                "Operator review recorded",
                BuildHandledSummary(job),
                retryAttemptCount,
                BuildHandledSteps(job),
                Array.Empty<LibraryRemediationEscalationAction>(),
                Array.Empty<LibraryRemediationEscalationOutcome>(),
                true);
        }

        return new(
            true,
            "Manual review required after repeated remediation failures",
            $"MediaCloud already attempted {retryAttemptCount} replacement retries for this issue and is now forcing operator review instead of another automatic search.",
            retryAttemptCount,
            BuildSteps(job),
            BuildActions(job),
            BuildOutcomes(job),
            false);
    }

    private static IReadOnlyList<string> BuildSteps(LibraryRemediationJobDto job)
    {
        var issueType = NormalizeIssueKey(job);
        var steps = new List<string>
        {
            "Review the latest verification summary and loopback notes before changing anything else.",
            "Compare source coverage and confirm the linked Radarr/Sonarr/Overseerr item is the one you actually want MediaCloud to manage."
        };

        switch (issueType)
        {
            case "wrong_language":
            case "audio_language_mismatch":
            case "subtitle_language_mismatch":
            case "subtitle_missing":
                steps.Add("Review language and subtitle profile rules before attempting another search.");
                break;
            case "runtime_mismatch":
            case "wrong_version":
                steps.Add("Review cut/version metadata, runtime evidence, and source mapping before retrying replacement.");
                break;
            default:
                steps.Add("Review playback evidence, metadata, and acquisition profile settings to decide whether retrying replacement still makes sense.");
                break;
        }

        steps.Add("Once human review is complete, record the operator outcome so MediaCloud stops presenting this escalation as unhandled.");
        return steps;
    }

    private static IReadOnlyList<string> BuildHandledSteps(LibraryRemediationJobDto job)
    {
        var status = (job.OperatorReviewStatus ?? string.Empty).Trim();
        if (string.Equals(status, "ManualTriageComplete", StringComparison.OrdinalIgnoreCase))
        {
            return ["Manual triage has been recorded for this escalated remediation path. Retry only if fresh evidence changes the decision."];
        }

        return ["Operator review has been recorded. Continue with manual follow-up or mark triage complete if no further action is needed."];
    }

    private static IReadOnlyList<LibraryRemediationEscalationAction> BuildActions(LibraryRemediationJobDto job)
    {
        var issueType = NormalizeIssueKey(job);
        var actions = new List<LibraryRemediationEscalationAction>
        {
            new("analyze_file", "Analyze file", "Refresh runtime/playability evidence on the current file before making another decision."),
            new("pull_playback_diagnostics", "Pull playback diagnostics", "Refresh real-world playback evidence so manual review is based on current data."),
            new("open_settings", "Review profile rules", "Open Settings to review profile, language, and quality policy that may be driving bad replacements.")
        };

        switch (issueType)
        {
            case "runtime_mismatch":
            case "wrong_version":
                actions.Add(new("review_current_detail", "Review current detail", "Review the current detail page metadata and issue context for version/runtime mismatches."));
                break;
            default:
                actions.Add(new("review_source_coverage", "Review source coverage", "Inspect source links and monitoring state before trying anything else."));
                break;
        }

        return actions;
    }

    private static IReadOnlyList<LibraryRemediationEscalationOutcome> BuildOutcomes(LibraryRemediationJobDto job)
    {
        var issueType = NormalizeIssueKey(job);
        var outcomes = new List<LibraryRemediationEscalationOutcome>();

        switch (issueType)
        {
            case "wrong_language":
            case "audio_language_mismatch":
            case "subtitle_language_mismatch":
            case "subtitle_missing":
                outcomes.Add(new("ProfileReviewed", "Mark profile review done", "Record that language/profile rules were reviewed for this escalated remediation."));
                outcomes.Add(new("SourceReviewed", "Mark source review done", "Record that source linkage and coverage were reviewed before manual follow-up."));
                break;
            case "runtime_mismatch":
            case "wrong_version":
                outcomes.Add(new("MetadataReviewed", "Mark metadata review done", "Record that runtime/version evidence and metadata mapping were reviewed."));
                break;
            default:
                outcomes.Add(new("SourceReviewed", "Mark source review done", "Record that source linkage and coverage were reviewed before manual follow-up."));
                outcomes.Add(new("MetadataReviewed", "Mark metadata review done", "Record that metadata and evidence were reviewed before manual follow-up."));
                break;
        }

        outcomes.Add(new("ManualTriageComplete", "Mark manual triage complete", "Record that the escalated remediation path has been handled by an operator."));
        return outcomes;
    }

    private static string BuildHandledSummary(LibraryRemediationJobDto job)
    {
        var summary = string.IsNullOrWhiteSpace(job.OperatorReviewSummary)
            ? "Operator follow-up was recorded for this escalated remediation path."
            : job.OperatorReviewSummary.Trim();
        var reviewedBy = string.IsNullOrWhiteSpace(job.OperatorReviewedBy) ? "an operator" : job.OperatorReviewedBy.Trim();
        var reviewedAt = job.OperatorReviewedAtUtc.HasValue
            ? job.OperatorReviewedAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : "an unknown time";
        return $"{summary} Recorded by {reviewedBy} on {reviewedAt}.";
    }

    private static string NormalizeIssueKey(LibraryRemediationJobDto job)
        => (job.IssueType ?? job.Reason ?? string.Empty).Trim().ToLowerInvariant();

    private static LibraryRemediationEscalationView None()
        => new(false, string.Empty, string.Empty, 0, Array.Empty<string>(), Array.Empty<LibraryRemediationEscalationAction>(), Array.Empty<LibraryRemediationEscalationOutcome>(), false);
}
