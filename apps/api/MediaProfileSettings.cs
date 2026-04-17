using System.Globalization;
using System.Text;
using System.Text.Json;
using api.Data;
using Microsoft.EntityFrameworkCore;

public sealed record UpdateMediaProfileSettingsRequest(
    string PreferredContainer,
    string PreferredVideoCodec,
    string MaxPreferredResolution,
    bool AllowHevc,
    bool Allow10BitVideo,
    string PreferredAudioCodec,
    bool AllowImageBasedSubtitles,
    bool PreferTextSubtitlesOnly,
    int MaxPreferredBitrateMbps,
    string ActivePresetKey = "",
    string ActivePresetName = "");

public sealed record MediaProfileSettingsResponse(
    string PreferredContainer,
    string PreferredVideoCodec,
    string MaxPreferredResolution,
    bool AllowHevc,
    bool Allow10BitVideo,
    string PreferredAudioCodec,
    bool AllowImageBasedSubtitles,
    bool PreferTextSubtitlesOnly,
    int MaxPreferredBitrateMbps,
    string ActivePresetKey = "",
    string ActivePresetName = "");

public sealed record MediaProfilePresetDto(
    string Key,
    string Name,
    bool IsBuiltIn,
    MediaProfileSettingsResponse Settings);

public sealed record SaveMediaProfilePresetRequest(string Name, UpdateMediaProfileSettingsRequest Settings);
public sealed record RenameMediaProfilePresetRequest(string Name);

file sealed record StoredMediaProfilePreset(
    string Name,
    string PreferredContainer,
    string PreferredVideoCodec,
    string MaxPreferredResolution,
    bool AllowHevc,
    bool Allow10BitVideo,
    string PreferredAudioCodec,
    bool AllowImageBasedSubtitles,
    bool PreferTextSubtitlesOnly,
    int MaxPreferredBitrateMbps);

public static class MediaProfilePresetCatalog
{
    private const string CustomPresetPrefix = "media_profile.presets.";

    public static MediaProfilePresetDto BroadPlexCompatibility { get; } = new(
        "broad-plex-compatibility",
        "Stable / Broad Compatibility",
        true,
        new MediaProfileSettingsResponse(
            PreferredContainer: "mp4",
            PreferredVideoCodec: "h264",
            MaxPreferredResolution: "1080p",
            AllowHevc: false,
            Allow10BitVideo: false,
            PreferredAudioCodec: "aac",
            AllowImageBasedSubtitles: false,
            PreferTextSubtitlesOnly: true,
            MaxPreferredBitrateMbps: 20,
            ActivePresetKey: "broad-plex-compatibility",
            ActivePresetName: "Stable / Broad Compatibility"));

    public static MediaProfilePresetDto ModernQualityEfficiency { get; } = new(
        "modern-quality-efficiency",
        "Modern / Quality Efficiency",
        true,
        new MediaProfileSettingsResponse(
            PreferredContainer: "mkv",
            PreferredVideoCodec: "hevc",
            MaxPreferredResolution: "4k",
            AllowHevc: true,
            Allow10BitVideo: true,
            PreferredAudioCodec: "eac3",
            AllowImageBasedSubtitles: true,
            PreferTextSubtitlesOnly: false,
            MaxPreferredBitrateMbps: 40,
            ActivePresetKey: "modern-quality-efficiency",
            ActivePresetName: "Modern / Quality Efficiency"));

    public static async Task<IReadOnlyList<MediaProfilePresetDto>> ListAsync(MediaCloudDbContext db)
    {
        var presets = new List<MediaProfilePresetDto>
        {
            BroadPlexCompatibility,
            ModernQualityEfficiency
        };

        var rows = await db.AppConfigEntries
            .Where(x => x.Key.StartsWith(CustomPresetPrefix))
            .OrderBy(x => x.Key)
            .ToListAsync();

        foreach (var row in rows)
        {
            var preset = TryParseCustomPreset(row.Key, row.Value);
            if (preset is not null)
            {
                presets.Add(preset);
            }
        }

        return presets;
    }

