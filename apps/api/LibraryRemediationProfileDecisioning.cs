using System.Text.Json;
using api.Models;

namespace api;

public static class LibraryRemediationProfileDecisioning
{
    public static LibraryRemediationIntent Apply(LibraryRemediationIntent intent, LibraryItem item)
    {
        if (item is null)
        {
            return intent;
        }

        var issueType = Normalize(intent.IssueType);
        var profile = Normalize(item.QualityProfile);
        var audioLanguages = ParseLanguages(item.AudioLanguagesJson);
        var subtitleLanguages = ParseLanguages(item.SubtitleLanguagesJson);

        if (issueType is "audio_language_mismatch" or "subtitle_language_mismatch" or "wrong_language")
        {
            if (LacksPreferredEnglishTrack(audioLanguages, subtitleLanguages) && LooksLikeGenericProfile(profile))
            {
                return intent with
                {
                    RequestedAction = "manual_review",
                    ShouldSearchNow = false,
                    ShouldBlacklistCurrentRelease = false,
                    NeedsManualReview = true,
                    ProfileDecision = "review_language_profile",
                    ProfileSummary = $"Current quality profile '{DisplayProfile(item.QualityProfile)}' does not look language-specific. Review the acquisition profile/language setup before asking {GetServiceName(item.MediaType)} to search again.",
                    PolicySummary = $"This looks like a language-policy problem, not just a one-off bad file. Review the acquisition profile before queueing another search for {item.Title}."
                };
            }

            return intent with
            {
                ProfileDecision = "current_profile_ok",
                ProfileSummary = "Current acquisition profile already looks language-aware enough for a replacement search."
            };
        }

        if (issueType is "subtitle_missing")
        {
            if (LooksLikeGenericProfile(profile))
            {
                return intent with
                {
                    RequestedAction = "manual_review",
                    ShouldSearchNow = false,
                    ShouldBlacklistCurrentRelease = false,
                    NeedsManualReview = true,
                    ProfileDecision = "review_language_profile",
                    ProfileSummary = $"Current quality profile '{DisplayProfile(item.QualityProfile)}' does not advertise subtitle/language intent. Review profile expectations before searching again.",
                    PolicySummary = $"Missing subtitles are more likely to repeat until the acquisition profile is clarified. Review the profile before queueing another search for {item.Title}."
                };
            }

            return intent with
            {
                ProfileDecision = "current_profile_ok",
                ProfileSummary = "Current acquisition profile does not block subtitle remediation, so a replacement search can proceed."
            };
        }

        if (issueType == "quality_issue")
        {
            if (LooksLikeLowCeilingQualityProfile(profile))
            {
                return intent with
                {
                    RequestedAction = "manual_review",
                    ShouldSearchNow = false,
                    ShouldBlacklistCurrentRelease = false,
                    NeedsManualReview = true,
                    ProfileDecision = "review_quality_profile",
                    ProfileSummary = $"Current quality profile '{DisplayProfile(item.QualityProfile)}' caps upgrades too low. Raise the acquisition profile ceiling before asking {GetServiceName(item.MediaType)} to search again.",
                    PolicySummary = $"This quality issue is likely constrained by the current acquisition profile. Review the quality profile before queueing another search for {item.Title}."
                };
            }

            return intent with
            {
                ProfileDecision = "current_profile_ok",
                ProfileSummary = "Current acquisition profile allows higher-quality replacements, so MediaCloud can queue a search."
            };
        }

        return intent with
        {
            ProfileDecision = string.IsNullOrWhiteSpace(intent.ProfileDecision) ? "current_profile_ok" : intent.ProfileDecision,
            ProfileSummary = string.IsNullOrWhiteSpace(intent.ProfileSummary)
                ? "Current acquisition profile does not block replacement for this issue type."
                : intent.ProfileSummary
        };
    }

    private static string Normalize(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant();

    private static string DisplayProfile(string? value)
        => string.IsNullOrWhiteSpace(value) ? "(unset)" : value.Trim();

    private static string GetServiceName(string? mediaType)
        => string.Equals(mediaType, "movie", StringComparison.OrdinalIgnoreCase) ? "Radarr"
            : string.Equals(mediaType, "episode", StringComparison.OrdinalIgnoreCase) || string.Equals(mediaType, "series", StringComparison.OrdinalIgnoreCase) ? "Sonarr"
            : string.Equals(mediaType, "album", StringComparison.OrdinalIgnoreCase) ? "Lidarr"
            : "the source manager";

    private static bool LooksLikeGenericProfile(string profile)
        => string.IsNullOrWhiteSpace(profile)
           || profile is "any" or "default"
           || (!profile.Contains("english")
               && !profile.Contains("eng")
               && !profile.Contains("multi")
               && !profile.Contains("dub")
               && !profile.Contains("sub")
               && !profile.Contains("dual")
               && !profile.Contains("original"));

    private static bool LooksLikeLowCeilingQualityProfile(string profile)
        => profile.Contains("sd")
           || profile.Contains("dvd")
           || profile.Contains("480")
           || profile.Contains("576")
           || profile.Contains("720")
           || profile.Contains("low")
           || profile.Contains("mobile");

    private static bool LacksPreferredEnglishTrack(IReadOnlyCollection<string> audioLanguages, IReadOnlyCollection<string> subtitleLanguages)
        => !ContainsEnglish(audioLanguages) && !ContainsEnglish(subtitleLanguages);

    private static bool ContainsEnglish(IEnumerable<string> values)
        => values.Any(value => value.Contains("english", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(value, "eng", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(value, "en", StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyCollection<string> ParseLanguages(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var values = JsonSerializer.Deserialize<List<string>>(json);
            return values?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? [];
        }
        catch
        {
            return [];
        }
    }
}
