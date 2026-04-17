using System.Globalization;
using System.Text.Json;

namespace api;

public static class JsonPropertyHelpers
{
    public static string? GetPropertyString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        return GetValueAsString(value);
    }

    public static string? GetValueAsString(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => value.ToString()
        };
}
