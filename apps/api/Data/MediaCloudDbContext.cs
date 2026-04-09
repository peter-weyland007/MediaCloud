using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Data;

public class MediaCloudDbContext(DbContextOptions<MediaCloudDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<UserAuditLog> UserAuditLogs => Set<UserAuditLog>();
    public DbSet<AppConfigEntry> AppConfigEntries => Set<AppConfigEntry>();
    public DbSet<IntegrationConfig> IntegrationConfigs => Set<IntegrationConfig>();
    public DbSet<IntegrationSyncState> IntegrationSyncStates => Set<IntegrationSyncState>();
    public DbSet<LibraryPathMapping> LibraryPathMappings => Set<LibraryPathMapping>();
    public DbSet<LibraryItem> LibraryItems => Set<LibraryItem>();
    public DbSet<LibraryItemSourceLink> LibraryItemSourceLinks => Set<LibraryItemSourceLink>();
    public DbSet<LibraryIssue> LibraryIssues => Set<LibraryIssue>();
    public DbSet<PlaybackDiagnosticEntry> PlaybackDiagnosticEntries => Set<PlaybackDiagnosticEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Username).IsUnique();
            entity.Property(x => x.Username).HasMaxLength(64).IsRequired();
            entity.Property(x => x.PasswordHash).IsRequired();
            entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(16).IsRequired();
        });

        modelBuilder.Entity<UserAuditLog>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ActorUsername).HasMaxLength(64).IsRequired();
            entity.Property(x => x.TargetUsername).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Action).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Summary).HasMaxLength(256).IsRequired();
            entity.HasIndex(x => x.OccurredAtUtc);
        });

        modelBuilder.Entity<AppConfigEntry>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Key).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Value).HasMaxLength(256).IsRequired();
            entity.HasIndex(x => x.Key).IsUnique();
        });

        modelBuilder.Entity<IntegrationConfig>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ServiceKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.InstanceName).HasMaxLength(64).IsRequired();
            entity.Property(x => x.BaseUrl).HasMaxLength(512).IsRequired();
            entity.Property(x => x.AuthType).HasMaxLength(24).IsRequired();
            entity.Property(x => x.ApiKey).HasMaxLength(512).IsRequired();
            entity.Property(x => x.Username).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Password).HasMaxLength(512).IsRequired();
            entity.HasIndex(x => new { x.ServiceKey, x.InstanceName }).IsUnique();
        });

        modelBuilder.Entity<IntegrationSyncState>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.LastCursor).HasMaxLength(512).IsRequired();
            entity.Property(x => x.LastEtag).HasMaxLength(512).IsRequired();
            entity.Property(x => x.LastError).HasMaxLength(2048).IsRequired();
            entity.HasIndex(x => x.IntegrationId).IsUnique();
        });

        modelBuilder.Entity<LibraryPathMapping>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RemoteRootPath).HasMaxLength(1024).IsRequired();
            entity.Property(x => x.LocalRootPath).HasMaxLength(1024).IsRequired();
            entity.HasIndex(x => x.IntegrationId).IsUnique();
        });

        modelBuilder.Entity<LibraryItem>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CanonicalKey).HasMaxLength(160).IsRequired();
            entity.Property(x => x.MediaType).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(256).IsRequired();
            entity.Property(x => x.SortTitle).HasMaxLength(256).IsRequired();
            entity.Property(x => x.ImdbId).HasMaxLength(32).IsRequired();
            entity.Property(x => x.PlexRatingKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(4000).IsRequired();
            entity.Property(x => x.DescriptionSourceService).HasMaxLength(64).IsRequired();
            entity.Property(x => x.AudioLanguagesJson).HasMaxLength(2048).IsRequired();
            entity.Property(x => x.SubtitleLanguagesJson).HasMaxLength(2048).IsRequired();
            entity.Property(x => x.PlayabilityScore).HasMaxLength(32).IsRequired();
            entity.Property(x => x.PlayabilitySummary).HasMaxLength(512).IsRequired();
            entity.Property(x => x.PlayabilityDetailsJson).HasMaxLength(4096).IsRequired();
            entity.Property(x => x.QualityProfile).HasMaxLength(128).IsRequired();
            entity.HasIndex(x => x.CanonicalKey).IsUnique();
            entity.HasIndex(x => x.MediaType);
        });

        modelBuilder.Entity<LibraryItemSourceLink>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SourceTitle).HasMaxLength(256).IsRequired();
            entity.Property(x => x.SourceSortTitle).HasMaxLength(256).IsRequired();
            entity.Property(x => x.ExternalId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ExternalType).HasMaxLength(32).IsRequired();
            entity.Property(x => x.SourcePayloadHash).HasMaxLength(128).IsRequired();
            entity.HasIndex(x => new { x.LibraryItemId, x.IntegrationId, x.ExternalId }).IsUnique();
            entity.HasIndex(x => x.IntegrationId);
        });

        modelBuilder.Entity<LibraryIssue>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.IssueType).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Severity).HasMaxLength(16).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(16).IsRequired();
            entity.Property(x => x.PolicyVersion).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Summary).HasMaxLength(512).IsRequired();
            entity.Property(x => x.DetailsJson).HasMaxLength(8192).IsRequired();
            entity.Property(x => x.SuggestedAction).HasMaxLength(256).IsRequired();
            entity.HasIndex(x => new { x.LibraryItemId, x.Status });
            entity.HasIndex(x => new { x.IssueType, x.Status });
        });

        modelBuilder.Entity<PlaybackDiagnosticEntry>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SourceService).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ExternalId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.UserName).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ClientName).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Player).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Product).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Platform).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Decision).HasMaxLength(64).IsRequired();
            entity.Property(x => x.TranscodeDecision).HasMaxLength(64).IsRequired();
            entity.Property(x => x.VideoDecision).HasMaxLength(64).IsRequired();
            entity.Property(x => x.AudioDecision).HasMaxLength(64).IsRequired();
            entity.Property(x => x.SubtitleDecision).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Container).HasMaxLength(64).IsRequired();
            entity.Property(x => x.VideoCodec).HasMaxLength(64).IsRequired();
            entity.Property(x => x.AudioCodec).HasMaxLength(64).IsRequired();
            entity.Property(x => x.SubtitleCodec).HasMaxLength(64).IsRequired();
            entity.Property(x => x.QualityProfile).HasMaxLength(128).IsRequired();
            entity.Property(x => x.HealthLabel).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Summary).HasMaxLength(512).IsRequired();
            entity.Property(x => x.SuspectedCause).HasMaxLength(512).IsRequired();
            entity.Property(x => x.ErrorMessage).HasMaxLength(2048).IsRequired();
            entity.Property(x => x.LogSnippet).HasMaxLength(4096).IsRequired();
            entity.Property(x => x.RawPayloadJson).HasMaxLength(16384).IsRequired();
            entity.HasIndex(x => new { x.LibraryItemId, x.OccurredAtUtc });
            entity.HasIndex(x => new { x.LibraryItemId, x.SourceService, x.ExternalId }).IsUnique();
        });
    }
}