    public static async Task<MediaProfilePresetDto?> FindByKeyAsync(MediaCloudDbContext db, string? key)
    {
        var normalizedKey = (key ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            return null;
        }

        if (string.Equals(normalizedKey, BroadPlexCompatibility.Key, StringComparison.OrdinalIgnoreCase))
        {
            return BroadPlexCompatibility;
        }

        if (string.Equals(normalizedKey, ModernQualityEfficiency.Key, StringComparison.OrdinalIgnoreCase))
        {
            return ModernQualityEfficiency;
        }

        var storageKey = BuildStorageKey(normalizedKey);
        var row = await db.AppConfigEntries.FirstOrDefaultAsync(x => x.Key == storageKey);
        return row is null ? null : TryParseCustomPreset(row.Key, row.Value);
    }

    public static async Task<MediaProfilePresetDto> SaveCustomAsync(MediaCloudDbContext db, string name, MediaProfileSettingsResponse settings, DateTimeOffset now)
    {
        var normalizedName = string.IsNullOrWhiteSpace(name) ? "Custom preset" : name.Trim();
        var presetKey = BuildCustomKey(normalizedName);
        await EnsureCustomNameAvailableAsync(db, normalizedName, candidatePresetKey: presetKey);
        var payload = JsonSerializer.Serialize(new StoredMediaProfilePreset(
            normalizedName,
            settings.PreferredContainer,
            settings.PreferredVideoCodec,
            settings.MaxPreferredResolution,
            settings.AllowHevc,
            settings.Allow10BitVideo,
            settings.PreferredAudioCodec,
            settings.AllowImageBasedSubtitles,
            settings.PreferTextSubtitlesOnly,
            settings.MaxPreferredBitrateMbps));

        await MediaProfileSettings.UpsertAsync(db, BuildStorageKey(presetKey), payload, now);
        return new MediaProfilePresetDto(presetKey, normalizedName, false, ApplyIdentity(settings, presetKey, normalizedName));
    }

