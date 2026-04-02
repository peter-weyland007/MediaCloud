using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.JSInterop;

namespace web.Services.Auth;

public class AuthTokenStore(ProtectedSessionStorage storage)
{
    private const string TokenKey = "auth.jwt";
    private string? _cachedToken;

    public async Task<string?> GetTokenAsync()
    {
        if (!string.IsNullOrWhiteSpace(_cachedToken))
        {
            return _cachedToken;
        }

        try
        {
            var result = await storage.GetAsync<string>(TokenKey);
            _cachedToken = result.Success ? result.Value : null;
            return _cachedToken;
        }
        catch (InvalidOperationException)
        {
            return _cachedToken;
        }
        catch (JSException)
        {
            return _cachedToken;
        }
    }

    public async Task SetTokenAsync(string token)
    {
        _cachedToken = token;
        await storage.SetAsync(TokenKey, token);
    }

    public async Task ClearTokenAsync()
    {
        _cachedToken = null;

        try
        {
            await storage.DeleteAsync(TokenKey);
        }
        catch (InvalidOperationException)
        {
            // no-op during prerender
        }
        catch (JSException)
        {
            // no-op for disconnected circuits
        }
    }
}
