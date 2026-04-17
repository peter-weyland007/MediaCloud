using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Components;

namespace web.Services.Auth;

public class BearerTokenHandler(
    AuthTokenStore tokenStore,
    ApiAuthenticationStateProvider authStateProvider,
    NavigationManager navigationManager) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await tokenStore.GetTokenAsync();
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized
            && request.Headers.Authorization is not null
            && UnauthorizedSessionRedirector.ShouldRedirectForUnauthorized(request.RequestUri))
        {
            await authStateProvider.SignOutAsync();
            navigationManager.NavigateTo(
                UnauthorizedSessionRedirector.BuildLoginRedirectTarget(navigationManager.Uri),
                forceLoad: false);
        }

        return response;
    }
}
