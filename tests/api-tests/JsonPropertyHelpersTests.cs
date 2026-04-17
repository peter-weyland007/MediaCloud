using System.Text.Json;
using api;
using Xunit;

public sealed class JsonPropertyHelpersTests
{
    [Fact]
    public void GetPropertyString_returns_numeric_json_values_as_strings()
    {
        using var doc = JsonDocument.Parse("""
        { "row_id": 12345, "friendly_name": "Darkmatter5" }
        """);

        var rowId = JsonPropertyHelpers.GetPropertyString(doc.RootElement, "row_id");
        var friendlyName = JsonPropertyHelpers.GetPropertyString(doc.RootElement, "friendly_name");

        Assert.Equal("12345", rowId);
        Assert.Equal("Darkmatter5", friendlyName);
    }
}
