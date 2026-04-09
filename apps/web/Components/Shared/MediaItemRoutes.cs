namespace web.Components.Shared;

public static class MediaItemRoutes
{
    public static string GetDetailsHref(string? mediaType, long itemId)
    {
        var normalized = NormalizeMediaType(mediaType);
        return normalized switch
        {
            "movie" => $"/media/movies/{itemId}",
            "series" or "episode" => $"/media/tv-shows/{itemId}",
            "album" or "track" => $"/media/music/{itemId}",
            _ => $"/media/{itemId}"
        };
    }

    public static string GetFallbackDisplayName(string? mediaType, long itemId)
    {
        var normalized = NormalizeMediaType(mediaType);
        var label = normalized switch
        {
            "movie" => "Movie",
            "series" => "Series",
            "episode" => "Episode",
            "album" => "Album",
            "track" => "Track",
            _ => "Item"
        };

        return $"{label} #{itemId}";
    }

    private static string NormalizeMediaType(string? mediaType)
        => (mediaType ?? string.Empty).Trim().ToLowerInvariant();
}
