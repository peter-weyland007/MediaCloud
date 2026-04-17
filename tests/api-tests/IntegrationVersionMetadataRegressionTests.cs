using Xunit;

public sealed class IntegrationVersionMetadataRegressionTests
{
    [Fact]
    public void TryExtractVersion_prefers_plex_media_container_version_over_xml_declaration_version()
    {
        var body = """
        <?xml version="1.0" encoding="UTF-8"?>
        <MediaContainer size="0" claimed="1" machineIdentifier="abc" version="1.43.0.7998-c29d4c0c8" />
        """;

        var version = IntegrationCatalog.TryExtractVersion("plex", body);

        Assert.Equal("1.43.0.7998-c29d4c0c8", version);
    }

    [Fact]
    public void Supported_services_include_overseerr_release_feed_for_latest_version_lookup()
    {
        Assert.Equal("https://api.github.com/repos/sct/overseerr/releases/latest", IntegrationCatalog.GetLatestReleaseRequestUri("overseerr"));
    }
}
