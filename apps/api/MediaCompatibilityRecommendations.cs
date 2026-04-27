using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using api.Models;

public sealed record MediaCompatibilityRecommendationResponse(
    long LibraryItemId,
    bool HasRecommendation,
    bool SafeToQueue,
    string RecommendationKey,
    string RecommendationTitle,
    string Confidence,
    string WhySummary,
    string ExpectedBenefit,
    string RiskSummary,
    string OutputStrategy,
    string CommandPreview,
    string ActivePresetName,
    string ProfileSummary,
    IReadOnlyList<MediaCompatibilityComparisonRowResponse> ComparisonRows,
    IReadOnlyList<string> Reasons,
    string ReviewDialogTitle,
    string ReviewOperatorWarning,
    IReadOnlyList<string> BlockedReasons,
    IReadOnlyList<string> ManualPlanSteps,
    string ManualCommandPreview,
    string UserDecisionGuidance = "",
    string BestActionLabel = "",
    string CurrentFileRiskLabel = "Unknown",
    string ConversionRiskLabel = "Unknown",
    string UserDecisionSummary = "");

public sealed record QueueMediaCompatibilityRemediationResponse(long LibraryItemId, long JobId, bool Queued, bool AlreadyQueued, string Message);
public sealed record RemoveMediaCompatibilityRemediationResponse(long JobId, bool Removed, string Message);
public sealed record MediaCompatibilityExecutionContext(string RecommendationKey, string InputPath, string OutputPath, string CommandPreview, string OutputStrategy, IReadOnlyList<string> Reasons);
public sealed record MediaCompatibilityComparisonRowResponse(string Label, string InspectedValue, string SelectedProfileValue, string Status);

