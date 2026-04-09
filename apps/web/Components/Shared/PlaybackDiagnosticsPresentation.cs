namespace web.Components.Shared;

public static class PlaybackDiagnosticsPresentation
{
    public static string BuildClientLabel(PlaybackDiagnosticDto entry)
    {
        var parts = new[] { entry.Player, entry.Product, entry.Platform }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return parts.Length == 0 ? "Unknown client" : string.Join(" · ", parts);
    }

    public static string BuildDecisionLabel(PlaybackDiagnosticDto entry)
    {
        var parts = new[] { entry.TranscodeDecision, entry.VideoDecision, entry.AudioDecision, entry.SubtitleDecision }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        return parts.Length == 0 ? (string.IsNullOrWhiteSpace(entry.Decision) ? "—" : entry.Decision) : string.Join(" / ", parts);
    }

    public static string GetHealthBadgeStyle(string? healthLabel)
    {
        const string baseStyle = "display:inline-block; padding:0.2rem 0.52rem; border-radius:999px; font-size:0.78rem; font-weight:700; letter-spacing:0.01em;";
        return (healthLabel ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "healthy" => baseStyle + "background:rgba(16,185,129,0.18); color:#6ee7b7; border:1px solid rgba(16,185,129,0.45);",
            "investigate" => baseStyle + "background:rgba(234,179,8,0.22); color:#fde68a; border:1px solid rgba(234,179,8,0.65);",
            "error" => baseStyle + "background:rgba(239,68,68,0.22); color:#fecaca; border:1px solid rgba(239,68,68,0.65);",
            _ => baseStyle + "background:rgba(59,130,246,0.18); color:#bfdbfe; border:1px solid rgba(59,130,246,0.45);"
        };
    }
}
