namespace api.Auth;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "MediaCloud";
    public string Audience { get; set; } = "MediaCloud.Client";
    public string SigningKey { get; set; } = "dev-only-change-me-super-long-signing-key-32+";
    public int ExpiresMinutes { get; set; } = 480;
}