    public static async Task<MediaProfilePresetDto?> RenameCustomAsync(MediaCloudDbContext db, string key, string newName, DateTimeOffset now)
    {
        var normalizedKey = NormalizePresetKey(key);
        if (string.IsNullOrWhiteSpace(normalizedKey) || !normalizedKey.StartsWith("custom-", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var row = await db.AppConfigEntries.FirstOrDefaultAsync(x => x.Key == BuildStorageKey(normalizedKey));
        if (row is null)
        {
            return null;
        }

        var existing = TryParseCustomPreset(row.Key, row.Value);
        if (existing is null)
        {
            return null;
        }

        var normalizedName = string.IsNullOrWhiteSpace(newName) ? existing.Name : newName.Trim();
        await EnsureCustomNameAvailableAsync(db, normalizedName, normalizedKey);
        row.Value = JsonSerializer.Serialize(new StoredMediaProfilePreset(
            normalizedName,
            existing.Settings.PreferredContainer,
            existing.Settings.PreferredVideoCodec,
            existing.Settings.MaxPreferredResolution,
            existing.Settings.AllowHevc,
            existing.Settings.Allow10BitVideo,
            existing.Settings.PreferredAudioCodec,
            existing.Settings.AllowImageBasedSubtitles,
            existing.Settings.PreferTextSubtitlesOnly,
            existing.Settings.MaxPreferredBitrateMbps));
        row.UpdatedAtUtc = now;

        await MediaProfileSettings.RenameActivePresetIfMatchesAsync(db, normalizedKey, normalizedName, now);
        return new MediaProfilePresetDto(normalizedKey, normalizedName, false, ApplyIdentity(existing.Settings, normalizedKey, normalizedName));
    }

    public static async Task<bool> DeleteCustomAsync(MediaCloudDbContext db, string key, DateTimeOffset now)
    {
        var normalizedKey = NormalizePresetKey(key);
        if (string.IsNullOrWhiteSpace(normalizedKey) || !normalizedKey.StartsWith("custom-", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var row = await db.AppConfigEntries.FirstOrDefaultAsync(x => x.Key == BuildStorageKey(normalizedKey));
        if (row is null)
        {
            return false;
        }

        db.AppConfigEntries.Remove(row);
        await MediaProfileSettings.ClearActivePresetIfMatchesAsync(db, normalizedKey, now);
        return true;
    }

    public static string NormalizePresetKey(string? key)
    {
        var normalized = (key ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        if (string.Equals(normalized, BroadPlexCompatibility.Key, StringComparison.OrdinalIgnoreCase)) return BroadPlexCompatibility.Key;
        if (string.Equals(normalized, ModernQualityEfficiency.Key, StringComparison.OrdinalIgnoreCase)) return ModernQualityEfficiency.Key;
        if (normalized.StartsWith("custom-", StringComparison.OrdinalIgnoreCase)) return normalized.ToLowerInvariant();
        return string.Empty;
    }

    public static MediaProfileSettingsResponse ApplyIdentity(MediaProfileSettingsResponse settings, string presetKey, string presetName)
        => settings with
        {
            ActivePresetKey = presetKey,
            ActivePresetName = presetName
        };

    private static string BuildCustomKey(string name)
    {
        var builder = new StringBuilder();
        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                continue;
            }

            if (builder.Length == 0 || builder[^1] == '-')
            {
                continue;
            }

            builder.Append('-');
        }

        var slug = builder.ToString().Trim('-');
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = "preset";
        }

        return $"custom-{slug}";
    }

    private static string BuildStorageKey(string presetKey)
        => presetKey.StartsWith(CustomPresetPrefix, StringComparison.OrdinalIgnoreCase)
            ? presetKey
            : $"{CustomPresetPrefix}{presetKey}";

    private static async Task EnsureCustomNameAvailableAsync(MediaCloudDbContext db, string candidateName, string? ignorePresetKey = null, string? candidatePresetKey = null)
    {
        var rows = await db.AppConfigEntries
            .Where(x => x.Key.StartsWith(CustomPresetPrefix))
            .ToListAsync();

        foreach (var row in rows)
        {
            var parsed = TryParseCustomPreset(row.Key, row.Value);
            if (parsed is null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(ignorePresetKey) && string.Equals(parsed.Key, ignorePresetKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(parsed.Name.Trim(), candidateName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"A custom media profile preset named '{candidateName.Trim()}' already exists.");
            }

            if (!string.IsNullOrWhiteSpace(candidatePresetKey) && string.Equals(parsed.Key, candidatePresetKey, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"A custom media profile preset with key '{candidatePresetKey}' already exists. Choose a more distinct preset name.");
            }
        }
    }

    private static MediaProfilePresetDto? TryParseCustomPreset(string key, string value)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<StoredMediaProfilePreset>(value);
            if (parsed is null)
            {
                return null;
            }

            var presetKey = key.StartsWith(CustomPresetPrefix, StringComparison.OrdinalIgnoreCase)
                ? key[CustomPresetPrefix.Length..]
                : key;
            var settings = new MediaProfileSettingsResponse(
                parsed.PreferredContainer,
                parsed.PreferredVideoCodec,
                parsed.MaxPreferredResolution,
                parsed.AllowHevc,
                parsed.Allow10BitVideo,
                parsed.PreferredAudioCodec,
                parsed.AllowImageBasedSubtitles,
                parsed.PreferTextSubtitlesOnly,
                parsed.MaxPreferredBitrateMbps,
                presetKey,
                parsed.Name);
            return new MediaProfilePresetDto(presetKey, parsed.Name, false, settings);
        }
        catch
        {
            return null;
        }
    }
}

public static class MediaProfileSettings
{
    public const string PreferredContainerKey = "media_profile.preferred_container";
    public const string PreferredVideoCodecKey = "media_profile.preferred_video_codec";
    public const string MaxPreferredResolutionKey = "media_profile.max_preferred_resolution";
    public const string AllowHevcKey = "media_profile.allow_hevc";
    public const string Allow10BitVideoKey = "media_profile.allow_10bit_video";
    public const string PreferredAudioCodecKey = "media_profile.preferred_audio_codec";
    public const string AllowImageBasedSubtitlesKey = "media_profile.allow_image_based_subtitles";
    public const string PreferTextSubtitlesOnlyKey = "media_profile.prefer_text_subtitles_only";
    public const string MaxPreferredBitrateMbpsKey = "media_profile.max_preferred_bitrate_mbps";
    public const string ActivePresetKeyKey = "media_profile.active_preset_key";
    public const string ActivePresetNameKey = "media_profile.active_preset_name";

