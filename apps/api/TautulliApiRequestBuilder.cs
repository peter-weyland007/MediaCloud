using System.Net.Http.Headers;

namespace api;

public static class TautulliApiRequestBuilder
{
    public static HttpRequestMessage BuildGetRequest(string baseUrl, string? apiKey, string cmd, IDictionary<string, string?> query)
    {
        var parameters = new List<string>
        {
            $"apikey={Uri.EscapeDataString(apiKey ?? string.Empty)}",
            $"cmd={Uri.EscapeDataString(cmd)}"
        };

        foreach (var pair in query)
        {
            if (string.IsNullOrWhiteSpace(pair.Value)) continue;
            parameters.Add($"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}");
        }

        var url = $"{baseUrl.TrimEnd('/')}/api/v2?{string.Join("&", parameters)}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/135.0.0.0 Safari/537.36");
        request.Headers.Accept.ParseAdd("application/json,text/plain,*/*");
        return request;
    }
}
