using Xunit;

public sealed class MediaItemSourceLinkContractDeduplicationTests
{
    [Fact]
    public void Shared_media_item_detail_contracts_define_library_item_source_link_dto_once()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var sharedPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Shared/MediaItemDetailContracts.cs"));
        var moviePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/MovieDetails.razor"));
        var musicPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/MusicDetails.razor"));

        var shared = File.ReadAllText(sharedPath);
        var movie = File.ReadAllText(moviePath);
        var music = File.ReadAllText(musicPath);

        Assert.Contains("public record LibraryItemSourceLinkDto(", shared);
        Assert.DoesNotContain("private record LibraryItemSourceLinkDto(", movie);
        Assert.DoesNotContain("private record LibraryItemSourceLinkDto(", music);
    }
}
