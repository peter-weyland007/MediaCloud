using Xunit;

public sealed class WebDataProtectionKeysPathSourceTests
{
    [Fact]
    public void Web_program_uses_content_root_dataprotection_keys_for_non_container_local_runs()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var programPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Program.cs"));
        var content = File.ReadAllText(programPath);

        Assert.Contains("StaticWebAssetsLoader.UseStaticWebAssets(builder.Environment, builder.Configuration);", content);
        Assert.Contains("GetDefaultDataProtectionKeysPath(builder.Environment)", content);
        Assert.Contains("static string GetDefaultDataProtectionKeysPath(IHostEnvironment environment)", content);
        Assert.Contains("return IsContainerContentRoot(contentRootPath)", content);
        Assert.Contains("Path.Combine(contentRootPath, \"DataProtectionKeys\")", content);
        Assert.Contains("static bool IsContainerContentRoot(string contentRootPath)", content);
        Assert.Contains("contentRootPath.StartsWith(\"/app/\"", content);
    }
}