public static class MediaCompatibilityRecommendationEngine
{
    public static MediaCompatibilityRecommendationResponse Build(
        LibraryItem item,
        MediaPlayabilityStoredDetails details,
        PlaybackDiagnosticEntry? latestDiagnostic,
        MediaProfileSettingsResponse settings)
    {
        var reasons = new List<string>();
        reasons.AddRange(details.Reasons ?? []);
        var comparisonRows = BuildComparisonRows(item, details, settings);

        var container = Normalize(details.ContainerNames.FirstOrDefault());
        var discImageSource = IsDiscImageSource(item.PrimaryFilePath, container);
        var videoCodec = Normalize(details.VideoCodec);
        var audioCodecs = details.AudioCodecs.Select(Normalize).Where(x => x.Length > 0).Distinct().ToArray();
        var subtitleCodecs = details.SubtitleCodecs.Select(Normalize).Where(x => x.Length > 0).Distinct().ToArray();
        var imageSubtitlePresent = subtitleCodecs.Any(IsImageSubtitle);
        var recentTranscode = latestDiagnostic is not null
            && ($"{latestDiagnostic.Decision} {latestDiagnostic.TranscodeDecision} {latestDiagnostic.VideoDecision} {latestDiagnostic.AudioDecision} {latestDiagnostic.SubtitleDecision}")
                .Contains("transcode", StringComparison.OrdinalIgnoreCase);

        if (recentTranscode && !string.IsNullOrWhiteSpace(latestDiagnostic?.Summary))
        {
            reasons.Insert(0, latestDiagnostic!.Summary);
        }

        if (string.IsNullOrWhiteSpace(container) && string.IsNullOrWhiteSpace(videoCodec) && audioCodecs.Length == 0 && subtitleCodecs.Length == 0)
        {
            return CreateRecommendation(
                item,
                settings,
                comparisonRows,
                reasons,
                hasRecommendation: false,
                safeToQueue: false,
                recommendationTitle: "No recommendation yet",
                confidence: "low",
                whySummary: "MediaCloud needs a fresh file analysis before it can recommend a compatibility remediation.",
                expectedBenefit: "Run Analyze file first so MediaCloud can inspect container, codec, audio, and subtitle traits.",
                riskSummary: "No ffmpeg preview is generated without probe details.",
                outputStrategy: DefaultNoOverwriteStrategy,
                commandPreview: string.Empty,
                userDecisionGuidance: "Analyze this file first",
                bestActionLabel: "Analyze file",
                currentFileRiskLabel: "Unknown",
                conversionRiskLabel: "Unknown",
                userDecisionSummary: BuildAnalyzeFirstDecisionSummary());
        }

        if (discImageSource)
        {
            var blockedReasons = BuildBlockedReasons(comparisonRows);
            return CreateRecommendation(
                item,
                settings,
                comparisonRows,
                reasons,
                hasRecommendation: true,
                safeToQueue: false,
                recommendationKey: "manual_disc_image_review",
                recommendationTitle: "Manual review: disc image source detected",
                confidence: "high",
                whySummary: BuildWhySummary(item, details, latestDiagnostic, settings, "Current source is an ISO disc image, so MediaCloud should flag it distinctly instead of treating it like a normal container mismatch."),
                expectedBenefit: "Operators can spot ISO-backed titles immediately in the profile match matrix and avoid queuing the standard compatibility workflow against them.",
                riskSummary: "Disc images are outside the ordinary safe-remux path. MediaCloud will not auto-queue ffmpeg compatibility remediation for this packaging type.",
                outputStrategy: DefaultNoOverwriteStrategy,
                commandPreview: string.Empty,
                reviewDialogTitle: "Disc Image Review",
                reviewOperatorWarning: "ISO sources should be identified and reviewed manually before any remediation path is considered.",
                blockedReasons: blockedReasons,
                manualPlanSteps: ["Confirm the source file is an ISO disc image and leave it out of the standard compatibility queue for now."],
                userDecisionGuidance: "Better to request a new file",
                bestActionLabel: "Request better file",
                currentFileRiskLabel: "High",
                conversionRiskLabel: "High",
                userDecisionSummary: BuildRequestReplacementDecisionSummary(comparisonRows));
        }

        var preferredContainer = Normalize(settings.PreferredContainer);
        var preferredVideoCodec = Normalize(settings.PreferredVideoCodec);
        var preferredAudioCodec = Normalize(settings.PreferredAudioCodec);
        var requiresVideoTranscode = RequiresVideoTranscode(details, settings, preferredVideoCodec, videoCodec);
        var needsContainerRemux = container.Length > 0
            && preferredContainer.Length > 0
            && container != preferredContainer
            && !imageSubtitlePresent;
        var needsAudioConversion = audioCodecs.Any(codec => !IsPreferredAudio(codec, preferredAudioCodec));
        var prefersTextOnlyButImageSubs = settings.PreferTextSubtitlesOnly && imageSubtitlePresent;
        var currentFileRiskLabel = BuildCurrentFileRiskLabel(
            recentTranscode,
            item.PlayabilityScore,
            requiresVideoTranscode,
            prefersTextOnlyButImageSubs,
            discImageSource,
            needsContainerRemux,
            needsAudioConversion);
        var hasReasonToAct = recentTranscode
            || string.Equals(item.PlayabilityScore, "Risky", StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.PlayabilityScore, "Problematic", StringComparison.OrdinalIgnoreCase)
            || needsContainerRemux
            || needsAudioConversion
            || prefersTextOnlyButImageSubs;

        if (!hasReasonToAct)
        {
            return CreateRecommendation(
                item,
                settings,
                comparisonRows,
                reasons,
                hasRecommendation: false,
                safeToQueue: false,
                recommendationTitle: "No compatibility remediation recommended",
                confidence: "low",
                whySummary: "Current file traits do not justify an ffmpeg compatibility pass right now.",
                expectedBenefit: "Keep observing playback. Pull diagnostics again if client behavior changes.",
                riskSummary: "No ffmpeg preview was generated.",
                outputStrategy: DefaultNoOverwriteStrategy,
                commandPreview: string.Empty,
                userDecisionGuidance: "Safe to leave alone",
                bestActionLabel: "Leave as-is",
                currentFileRiskLabel: "Low",
                conversionRiskLabel: "Not needed",
                userDecisionSummary: BuildLeaveAloneDecisionSummary(comparisonRows));
        }

        if (requiresVideoTranscode)
        {
            var blockedReasons = BuildBlockedReasons(comparisonRows);
            var manualCommandPreview = BuildManualCommandPreview(item.PrimaryFilePath, preferredContainer, preferredAudioCodec, includeSubtitleTextConversion: true);
            var manualPlanSteps = BuildManualPlanSteps(
                "Run a one-off ffmpeg compatibility transcode to bring video, audio, and container back inside the preferred playback target.",
                "Keep the output as a sidecar review artifact until someone validates playback on the target client.",
                manualCommandPreview);

            return CreateRecommendation(
                item,
                settings,
                comparisonRows,
                reasons,
                hasRecommendation: true,
                safeToQueue: false,
                recommendationKey: "manual_video_review",
                recommendationTitle: "Manual review: would require video transcode",
                confidence: recentTranscode ? "medium" : "low",
                whySummary: BuildWhySummary(item, details, latestDiagnostic, settings, "Current file falls outside the preferred video target, so a compatibility pass would need a full video transcode."),
                expectedBenefit: "Manual review is safer here than auto-queueing a lossy video conversion.",
                riskSummary: "Full video transcoding can change quality, HDR, bitrate, and file size. MediaCloud will not auto-queue that in phase 1.",
                outputStrategy: "Create sidecar remediated copy after manual review; never overwrite original automatically.",
                commandPreview: string.Empty,
                reviewDialogTitle: "Manual Conversion Review",
                reviewOperatorWarning: "This path requires a lossy or time-consuming video transcode. MediaCloud will not auto-queue it without explicit operator review.",
                blockedReasons: blockedReasons,
                manualPlanSteps: manualPlanSteps,
                manualCommandPreview: manualCommandPreview,
                userDecisionGuidance: "Better to request a new file",
                bestActionLabel: "Request better file",
                currentFileRiskLabel: currentFileRiskLabel,
                conversionRiskLabel: "High",
                userDecisionSummary: BuildRequestReplacementDecisionSummary(comparisonRows));
        }

        if (prefersTextOnlyButImageSubs)
        {
            var blockedReasons = BuildBlockedReasons(comparisonRows);
            var manualCommandPreview = BuildManualSubtitleCommandPreview(item.PrimaryFilePath);
            var manualPlanSteps = BuildManualPlanSteps(
                "Inspect the image-based subtitle tracks and decide whether OCR, subtitle replacement, or a release swap is the safer fix.",
                "If you keep this file, create a sidecar subtitle output first and verify Plex can direct play without forced burn-in.",
                manualCommandPreview);

            return CreateRecommendation(
                item,
                settings,
                comparisonRows,
                reasons,
                hasRecommendation: true,
                safeToQueue: false,
                recommendationKey: "manual_subtitle_review",
                recommendationTitle: "Manual review: subtitle format likely forces burn-in",
                confidence: recentTranscode ? "high" : "medium",
                whySummary: BuildWhySummary(item, details, latestDiagnostic, settings, "Image-based subtitles conflict with the preferred text-subtitle policy and are likely contributing to Plex burn-in/transcode behavior."),
                expectedBenefit: "Manual subtitle cleanup or replacement can reduce subtitle-driven transcodes.",
                riskSummary: "Phase 1 does not auto-convert image subtitles to text. OCR/subtitle replacement should be reviewed manually.",
                outputStrategy: "Create sidecar remediated copy after manual subtitle review; never overwrite original automatically.",
                commandPreview: string.Empty,
                reviewDialogTitle: "Manual Conversion Review",
                reviewOperatorWarning: "Image-based subtitles usually need OCR, replacement subtitles, or a deliberate burn-in decision. MediaCloud will not auto-queue that blindly.",
                blockedReasons: blockedReasons,
                manualPlanSteps: manualPlanSteps,
                manualCommandPreview: manualCommandPreview,
                userDecisionGuidance: "Better to request a new file",
                bestActionLabel: "Request better file",
                currentFileRiskLabel: currentFileRiskLabel,
                conversionRiskLabel: "Medium",
                userDecisionSummary: BuildSubtitleDecisionSummary(comparisonRows));
        }

        var recommendationKey = needsContainerRemux && needsAudioConversion
            ? "container_audio_sidecar"
            : needsAudioConversion
                ? "audio_sidecar"
                : "container_remux_sidecar";
        var title = needsContainerRemux && needsAudioConversion
            ? "Queue safe sidecar remux + audio compatibility pass"
            : needsAudioConversion
                ? "Queue safe audio compatibility pass"
                : "Queue safe container remux";
        var why = BuildWhySummary(
            item,
            details,
            latestDiagnostic,
            settings,
            needsContainerRemux && needsAudioConversion
                ? $"Current file differs from the preferred {settings.PreferredContainer.ToUpperInvariant()} / {settings.PreferredAudioCodec.ToUpperInvariant()} target and recent playback evidence suggests a safer sidecar compatibility pass is warranted."
                : needsAudioConversion
                    ? $"Current audio tracks do not align with the preferred {settings.PreferredAudioCodec.ToUpperInvariant()} target and are likely increasing Plex direct-stream/transcode pressure."
                    : $"Current container differs from the preferred {settings.PreferredContainer.ToUpperInvariant()} target, but video/audio traits are safe enough for a non-destructive remux preview.");
        var expectedBenefit = needsAudioConversion
            ? "Create a sidecar copy with video preserved and audio normalized toward the preferred client-safe codec profile."
            : "Create a sidecar copy in the preferred container so more Plex clients can direct play/direct stream it cleanly.";
        var risk = needsAudioConversion
            ? "Audio codecs/layout may change and subtitle handling still depends on the source tracks. Original file remains untouched."
            : "Container remuxes are low risk, but some subtitle streams may still need manual cleanup depending on the client.";
        var outputStrategy = "Create sidecar remediated copy; keep original file untouched until an operator reviews the result.";
        var commandPreview = BuildCommandPreview(item.PrimaryFilePath, preferredContainer, preferredAudioCodec, needsAudioConversion);

        return CreateRecommendation(
            item,
            settings,
            comparisonRows,
            reasons,
            hasRecommendation: true,
            safeToQueue: true,
            recommendationKey: recommendationKey,
            recommendationTitle: title,
            confidence: recentTranscode ? "high" : "medium",
            whySummary: why,
            expectedBenefit: expectedBenefit,
            riskSummary: risk,
            outputStrategy: outputStrategy,
            commandPreview: commandPreview,
            userDecisionGuidance: "Worth converting",
            bestActionLabel: "Convert this file",
            currentFileRiskLabel: currentFileRiskLabel,
            conversionRiskLabel: "Low",
            userDecisionSummary: BuildSafeQueueDecisionSummary(comparisonRows));
    }

