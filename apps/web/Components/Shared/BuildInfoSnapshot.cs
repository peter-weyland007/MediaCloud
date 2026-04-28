namespace web.Components.Shared;

public sealed record BuildInfoSnapshot(
    string Application,
    string Environment,
    string GitSha,
    string GitShaShort,
    string ImageTag,
    string BuildTimestampUtc,
    string CompactLabel)
{
    public static BuildInfoSnapshot Create(IConfiguration configuration, IHostEnvironment environment, string application)
    {
        var gitSha = (configuration["BuildInfo:GitSha"] ?? "dev-local").Trim();
        if (string.IsNullOrWhiteSpace(gitSha))
        {
            gitSha = "dev-local";
        }

        var gitShaShort = gitSha.Length > 7 ? gitSha[..7] : gitSha;
        var imageTag = (configuration["BuildInfo:ImageTag"] ?? "dev-local").Trim();
        if (string.IsNullOrWhiteSpace(imageTag))
        {
            imageTag = "dev-local";
        }

        var buildTimestampUtc = (configuration["BuildInfo:BuildTimestampUtc"] ?? "local").Trim();
        if (string.IsNullOrWhiteSpace(buildTimestampUtc))
        {
            buildTimestampUtc = "local";
        }

        var environmentName = string.IsNullOrWhiteSpace(environment.EnvironmentName)
            ? "Unknown"
            : environment.EnvironmentName;

        return new BuildInfoSnapshot(
            application,
            environmentName,
            gitSha,
            gitShaShort,
            imageTag,
            buildTimestampUtc,
            BuildCompactLabel(gitShaShort, imageTag, environmentName, buildTimestampUtc));
    }

    private static string BuildCompactLabel(string gitShaShort, string imageTag, string environmentName, string buildTimestampUtc)
        => string.Join(" · ", new[]
        {
            gitShaShort,
            imageTag,
            environmentName,
            buildTimestampUtc
        }.Where(x => !string.IsNullOrWhiteSpace(x)));
}
