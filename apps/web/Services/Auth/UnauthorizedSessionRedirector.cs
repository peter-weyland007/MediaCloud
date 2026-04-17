namespace web.Services.Auth;

public static class UnauthorizedSessionRedirector
{
    public static bool ShouldRedirectForUnauthorized(Uri? requestUri)
    {
        if (requestUri is null)
        {
            return false;
        }

        var path = requestUri.AbsolutePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (path.StartsWith("/api/public/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (path.StartsWith("/api/auth/login", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/auth/register", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/auth/logout", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    public static string BuildLoginRedirectTarget(string? currentUri)
    {
        if (!Uri.TryCreate(currentUri, UriKind.Absolute, out var parsedUri))
        {
            return "/login";
        }

        var relativeTarget = parsedUri.PathAndQuery;
        if (string.IsNullOrWhiteSpace(relativeTarget)
            || relativeTarget.Equals("/", StringComparison.Ordinal)
            || relativeTarget.StartsWith("/login", StringComparison.OrdinalIgnoreCase))
        {
            return "/login";
        }

        return $"/login?ReturnUrl={Uri.EscapeDataString(relativeTarget)}";
    }
}
