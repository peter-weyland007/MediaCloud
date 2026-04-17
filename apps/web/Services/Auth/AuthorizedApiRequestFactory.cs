using System.Net.Http.Json;

namespace web.Services.Auth;

public class AuthorizedApiRequestFactory(AuthTokenStore tokenStore)
{
    public async Task<HttpRequestMessage> CreateAsync(HttpMethod method, string uri, object? body = null)
    {
        var request = new HttpRequestMessage(method, uri);
        var token = await tokenStore.GetTokenAsync();
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }
}