    public static LibraryRemediationJob BuildPreviewJob(
        MediaCompatibilityRecommendationResponse recommendation,
        string actor,
        DateTimeOffset now)
    {
        var commandPreview = GetApprovedCommandPreview(recommendation);
        return new()
        {
            LibraryItemId = recommendation.LibraryItemId,
            ServiceKey = "ffmpeg",
            ServiceDisplayName = "FFmpeg",
            RequestedAction = recommendation.RecommendationKey,
            CommandName = "ffmpeg-compat-preview",
            IssueType = "playback_compatibility",
            Reason = recommendation.WhySummary,
            Notes = commandPreview,
            ReasonCategory = "compatibility",
            Confidence = recommendation.Confidence,
            ShouldSearchNow = false,
            ShouldBlacklistCurrentRelease = false,
            NeedsManualReview = !recommendation.SafeToQueue,
            NotesRecordedOnly = true,
            LookedUpRemotely = false,
            PolicySummary = recommendation.ExpectedBenefit,
            NotesHandling = recommendation.SafeToQueue ? "preview_queue_only" : "approved_manual_preview_queue",
            ProfileDecision = recommendation.OutputStrategy,
            ProfileSummary = recommendation.ProfileSummary,
            Status = "Queued",
            SearchStatus = "NotApplicable",
            BlacklistStatus = "NotApplicable",
            OutcomeSummary = "Queued FFmpeg command for approved compatibility remediation.",
            ResultMessage = string.IsNullOrWhiteSpace(commandPreview) ? recommendation.WhySummary : commandPreview,
            ReleaseSummary = recommendation.RecommendationTitle,
            ReleaseContextJson = JsonSerializer.Serialize(new MediaCompatibilityExecutionContext(
                recommendation.RecommendationKey,
                BuildInputPathFromPreview(commandPreview),
                BuildOutputPathFromPreview(commandPreview),
                commandPreview,
                recommendation.OutputStrategy,
                recommendation.Reasons.ToArray())),
            RequestedBy = actor,
            RequestedAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    public static LibraryRemediationJob BuildManualReviewJob(
        MediaCompatibilityRecommendationResponse recommendation,
        string actor,
        DateTimeOffset now)
        => new()
        {
            LibraryItemId = recommendation.LibraryItemId,
            ServiceKey = "ffmpeg",
            ServiceDisplayName = "FFmpeg",
            RequestedAction = recommendation.RecommendationKey,
            CommandName = "ffmpeg-manual-review",
            IssueType = "playback_compatibility",
            Reason = recommendation.WhySummary,
            Notes = string.Join(Environment.NewLine, recommendation.ManualPlanSteps),
            ReasonCategory = "compatibility",
            Confidence = recommendation.Confidence,
            ShouldSearchNow = false,
            ShouldBlacklistCurrentRelease = false,
            NeedsManualReview = true,
            NotesRecordedOnly = true,
            LookedUpRemotely = false,
            PolicySummary = recommendation.ExpectedBenefit,
            NotesHandling = "manual_review_only",
            ProfileDecision = recommendation.OutputStrategy,
            ProfileSummary = recommendation.ProfileSummary,
            Status = "BlockedManualReview",
            SearchStatus = "NotApplicable",
            BlacklistStatus = "NotApplicable",
            OutcomeSummary = "Manual conversion review queued for operator follow-up. No background ffmpeg job was started.",
            ResultMessage = string.IsNullOrWhiteSpace(recommendation.ManualCommandPreview) ? "manual-review" : recommendation.ManualCommandPreview,
            ReleaseSummary = string.IsNullOrWhiteSpace(recommendation.ReviewDialogTitle) ? recommendation.RecommendationTitle : recommendation.ReviewDialogTitle,
            ReleaseContextJson = JsonSerializer.Serialize(new
            {
                recommendation.RecommendationKey,
                recommendation.BlockedReasons,
                recommendation.ManualPlanSteps,
                recommendation.ManualCommandPreview,
                recommendation.ReviewOperatorWarning
            }),
            RequestedBy = actor,
            RequestedAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

    private const string DefaultNoOverwriteStrategy = "Create sidecar remediated copy; never overwrite original automatically.";
    private const string UnsavedPresetName = "Custom / Unsaved";

    public static string GetApprovedCommandPreview(MediaCompatibilityRecommendationResponse recommendation)
        => !string.IsNullOrWhiteSpace(recommendation.CommandPreview)
            ? recommendation.CommandPreview
            : recommendation.ManualCommandPreview;

    private static MediaCompatibilityRecommendationResponse CreateRecommendation(
        LibraryItem item,
        MediaProfileSettingsResponse settings,
        IReadOnlyList<MediaCompatibilityComparisonRowResponse> comparisonRows,
        IReadOnlyList<string> reasons,
        bool hasRecommendation,
        bool safeToQueue,
        string recommendationKey = "",
        string recommendationTitle = "",
        string confidence = "low",
        string whySummary = "",
        string expectedBenefit = "",
        string riskSummary = "",
        string outputStrategy = "",
        string commandPreview = "",
        string reviewDialogTitle = "",
        string reviewOperatorWarning = "",
        IReadOnlyList<string>? blockedReasons = null,
        IReadOnlyList<string>? manualPlanSteps = null,
        string manualCommandPreview = "",
        string userDecisionGuidance = "",
        string bestActionLabel = "",
        string currentFileRiskLabel = "Unknown",
        string conversionRiskLabel = "Unknown",
        string userDecisionSummary = "")
        => new(
            item.Id,
            hasRecommendation,
            safeToQueue,
            recommendationKey,
            recommendationTitle,
            confidence,
            whySummary,
            expectedBenefit,
            riskSummary,
            outputStrategy,
            commandPreview,
            GetActivePresetName(settings),
            MediaProfileSettings.BuildSummary(settings),
            comparisonRows,
            reasons,
            reviewDialogTitle,
            reviewOperatorWarning,
            blockedReasons ?? [],
            manualPlanSteps ?? [],
            manualCommandPreview,
            userDecisionGuidance,
            bestActionLabel,
            currentFileRiskLabel,
            conversionRiskLabel,
            userDecisionSummary);

    private static string GetActivePresetName(MediaProfileSettingsResponse settings)
        => string.IsNullOrWhiteSpace(settings.ActivePresetName) ? UnsavedPresetName : settings.ActivePresetName;

    private static bool RequiresVideoTranscode(MediaPlayabilityStoredDetails details, MediaProfileSettingsResponse settings, string preferredVideoCodec, string videoCodec)
    {
        if (string.IsNullOrWhiteSpace(videoCodec))
        {
            return true;
        }

        if ((videoCodec is "hevc" or "h265") && !settings.AllowHevc)
        {
            return true;
        }

        if (videoCodec != preferredVideoCodec && !(settings.AllowHevc && videoCodec is "hevc" or "h265"))
        {
            return true;
        }

        if (!settings.Allow10BitVideo && IsTenBit(details))
        {
            return true;
        }

        if (ExceedsResolution(details, settings.MaxPreferredResolution))
        {
            return true;
        }

        if (details.BitrateBitsPerSecond.HasValue && details.BitrateBitsPerSecond.Value > settings.MaxPreferredBitrateMbps * 1_000_000L)
        {
            return true;
        }

        return false;
    }

    private static string BuildWhySummary(
        LibraryItem item,
        MediaPlayabilityStoredDetails details,
        PlaybackDiagnosticEntry? latestDiagnostic,
        MediaProfileSettingsResponse settings,
        string fallback)
    {
        var container = details.ContainerNames.FirstOrDefault() ?? "unknown container";
        var video = string.IsNullOrWhiteSpace(details.VideoCodec) ? "unknown video" : details.VideoCodec.ToUpperInvariant();
        var audio = details.AudioCodecs.Count == 0 ? "unknown audio" : string.Join(", ", details.AudioCodecs.Select(x => x.ToUpperInvariant()));
        var diagnostic = latestDiagnostic is null
            ? "No recent playback diagnostic is stored."
            : $"Latest playback evidence: {FirstNonEmpty(latestDiagnostic.SuspectedCause, latestDiagnostic.Summary, latestDiagnostic.Decision)}";
        return $"{fallback} Current traits: {container.ToUpperInvariant()} · {video} video · {audio} audio. {diagnostic} Preferred target: {MediaProfileSettings.BuildSummary(settings)}";
    }

    private static string BuildUserDecisionSummary(IReadOnlyList<MediaCompatibilityComparisonRowResponse> comparisonRows, string followUp)
    {
        var mismatches = comparisonRows
            .Where(row => string.Equals(row.Status, "Outside profile", StringComparison.OrdinalIgnoreCase)
                || string.Equals(row.Status, "Disc image", StringComparison.OrdinalIgnoreCase))
            .OrderBy(GetMismatchPriority)
            .Select(BuildUserFacingMismatchPhrase)
            .Where(phrase => !string.IsNullOrWhiteSpace(phrase))
            .Take(4)
            .ToArray();

        if (mismatches.Length == 0)
        {
            return followUp;
        }

        return $"This file differs from the preferred profile because {JoinUserFacingPhrases(mismatches)}. {followUp}";
    }

    private static string BuildUserFacingMismatchPhrase(MediaCompatibilityComparisonRowResponse row)
    {
        var inspected = FormatDecisionDisplayValue(row.Label, row.InspectedValue);
        var selected = FormatDecisionDisplayValue(row.Label, row.SelectedProfileValue);

        if (string.Equals(inspected, selected, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return row.Label switch
        {
            "Container" => $"container is {inspected} instead of {selected}",
            "Video codec" => $"video codec is {inspected} instead of {selected}",
            "Audio" => $"audio is {inspected} instead of {selected}",
            "Subtitles" => $"subtitles are {inspected} instead of {selected}",
            "Resolution" => $"resolution is {inspected} instead of {selected}",
            _ => $"{row.Label.ToLowerInvariant()} is {inspected} instead of {selected}"
        };
    }

    private static string FormatDecisionDisplayValue(string? label, string? value)
    {
        var display = FirstNonEmpty(value, "Unknown");
        return (label ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "container" => display.Trim().ToLowerInvariant() switch
            {
                "matroska,webm" => "MKV",
                "matroska/webm" => "MKV",
                "matroska" => "MKV",
                "mkv" => "MKV",
                "mov,mp4,m4a,3gp,3g2,mj2" => "MP4",
                "mp4" => "MP4",
                "iso" => "ISO",
                _ => display.ToUpperInvariant()
            },
            "video codec" => display.Trim().ToLowerInvariant() switch
            {
                "h264" => "H.264",
                "avc" => "H.264",
                "hevc" => "HEVC",
                "h265" => "H.265",
                _ => display.ToUpperInvariant()
            },
            "subtitles" => NormalizeSubtitleDisplayValue(display),
            _ => display
        };
    }

    private static int GetMismatchPriority(MediaCompatibilityComparisonRowResponse row)
        => row.Label switch
        {
            "Container" => 0,
            "Video codec" => 1,
            "Subtitles" => 2,
            "Audio" => 3,
            "Resolution" => 4,
            "10-bit video" => 5,
            "Bitrate" => 6,
            _ => 10
        };

    private static string JoinUserFacingPhrases(IReadOnlyList<string> phrases)
        => phrases.Count switch
        {
            0 => string.Empty,
            1 => phrases[0],
            2 => $"{phrases[0]}, and {phrases[1]}",
            _ => $"{string.Join(", ", phrases.Take(phrases.Count - 1))}, and {phrases[^1]}"
        };

    private static string BuildCurrentFileRiskLabel(bool recentTranscode, string? playabilityScore, bool requiresVideoTranscode, bool prefersTextOnlyButImageSubs, bool discImageSource, bool needsContainerRemux, bool needsAudioConversion)
    {
        if (recentTranscode
            || string.Equals(playabilityScore, "Problematic", StringComparison.OrdinalIgnoreCase)
            || requiresVideoTranscode
            || prefersTextOnlyButImageSubs
            || discImageSource)
        {
            return "High";
        }

        if (string.Equals(playabilityScore, "Risky", StringComparison.OrdinalIgnoreCase)
            || needsContainerRemux
            || needsAudioConversion)
        {
            return "Medium";
        }

        return "Low";
    }

    private static string BuildSafeQueueDecisionSummary(IReadOnlyList<MediaCompatibilityComparisonRowResponse> comparisonRows)
        => BuildUserDecisionSummary(
            comparisonRows,
            "The fix is a lower-risk compatibility change, so converting this file is usually worth it.");

    private static string BuildLeaveAloneDecisionSummary(IReadOnlyList<MediaCompatibilityComparisonRowResponse> comparisonRows)
        => BuildUserDecisionSummary(
            comparisonRows,
            "MediaCloud does not see enough playback risk to justify changing it right now.");

    private static string BuildRequestReplacementDecisionSummary(IReadOnlyList<MediaCompatibilityComparisonRowResponse> comparisonRows)
        => BuildUserDecisionSummary(
            comparisonRows,
            "Fixing it would require a higher-risk conversion, so requesting a better file is usually safer than converting this one.");

    private static string BuildAnalyzeFirstDecisionSummary()
        => "MediaCloud needs a fresh analysis before it can tell you whether to leave this file alone, convert it, or request a better one.";

    private static string BuildSubtitleDecisionSummary(IReadOnlyList<MediaCompatibilityComparisonRowResponse> comparisonRows)
        => BuildUserDecisionSummary(
            comparisonRows,
            "Subtitle cleanup here is manual enough that requesting a better file is usually the cleaner move.");

    private static string BuildCommandPreview(string primaryFilePath, string preferredContainer, string preferredAudioCodec, bool needsAudioConversion)
    {
        if (string.IsNullOrWhiteSpace(primaryFilePath))
        {
            return string.Empty;
        }

        var outputExtension = preferredContainer == "mkv" ? ".compat.mkv" : ".compat.mp4";
        var outputPath = $"{primaryFilePath}{outputExtension}";
        var subtitleCodec = preferredContainer == "mp4" ? "mov_text" : "copy";
        var audioCodec = needsAudioConversion ? preferredAudioCodec : "copy";
        return $"ffmpeg -y -i {QuoteShellArgument(primaryFilePath)} -map 0:v:0 -map 0:a? -map 0:s? -c:v copy -c:a {audioCodec} -c:s {subtitleCodec} {QuoteShellArgument(outputPath)}";
    }

    private static string BuildManualCommandPreview(string primaryFilePath, string preferredContainer, string preferredAudioCodec, bool includeSubtitleTextConversion)
    {
        if (string.IsNullOrWhiteSpace(primaryFilePath))
        {
            return string.Empty;
        }

        var outputExtension = preferredContainer == "mkv" ? ".manual-review.mkv" : ".manual-review.mp4";
        var outputPath = $"{primaryFilePath}{outputExtension}";
        var subtitleCodec = includeSubtitleTextConversion && preferredContainer == "mp4" ? "mov_text" : "copy";
        return $"ffmpeg -i {QuoteShellArgument(primaryFilePath)} -map 0:v:0 -map 0:a? -map 0:s? -c:v libx264 -preset slow -crf 18 -c:a {preferredAudioCodec} -c:s {subtitleCodec} {QuoteShellArgument(outputPath)}";
    }

    private static string BuildManualSubtitleCommandPreview(string primaryFilePath)
    {
        if (string.IsNullOrWhiteSpace(primaryFilePath))
        {
            return string.Empty;
        }

        return $"ffmpeg -i {QuoteShellArgument(primaryFilePath)} -map 0:s:0 {QuoteShellArgument($"{primaryFilePath}.manual-review.sup")}";
    }

    private static string QuoteShellArgument(string value)
        => $"\"{(value ?? string.Empty).Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static IReadOnlyList<string> BuildBlockedReasons(IReadOnlyList<MediaCompatibilityComparisonRowResponse> comparisonRows)
        => comparisonRows
            .Where(row => string.Equals(row.Status, "Outside profile", StringComparison.OrdinalIgnoreCase)
                || string.Equals(row.Status, "Disc image", StringComparison.OrdinalIgnoreCase))
            .Select(row => $"{row.Label}: current {row.InspectedValue}; target {row.SelectedProfileValue}.")
            .ToArray();

    private static IReadOnlyList<string> BuildManualPlanSteps(string firstStep, string secondStep, string commandPreview)
    {
        var steps = new List<string>
        {
            firstStep,
            secondStep
        };

        if (!string.IsNullOrWhiteSpace(commandPreview))
        {
            steps.Add($"Draft ffmpeg plan: {commandPreview}");
        }

        return steps;
    }

    private static bool ExceedsResolution(MediaPlayabilityStoredDetails details, string maxPreferredResolution)
    {
        var height = details.Height ?? 0;
        var normalized = Normalize(maxPreferredResolution);
        return normalized switch
        {
            "1080p" => height > 1080,
            "4k" => height > 2160,
            _ => false
        };
    }

    private static IReadOnlyList<MediaCompatibilityComparisonRowResponse> BuildComparisonRows(
        LibraryItem item,
        MediaPlayabilityStoredDetails details,
        MediaProfileSettingsResponse settings)
    {
        var container = Normalize(details.ContainerNames.FirstOrDefault());
        var discImageSource = IsDiscImageSource(item.PrimaryFilePath, container);
        var displayContainer = discImageSource
            ? FirstNonEmpty(Path.GetExtension(item.PrimaryFilePath).TrimStart('.'), container, "iso")
            : container;
        var videoCodec = Normalize(details.VideoCodec);
        var audioCodecs = details.AudioCodecs.Select(Normalize).Where(x => x.Length > 0).Distinct().ToArray();
        var subtitleCodecs = details.SubtitleCodecs.Select(Normalize).Where(x => x.Length > 0).Distinct().ToArray();
        var imageSubtitlePresent = subtitleCodecs.Any(IsImageSubtitle);
        var isTenBit = IsTenBit(details);
        var bitrateMbps = details.BitrateBitsPerSecond.HasValue && details.BitrateBitsPerSecond.Value > 0
            ? details.BitrateBitsPerSecond.Value / 1_000_000d
            : (double?)null;

        return
        [
            new(
                "Container",
                FormatValue(displayContainer, value => value.ToUpperInvariant()),
                settings.PreferredContainer.ToUpperInvariant(),
                EvaluateContainer(displayContainer, settings.PreferredContainer, discImageSource)),
            new(
                "Video codec",
                FormatValue(videoCodec, value => value.ToUpperInvariant()),
                settings.AllowHevc
                    ? $"{settings.PreferredVideoCodec.ToUpperInvariant()} (HEVC allowed)"
                    : settings.PreferredVideoCodec.ToUpperInvariant(),
                EvaluateVideoCodec(videoCodec, settings)),
            new(
                "Resolution",
                FormatResolution(details.Width, details.Height),
                $"{settings.MaxPreferredResolution.ToUpperInvariant()} max",
                EvaluateResolution(details, settings.MaxPreferredResolution)),
            new(
                "10-bit video",
                isTenBit ? "Yes" : "No",
                settings.Allow10BitVideo ? "Allowed" : "Avoid",
                isTenBit && !settings.Allow10BitVideo ? "Outside profile" : "Allowed"),
            new(
                "Audio",
                audioCodecs.Length == 0 ? "Unknown" : string.Join(", ", audioCodecs.Select(x => x.ToUpperInvariant())),
                settings.PreferredAudioCodec.ToUpperInvariant(),
                EvaluateAudio(audioCodecs, settings.PreferredAudioCodec)),
            new(
                "Subtitles",
                subtitleCodecs.Length == 0
                    ? "None detected"
                    : string.Join(", ", subtitleCodecs.Select(NormalizeSubtitleDisplayValue)),
                settings.PreferTextSubtitlesOnly
                    ? "Text subtitles only"
                    : settings.AllowImageBasedSubtitles ? "Image subtitles allowed" : "Text preferred",
                EvaluateSubtitles(subtitleCodecs, imageSubtitlePresent, settings)),
            new(
                "Bitrate",
                bitrateMbps.HasValue ? $"{bitrateMbps.Value:0.#} Mbps" : "Unknown",
                $"≤ {settings.MaxPreferredBitrateMbps} Mbps",
                EvaluateBitrate(details.BitrateBitsPerSecond, settings.MaxPreferredBitrateMbps))
        ];
    }

    private static string BuildInputPathFromPreview(string commandPreview)
        => ExtractQuotedSegments(commandPreview).FirstOrDefault() ?? string.Empty;

    private static string BuildOutputPathFromPreview(string commandPreview)
        => ExtractQuotedSegments(commandPreview).LastOrDefault() ?? string.Empty;

    private static IReadOnlyList<string> ExtractQuotedSegments(string commandPreview)
    {
        if (string.IsNullOrWhiteSpace(commandPreview))
        {
            return [];
        }

        return Regex.Matches(commandPreview, "\"([^\"]+)\"")
            .Select(x => x.Groups[1].Value)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();
    }

    private static bool IsTenBit(MediaPlayabilityStoredDetails details)
        => Normalize(details.VideoProfile).Contains("10", StringComparison.Ordinal)
            || Normalize(details.PixelFormat).Contains("10", StringComparison.Ordinal);

    private static string FormatValue(string value, Func<string, string> formatter)
        => string.IsNullOrWhiteSpace(value) ? "Unknown" : formatter(value);

    private static string FormatResolution(int? width, int? height)
        => width.HasValue && height.HasValue ? $"{width.Value}×{height.Value}" : "Unknown";

    private static string EvaluateExactMatch(string inspectedValue, string selectedValue)
        => string.IsNullOrWhiteSpace(inspectedValue)
            ? "Unknown"
            : string.Equals(inspectedValue, Normalize(selectedValue), StringComparison.Ordinal)
                ? "Match"
                : "Outside profile";

    private static string EvaluateContainer(string inspectedValue, string selectedValue, bool discImageSource)
        => discImageSource
            ? "Disc image"
            : EvaluateExactMatch(inspectedValue, selectedValue);

    private static string EvaluateVideoCodec(string videoCodec, MediaProfileSettingsResponse settings)
    {
        if (string.IsNullOrWhiteSpace(videoCodec))
        {
            return "Unknown";
        }

        if (videoCodec == Normalize(settings.PreferredVideoCodec))
        {
            return "Match";
        }

        if (settings.AllowHevc && videoCodec is "hevc" or "h265")
        {
            return "Allowed";
        }

        return "Outside profile";
    }

    private static string EvaluateResolution(MediaPlayabilityStoredDetails details, string maxPreferredResolution)
        => !details.Height.HasValue
            ? "Unknown"
            : ExceedsResolution(details, maxPreferredResolution)
                ? "Outside profile"
                : "Allowed";

    private static string EvaluateAudio(IReadOnlyList<string> audioCodecs, string preferredAudioCodec)
    {
        if (audioCodecs.Count == 0)
        {
            return "Unknown";
        }

        var preferred = Normalize(preferredAudioCodec);
        if (audioCodecs.All(codec => codec == preferred))
        {
            return "Match";
        }

        return audioCodecs.All(codec => IsPreferredAudio(codec, preferred)) ? "Allowed" : "Outside profile";
    }

    private static string EvaluateSubtitles(
        IReadOnlyList<string> subtitleCodecs,
        bool imageSubtitlePresent,
        MediaProfileSettingsResponse settings)
    {
        if (subtitleCodecs.Count == 0)
        {
            return "Allowed";
        }

        if (imageSubtitlePresent && settings.PreferTextSubtitlesOnly)
        {
            return "Outside profile";
        }

        if (imageSubtitlePresent && !settings.AllowImageBasedSubtitles)
        {
            return "Outside profile";
        }

        return imageSubtitlePresent ? "Allowed" : "Match";
    }

    private static string EvaluateBitrate(long? bitrateBitsPerSecond, int maxPreferredBitrateMbps)
        => !bitrateBitsPerSecond.HasValue || bitrateBitsPerSecond.Value <= 0
            ? "Unknown"
            : bitrateBitsPerSecond.Value > maxPreferredBitrateMbps * 1_000_000L
                ? "Outside profile"
                : "Allowed";

    private static bool IsPreferredAudio(string codec, string preferredAudioCodec)
        => codec == preferredAudioCodec || codec is "aac" or "ac3" or "eac3";

    private static string NormalizeSubtitleDisplayValue(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "hdmv_pgs_subtitle" => "PGS",
            "pgs" => "PGS",
            "dvd_subtitle" => "VobSub",
            "vobsub" => "VobSub",
            "mov_text" => "Text",
            "text subtitles only" => "Text subtitles only",
            "image subtitles allowed" => "Image subtitles allowed",
            "text preferred" => "Text preferred",
            "none detected" => "None detected",
            _ => value.ToUpperInvariant()
        };

    private static bool IsImageSubtitle(string codec)
        => codec is "hdmv_pgs_subtitle" or "pgs" or "dvd_subtitle" or "vobsub";

    private static bool IsDiscImageSource(string? primaryFilePath, string? container)
    {
        var extension = Path.GetExtension(primaryFilePath ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedContainer = Normalize(container);
        return extension == ".iso" || normalizedContainer is "iso" or "udf";
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;

    private static string Normalize(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant();
}
