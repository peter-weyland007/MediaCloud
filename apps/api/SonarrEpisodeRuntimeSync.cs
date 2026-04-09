public static class SonarrEpisodeRuntimeSync
{
    public static double? ResolveActualRuntimeMinutes(
        bool hasFile,
        string resolvedFilePath,
        string? previousFilePath,
        double? existingActualRuntimeMinutes)
    {
        if (!hasFile)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(resolvedFilePath))
        {
            return null;
        }

        return string.Equals(previousFilePath, resolvedFilePath, StringComparison.Ordinal)
            ? existingActualRuntimeMinutes
            : null;
    }
}
