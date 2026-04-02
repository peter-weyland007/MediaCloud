using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;

namespace web.Services.Auth;

public class ApiAuthenticationStateProvider(AuthTokenStore tokenStore) : AuthenticationStateProvider
{
    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());
    private ClaimsPrincipal _cachedPrincipal = Anonymous;

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (_cachedPrincipal.Identity?.IsAuthenticated == true)
        {
            return new AuthenticationState(_cachedPrincipal);
        }

        var token = await tokenStore.GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            _cachedPrincipal = Anonymous;
            return new AuthenticationState(Anonymous);
        }

        var principal = BuildPrincipal(token);
        if (principal.Identity?.IsAuthenticated != true)
        {
            await tokenStore.ClearTokenAsync();
            _cachedPrincipal = Anonymous;
            return new AuthenticationState(Anonymous);
        }

        _cachedPrincipal = principal;
        return new AuthenticationState(principal);
    }

    public async Task SignInAsync(string token)
    {
        await tokenStore.SetTokenAsync(token);
        var principal = BuildPrincipal(token);
        _cachedPrincipal = principal;
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(principal)));
    }

    public async Task SignOutAsync()
    {
        await tokenStore.ClearTokenAsync();
        _cachedPrincipal = Anonymous;
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(Anonymous)));
    }

    private static ClaimsPrincipal BuildPrincipal(string token)
    {
        try
        {
            var payload = ParsePayload(token);
            if (payload.Count == 0)
            {
                return Anonymous;
            }

            if (payload.TryGetValue("exp", out var expRaw)
                && long.TryParse(expRaw, out var expUnix)
                && DateTimeOffset.FromUnixTimeSeconds(expUnix) <= DateTimeOffset.UtcNow)
            {
                return Anonymous;
            }

            var claims = new List<Claim>();
            AddClaimIfPresent(claims, ClaimTypes.NameIdentifier, payload, ClaimTypes.NameIdentifier, "sub");
            AddClaimIfPresent(claims, ClaimTypes.Name, payload, ClaimTypes.Name, "unique_name", "name");
            AddClaimIfPresent(claims, ClaimTypes.Role, payload, ClaimTypes.Role, "role");

            foreach (var (key, value) in payload)
            {
                if (claims.All(c => c.Type != key))
                {
                    claims.Add(new Claim(key, value));
                }
            }

            if (claims.Count == 0)
            {
                return Anonymous;
            }

            var identity = new ClaimsIdentity(claims, authenticationType: "jwt", nameType: ClaimTypes.Name, roleType: ClaimTypes.Role);
            return new ClaimsPrincipal(identity);
        }
        catch
        {
            return Anonymous;
        }
    }

    private static Dictionary<string, string> ParsePayload(string token)
    {
        var parts = token.Split('.');
        if (parts.Length < 2)
        {
            return [];
        }

        var payloadBytes = DecodeBase64Url(parts[1]);
        var json = Encoding.UTF8.GetString(payloadBytes);

        var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? [];
        return payload.ToDictionary(
            x => x.Key,
            x => x.Value.ValueKind switch
            {
                JsonValueKind.String => x.Value.GetString() ?? string.Empty,
                _ => x.Value.ToString()
            },
            StringComparer.OrdinalIgnoreCase);
    }

    private static byte[] DecodeBase64Url(string input)
    {
        var normalized = input.Replace('-', '+').Replace('_', '/');
        while (normalized.Length % 4 != 0)
        {
            normalized += "=";
        }

        return Convert.FromBase64String(normalized);
    }

    private static void AddClaimIfPresent(List<Claim> claims, string claimType, Dictionary<string, string> payload, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (payload.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                claims.Add(new Claim(claimType, value));
                return;
            }
        }
    }
}
