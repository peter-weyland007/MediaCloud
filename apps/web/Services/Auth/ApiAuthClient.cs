using System.Net.Http.Json;

namespace web.Services.Auth;

public class ApiAuthClient(HttpClient httpClient, ApiAuthenticationStateProvider authStateProvider)
{
    public async Task<(bool Success, string? Error)> RegisterAsync(string username, string password)
    {
        var payload = new { Username = username, Password = password };
        using var response = await httpClient.PostAsJsonAsync("/api/auth/register", payload);

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                return (false, "Username already exists.");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                return (false, "Registration payload is invalid.");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                return (false, "Self-registration is currently disabled. Ask an admin to create your account.");
            }

            return (false, $"Registration failed ({(int)response.StatusCode}).");
        }

        return (true, null);
    }

    public async Task<(bool Success, string? Error)> LoginAsync(string username, string password)
    {
        var payload = new { Username = username, Password = password };
        using var response = await httpClient.PostAsJsonAsync("/api/auth/login", payload);

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return (false, "Invalid username or password.");
            }

            return (false, $"Login failed ({(int)response.StatusCode}).");
        }

        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        if (login is null || string.IsNullOrWhiteSpace(login.Token))
        {
            return (false, "Login response was invalid.");
        }

        await authStateProvider.SignInAsync(login.Token);
        return (true, null);
    }

    public async Task LogoutAsync()
    {
        try
        {
            await httpClient.PostAsync("/api/auth/logout", content: null);
        }
        catch
        {
            // best effort only
        }

        await authStateProvider.SignOutAsync();
    }
}