    public static async Task<MediaProfileSettingsResponse> LoadCurrentAsync(MediaCloudDbContext db, MediaProfileSettingsResponse defaults)
    {
        var keys = new[]
        {
            PreferredContainerKey,
            PreferredVideoCodecKey,
            MaxPreferredResolutionKey,
            AllowHevcKey,
            Allow10BitVideoKey,
            PreferredAudioCodecKey,
            AllowImageBasedSubtitlesKey,
            PreferTextSubtitlesOnlyKey,
            MaxPreferredBitrateMbpsKey,
            ActivePresetKeyKey,
            ActivePresetNameKey
        };

        var values = await db.AppConfigEntries
            .Where(x => keys.Contains(x.Key))
            .ToDictionaryAsync(x => x.Key, x => x.Value);

        var activePresetKey = MediaProfilePresetCatalog.NormalizePresetKey(ReadOptionalString(values, ActivePresetKeyKey) ?? defaults.ActivePresetKey);
        var activePresetName = ReadOptionalString(values, ActivePresetNameKey) ?? defaults.ActivePresetName;

        return new MediaProfileSettingsResponse(
            ReadString(values, PreferredContainerKey, defaults.PreferredContainer),
            ReadString(values, PreferredVideoCodecKey, defaults.PreferredVideoCodec),
            ReadString(values, MaxPreferredResolutionKey, defaults.MaxPreferredResolution),
            ReadBool(values, AllowHevcKey, defaults.AllowHevc),
            ReadBool(values, Allow10BitVideoKey, defaults.Allow10BitVideo),
            ReadString(values, PreferredAudioCodecKey, defaults.PreferredAudioCodec),
            ReadBool(values, AllowImageBasedSubtitlesKey, defaults.AllowImageBasedSubtitles),
            ReadBool(values, PreferTextSubtitlesOnlyKey, defaults.PreferTextSubtitlesOnly),
            ReadInt(values, MaxPreferredBitrateMbpsKey, defaults.MaxPreferredBitrateMbps),
            activePresetKey,
            activePresetName);
    }

    public static async Task SaveCurrentAsync(MediaCloudDbContext db, MediaProfileSettingsResponse settings, DateTimeOffset now)
    {
        await UpsertAsync(db, PreferredContainerKey, settings.PreferredContainer, now);
        await UpsertAsync(db, PreferredVideoCodecKey, settings.PreferredVideoCodec, now);
        await UpsertAsync(db, MaxPreferredResolutionKey, settings.MaxPreferredResolution, now);
        await UpsertAsync(db, AllowHevcKey, settings.AllowHevc.ToString(), now);
        await UpsertAsync(db, Allow10BitVideoKey, settings.Allow10BitVideo.ToString(), now);
        await UpsertAsync(db, PreferredAudioCodecKey, settings.PreferredAudioCodec, now);
        await UpsertAsync(db, AllowImageBasedSubtitlesKey, settings.AllowImageBasedSubtitles.ToString(), now);
        await UpsertAsync(db, PreferTextSubtitlesOnlyKey, settings.PreferTextSubtitlesOnly.ToString(), now);
        await UpsertAsync(db, MaxPreferredBitrateMbpsKey, settings.MaxPreferredBitrateMbps.ToString(CultureInfo.InvariantCulture), now);
        await UpsertAsync(db, ActivePresetKeyKey, MediaProfilePresetCatalog.NormalizePresetKey(settings.ActivePresetKey), now);
        await UpsertAsync(db, ActivePresetNameKey, settings.ActivePresetName.Trim(), now);
    }

