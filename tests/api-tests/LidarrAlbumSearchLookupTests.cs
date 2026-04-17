using System.Text.Json;
using api;
using api.Models;
using Xunit;

public sealed class LidarrAlbumSearchLookupTests
{
    [Fact]
    public void SelectAlbumId_prefers_exact_title_and_year_match()
    {
        var item = new LibraryItem
        {
            MediaType = "Album",
            Title = "Random Access Memories",
            SortTitle = "Random Access Memories",
            Year = 2013
        };

        using var document = JsonDocument.Parse("""
        [
          { "id": 301, "title": "Homework", "sortTitle": "Homework", "releaseDate": "1997-01-20" },
          { "id": 302, "title": "Random Access Memories", "sortTitle": "Random Access Memories", "releaseDate": "2013-05-17" }
        ]
        """);

        var albumId = LidarrAlbumSearchLookup.SelectAlbumId(item, document.RootElement.EnumerateArray());

        Assert.Equal(302, albumId);
    }

    [Fact]
    public void SelectAlbumId_can_match_album_title_from_artist_prefixed_library_title()
    {
        var item = new LibraryItem
        {
            MediaType = "Album",
            Title = "Daft Punk — Random Access Memories",
            SortTitle = "Daft Punk - Random Access Memories",
            Year = 2013
        };

        using var document = JsonDocument.Parse("""
        [
          { "id": 401, "title": "Random Access Memories", "sortTitle": "Random Access Memories", "releaseDate": "2013-05-17" },
          { "id": 402, "title": "Discovery", "sortTitle": "Discovery", "releaseDate": "2001-03-12" }
        ]
        """);

        var albumId = LidarrAlbumSearchLookup.SelectAlbumId(item, document.RootElement.EnumerateArray());

        Assert.Equal(401, albumId);
    }
}
