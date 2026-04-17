using Xunit;

public sealed class MediaItemDetailPlayabilityBadgeTests
{
    [Fact]
    public void Shared_media_detail_body_uses_playability_badge_styles_matching_movie_table_palette()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var fullPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Shared/MediaItemDetailBody.razor"));
        var content = File.ReadAllText(fullPath);

        Assert.Contains("GetPlayabilityBadgeStyle(Item.PlayabilityScore)", content);
        Assert.Contains("\"good\" => BadgeStyle(\"rgba(59,130,246,0.18)\", \"#bfdbfe\", \"rgba(59,130,246,0.45)\")", content);
        Assert.Contains("\"problematic\" => BadgeStyle(\"rgba(239,68,68,0.22)\", \"#fecaca\", \"rgba(239,68,68,0.65)\")", content);
        Assert.Contains("display:inline-block; padding:0.2rem 0.52rem; border-radius:999px; font-size:0.78rem; font-weight:700;", content);
    }
}
