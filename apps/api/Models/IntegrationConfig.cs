namespace api.Models;

public class IntegrationConfig
{
    public long Id { get; set; }
    public string ServiceKey { get; set; } = string.Empty;
    public string InstanceName { get; set; } = "Default";
    public string BaseUrl { get; set; } = string.Empty;
    public string AuthType { get; set; } = "ApiKey";
    public string ApiKey { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string RemoteRootPath { get; set; } = string.Empty;
    public string LocalRootPath { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
