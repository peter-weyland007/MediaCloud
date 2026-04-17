using api.Data;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api;

public static class LatestPlaybackDiagnostic
{
    public static PlaybackDiagnosticEntry? SelectLatest(IEnumerable<PlaybackDiagnosticEntry> diagnostics)
        => (diagnostics ?? [])
            .OrderByDescending(x => x.OccurredAtUtc)
            .ThenByDescending(x => x.ImportedAtUtc)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();

    public static async Task<PlaybackDiagnosticEntry?> LoadLatestAsync(MediaCloudDbContext db, long libraryItemId)
    {
        var rows = await db.PlaybackDiagnosticEntries
            .Where(x => x.LibraryItemId == libraryItemId)
            .ToListAsync();

        return SelectLatest(rows);
    }
}
