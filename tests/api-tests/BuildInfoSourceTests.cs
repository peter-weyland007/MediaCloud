using System;
using System.IO;
using Xunit;

public sealed class BuildInfoSourceTests
{
    [Fact]
    public void Dockerfile_and_build_workflow_pass_build_metadata_into_the_runtime_image()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var dockerfilePath = Path.GetFullPath(Path.Combine(repoRoot, "Dockerfile"));
        var workflowPath = Path.GetFullPath(Path.Combine(repoRoot, ".github/workflows/build-and-publish-ghcr.yml"));

        var dockerfile = File.ReadAllText(dockerfilePath);
        var workflow = File.ReadAllText(workflowPath);

        Assert.Contains("ARG GIT_SHA=dev-local", dockerfile);
        Assert.Contains("ARG BUILD_DATE=local", dockerfile);
        Assert.Contains("ARG IMAGE_TAG=dev-local", dockerfile);
        Assert.Contains("FROM mcr.microsoft.com/dotnet/aspnet:11.0-preview AS runtime\nWORKDIR /app\n\nARG GIT_SHA=dev-local\nARG BUILD_DATE=local\nARG IMAGE_TAG=dev-local", dockerfile);
        Assert.Contains("ENV BuildInfo__GitSha=$GIT_SHA", dockerfile);
        Assert.Contains("ENV BuildInfo__BuildTimestampUtc=$BUILD_DATE", dockerfile);
        Assert.Contains("ENV BuildInfo__ImageTag=$IMAGE_TAG", dockerfile);

        Assert.Contains("id: build_meta", workflow);
        Assert.Contains("short_sha=", workflow);
        Assert.Contains("build_date=", workflow);
        Assert.Contains("build-args:", workflow);
        Assert.Contains("GIT_SHA=${{ github.sha }}", workflow);
        Assert.Contains("BUILD_DATE=${{ steps.build_meta.outputs.build_date }}", workflow);
        Assert.Contains("IMAGE_TAG=sha-${{ steps.build_meta.outputs.short_sha }}", workflow);
    }

    [Fact]
    public void Api_and_web_surface_public_build_info_and_render_visible_build_stamps()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var apiProgramPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var mainLayoutPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Layout/MainLayout.razor"));
        var loginPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/Login.razor"));
        var buildStampPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Shared/BuildStamp.razor"));
        var webProgramPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Program.cs"));

        var apiProgram = File.ReadAllText(apiProgramPath);
        var mainLayout = File.ReadAllText(mainLayoutPath);
        var login = File.ReadAllText(loginPath);
        var buildStamp = File.ReadAllText(buildStampPath);
        var webProgram = File.ReadAllText(webProgramPath);

        Assert.Contains("app.MapGet(\"/api/public/build-info\"", apiProgram);
        Assert.Contains("BuildInfoResponse(", apiProgram);
        Assert.Contains("GitShaShort", apiProgram);
        Assert.Contains("ImageTag", apiProgram);
        Assert.Contains("BuildTimestampUtc", apiProgram);

        Assert.Contains("builder.Services.AddSingleton(BuildInfoSnapshot.Create(", webProgram);
        Assert.Contains("<BuildStamp Compact=\"true\" />", mainLayout);
        Assert.DoesNotContain("<div class=\"tag\">MVP</div>", mainLayout);
        Assert.Contains("<BuildStamp />", login);
        Assert.Contains("[Parameter] public bool Compact { get; set; }", buildStamp);
        Assert.Contains("Http.GetFromJsonAsync<BuildInfoSnapshot>(\"/api/public/build-info\")", buildStamp);
        Assert.Contains("Web:", buildStamp);
        Assert.Contains("API:", buildStamp);
        Assert.Contains("Version:", buildStamp);
        Assert.Contains("@($\"Version: {LocalBuildInfo.GitShaShort}/{(_apiBuildInfo?.GitShaShort ?? \"loading\")}\")", buildStamp);
        Assert.Contains("class=\"build-stamp @(Compact ? \"build-stamp--compact\" : string.Empty)\"", buildStamp);
    }
}
