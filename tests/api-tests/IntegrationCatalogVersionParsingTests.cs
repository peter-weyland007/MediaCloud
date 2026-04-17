using Xunit;

public sealed class IntegrationCatalogVersionParsingTests
{
    [Fact]
    public void TryExtractVersion_reads_arr_version_property()
    {
        var body = """
        {
          "version": "5.7.0.1234"
        }
        """;

        var version = IntegrationCatalog.TryExtractVersion("radarr", body);

        Assert.Equal("5.7.0.1234", version);
    }

    [Fact]
    public void TryExtractVersion_reads_plex_xml_version_attribute()
    {
        var body = """
        <MediaContainer size="0" claimed="1" machineIdentifier="abc" version="1.40.2.8395-c67dce28e" />
        """;

        var version = IntegrationCatalog.TryExtractVersion("plex", body);

        Assert.Equal("1.40.2.8395-c67dce28e", version);
    }

    [Fact]
    public void TryExtractVersion_reads_tautulli_nested_version_property()
    {
        var body = """
        {
          "response": {
            "result": "success",
            "data": {
              "tautulli_version": "2.15.1"
            }
          }
        }
        """;

        var version = IntegrationCatalog.TryExtractVersion("tautulli", body);

        Assert.Equal("2.15.1", version);
    }


    [Fact]
    public void TryExtractVersion_reads_bazarr_nested_version_property()
    {
        var body = """
        {
          "data": {
            "bazarr_version": "1.5.6"
          }
        }
        """;

        var version = IntegrationCatalog.TryExtractVersion("bazarr", body);

        Assert.Equal("1.5.6", version);
    }

    [Fact]
    public void TryExtractLatestReleaseVersion_reads_github_release_tag()
    {
        var body = """
        {
          "tag_name": "v6.1.1.10360"
        }
        """;

        var version = IntegrationCatalog.TryExtractLatestReleaseVersion("radarr", body);

        Assert.Equal("6.1.1.10360", version);
    }

    [Fact]
    public void TryExtractLatestReleaseVersion_reads_plex_download_feed()
    {
        var body = """
        {
          "computer": {
            "Linux": {
              "version": "1.43.1.10611-1e34174b1"
            }
          }
        }
        """;

        var version = IntegrationCatalog.TryExtractLatestReleaseVersion("plex", body);

        Assert.Equal("1.43.1.10611-1e34174b1", version);
    }
}


public sealed class IntegrationCatalogServiceMetadataTests
{
    [Fact]
    public void Supported_services_include_bazarr_with_api_key_only_auth_and_release_feed()
    {
        Assert.Contains(IntegrationCatalog.SupportedServices, service => service.Key == "bazarr" && service.Name == "Bazarr");
        Assert.Equal(new[] { "ApiKey" }, IntegrationCatalog.GetAllowedAuthTypesForService("bazarr"));
        Assert.Equal("https://api.github.com/repos/morpheus65535/bazarr/releases/latest", IntegrationCatalog.GetLatestReleaseRequestUri("bazarr"));
    }
}
