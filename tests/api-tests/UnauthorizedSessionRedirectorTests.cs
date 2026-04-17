using web.Services.Auth;
using Xunit;

public sealed class UnauthorizedSessionRedirectorTests
{
    [Fact]
    public void BuildLoginRedirectTarget_preserves_current_relative_path_as_return_url()
    {
        var redirectTarget = UnauthorizedSessionRedirector.BuildLoginRedirectTarget(
            "http://100.75.86.96:5288/media/moviev2/1424?filter=bad");

        Assert.Equal("/login?ReturnUrl=%2Fmedia%2Fmoviev2%2F1424%3Ffilter%3Dbad", redirectTarget);
    }

    [Fact]
    public void BuildLoginRedirectTarget_returns_plain_login_for_login_page()
    {
        var redirectTarget = UnauthorizedSessionRedirector.BuildLoginRedirectTarget(
            "http://100.75.86.96:5288/login?ReturnUrl=%2Fmedia%2Fmoviev2");

        Assert.Equal("/login", redirectTarget);
    }

    [Theory]
    [InlineData("http://127.0.0.1:5299/api/auth/login", false)]
    [InlineData("http://127.0.0.1:5299/api/public/auth-status", false)]
    [InlineData("http://127.0.0.1:5299/api/library/items?skip=0", true)]
    public void ShouldRedirectForUnauthorized_distinguishes_auth_and_protected_endpoints(string requestUri, bool expected)
    {
        var shouldRedirect = UnauthorizedSessionRedirector.ShouldRedirectForUnauthorized(new Uri(requestUri));

        Assert.Equal(expected, shouldRedirect);
    }
}
