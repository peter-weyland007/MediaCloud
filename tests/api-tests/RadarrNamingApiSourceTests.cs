using Xunit;

public sealed class RadarrNamingApiSourceTests
{
    [Fact]
    public void Program_exposes_radarr_naming_read_and_write_routes()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var programPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var content = File.ReadAllText(programPath);

        Assert.Contains("app.MapGet(\"/api/integrations/{id:long}/radarr/naming\"", content);
        Assert.Contains("app.MapPut(\"/api/integrations/{id:long}/radarr/naming\"", content);
        Assert.Contains("/api/v3/config/naming", content);
        Assert.Contains("RadarrNamingSettingsResponse", content);
        Assert.Contains("UpdateRadarrNamingSettingsRequest", content);
    }
}
