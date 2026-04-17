namespace api;

public static class TelevisionDuplicateKeys
{
    public static IReadOnlyList<string> BuildTitleYearAliases(string? title, string? sortTitle, int? year)
    {
        if (!year.HasValue)
        {
            return [];
        }

        var aliases = new List<string>();
        AddAlias(aliases, title, year.Value);
        AddAlias(aliases, sortTitle, year.Value);
        return aliases;
    }

    private static void AddAlias(ICollection<string> aliases, string? rawTitle, int year)
    {
        var normalized = Normalize(rawTitle);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        var key = $"{normalized}:{year}";
        if (!aliases.Contains(key, StringComparer.Ordinal))
        {
            aliases.Add(key);
        }
    }

    private static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var chars = raw.Trim().ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray();

        return new string(chars);
    }
}
