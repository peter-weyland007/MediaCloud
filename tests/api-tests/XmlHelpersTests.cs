using System.Xml.Linq;
using api;
using Xunit;

public sealed class XmlHelpersTests
{
    [Fact]
    public void GetAttributeValue_returns_null_when_element_is_missing()
    {
        Assert.Null(XmlHelpers.GetAttributeValue(element: null, "title"));
    }

    [Fact]
    public void GetAttributeValue_returns_attribute_when_element_exists()
    {
        var element = XElement.Parse("<Player title=\"Samsung TV\" />");

        Assert.Equal("Samsung TV", XmlHelpers.GetAttributeValue(element, "title"));
    }
}
