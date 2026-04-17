using System.Xml.Linq;

namespace api;

public static class XmlHelpers
{
    public static string? GetAttributeValue(XElement? element, string name)
        => element?.Attribute(name)?.Value;
}