    public static async Task RenameActivePresetIfMatchesAsync(MediaCloudDbContext db, string presetKey, string presetName, DateTimeOffset now)
    {
        var activeKey = await db.AppConfigEntries.FirstOrDefaultAsync(x => x.Key == ActivePresetKeyKey);
        if (activeKey is null || !string.Equals(MediaProfilePresetCatalog.NormalizePresetKey(activeKey.Value), presetKey, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await UpsertAsync(db, ActivePresetNameKey, presetName, now);
    }

    public static async Task ClearActivePresetIfMatchesAsync(MediaCloudDbContext db, string presetKey, DateTimeOffset now)
    {
        var activeKey = await db.AppConfigEntries.FirstOrDefaultAsync(x => x.Key == ActivePresetKeyKey);
        if (activeKey is null || !string.Equals(MediaProfilePresetCatalog.NormalizePresetKey(activeKey.Value), presetKey, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await UpsertAsync(db, ActivePresetKeyKey, string.Empty, now);
        await UpsertAsync(db, ActivePresetNameKey, string.Empty, now);
    }

    public static async Task UpsertAsync(MediaCloudDbContext db, string key, string value, DateTimeOffset now)
    {
        var row = await db.AppConfigEntries.FirstOrDefaultAsync(x => x.Key == key);
        if (row is null)
        {
            db.AppConfigEntries.Add(new api.Models.AppConfigEntry { Key = key, Value = value, UpdatedAtUtc = now });
            return;
        }

        row.Value = value;
        row.UpdatedAtUtc = now;
    }

    public static MediaProfileSettingsResponse NormalizeForSave(UpdateMediaProfileSettingsRequest request)
    {
        var preferredContainer = string.Equals(request.PreferredContainer, "mkv", StringComparison.OrdinalIgnoreCase) ? "mkv" : "mp4";
        var preferredVideoCodec = string.Equals(request.PreferredVideoCodec, "hevc", StringComparison.OrdinalIgnoreCase) ? "hevc" : "h264";
        var maxPreferredResolution = string.Equals(request.MaxPreferredResolution, "4k", StringComparison.OrdinalIgnoreCase) ? "4k" : "1080p";
        var preferredAudioCodec = request.PreferredAudioCodec?.Trim().ToLowerInvariant() switch
        {
            "ac3" => "ac3",
            "eac3" => "eac3",
            _ => "aac"
        };
        var maxPreferredBitrateMbps = Math.Clamp(request.MaxPreferredBitrateMbps, 5, 200);
        var activePresetKey = MediaProfilePresetCatalog.NormalizePresetKey(request.ActivePresetKey);
        var activePresetName = string.IsNullOrWhiteSpace(request.ActivePresetName) ? string.Empty : request.ActivePresetName.Trim();

        return new MediaProfileSettingsResponse(
            preferredContainer,
            preferredVideoCodec,
            maxPreferredResolution,
            request.AllowHevc,
            request.Allow10BitVideo,
            preferredAudioCodec,
            request.AllowImageBasedSubtitles,
            request.PreferTextSubtitlesOnly,
            maxPreferredBitrateMbps,
            activePresetKey,
            activePresetName);
    }

    public static string BuildSummary(MediaProfileSettingsResponse settings)
        => $"Target {settings.PreferredContainer.ToUpperInvariant()} · prefer {settings.PreferredVideoCodec.ToUpperInvariant()} video · {settings.PreferredAudioCodec.ToUpperInvariant()} audio · max {settings.MaxPreferredResolution.ToUpperInvariant()} · HEVC {(settings.AllowHevc ? "allowed" : "off")} · 10-bit {(settings.Allow10BitVideo ? "allowed" : "off")} · {(settings.PreferTextSubtitlesOnly ? "text subtitles preferred" : settings.AllowImageBasedSubtitles ? "image subtitles allowed" : "image subtitles discouraged")}";

    private static string ReadString(IReadOnlyDictionary<string, string> values, string key, string fallback)
        => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : fallback;

    private static string? ReadOptionalString(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) ? value.Trim() : null;

    private static bool ReadBool(IReadOnlyDictionary<string, string> values, string key, bool fallback)
        => values.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : fallback;

    private static int ReadInt(IReadOnlyDictionary<string, string> values, string key, int fallback)
        => values.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
}
