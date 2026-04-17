using web.Components.Shared;
using Xunit;

public sealed class IssuesPagePresentationTests
{
    [Fact]
    public void GetIssueTypeLabel_formats_manual_issue_types_into_friendly_text()
    {
        Assert.Equal("Playback stall", LibraryIssuePresentation.GetIssueTypeLabel("playback_stall"));
        Assert.Equal("Wrong audio language", LibraryIssuePresentation.GetIssueTypeLabel("wrong-audio-language"));
    }

    [Fact]
    public void ResolveJumpPageIndex_clamps_to_valid_range()
    {
        Assert.Equal(0, LibraryIssuePresentation.ResolveJumpPageIndex("1", totalPages: 20));
        Assert.Equal(19, LibraryIssuePresentation.ResolveJumpPageIndex("999", totalPages: 20));
        Assert.Equal(4, LibraryIssuePresentation.ResolveJumpPageIndex("5", totalPages: 20));
    }
}
