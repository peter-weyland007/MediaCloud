using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using api;
using api.Auth;
using api.Data;
using api.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", p => p.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());
});

builder.Services.AddDbContext<MediaCloudDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=data/mediacloud.db"));

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddScoped<IJwtTokenFactory, JwtTokenFactory>();
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddHttpClient();

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey));
var allowSelfRegistrationDefault = builder.Configuration.GetValue<bool?>("Auth:AllowSelfRegistration") ?? false;
const string allowSelfRegistrationKey = "auth.allow_self_registration";
const string runtimeToleranceMinutesFloorKey = "runtime_policy.tolerance_minutes_floor";
const string runtimeTolerancePercentKey = "runtime_policy.tolerance_percent";
const string runtimeWarningPercentKey = "runtime_policy.warning_percent";
const string runtimeHighMinutesKey = "runtime_policy.high_minutes";
const string runtimeCriticalPercentKey = "runtime_policy.critical_percent";
const string runtimeCriticalMinutesKey = "runtime_policy.critical_minutes";
const string monitoringManagedByMediaCloudKey = "monitoring.managed_by_mediacloud";
const string monitoringAutoSyncEnabledKey = "monitoring.auto_sync_enabled";
const string tvHideSpecialsByDefaultKey = "tv.hide_specials_by_default";
const string runtimeMismatchIssueType = "runtime_mismatch";
const string runtimeProbeFailureIssueType = "runtime_probe_failed";
const string runtimePolicyVersion = "runtime-v1";
const double runtimeToleranceMinutesFloorDefault = 3d;
const double runtimeTolerancePercentDefault = 5d;
const double runtimeWarningPercentDefault = 10d;
const double runtimeHighMinutesDefault = 12d;
const double runtimeCriticalPercentDefault = 20d;
const double runtimeCriticalMinutesDefault = 25d;

var runtimeReprobeJobs = new ConcurrentDictionary<Guid, RuntimeReprobeJobStatusResponse>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminOnly", p => p.RequireRole("Admin"));

var app = builder.Build();

if (app.Environment.IsDevelopment()) app.MapOpenApi();

app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MediaCloudDbContext>();
    db.Database.EnsureCreated();

    db.Database.ExecuteSqlRaw(@"
CREATE TABLE IF NOT EXISTS UserAuditLogs (
    Id INTEGER NOT NULL CONSTRAINT PK_UserAuditLogs PRIMARY KEY AUTOINCREMENT,
    OccurredAtUtc TEXT NOT NULL,
    ActorUserId TEXT NULL,
    ActorUsername TEXT NOT NULL,
    TargetUserId TEXT NOT NULL,
    TargetUsername TEXT NOT NULL,
    Action TEXT NOT NULL,
    Summary TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_UserAuditLogs_OccurredAtUtc ON UserAuditLogs(OccurredAtUtc);
CREATE TABLE IF NOT EXISTS AppConfigEntries (
    Id INTEGER NOT NULL CONSTRAINT PK_AppConfigEntries PRIMARY KEY AUTOINCREMENT,
    ""Key"" TEXT NOT NULL,
    Value TEXT NOT NULL,
    UpdatedAtUtc TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_AppConfigEntries_Key ON AppConfigEntries(""Key"");
CREATE TABLE IF NOT EXISTS IntegrationConfigs (
    Id INTEGER NOT NULL CONSTRAINT PK_IntegrationConfigs PRIMARY KEY AUTOINCREMENT,
    ServiceKey TEXT NOT NULL,
    InstanceName TEXT NOT NULL,
    BaseUrl TEXT NOT NULL,
    AuthType TEXT NOT NULL,
    ApiKey TEXT NOT NULL,
    Username TEXT NOT NULL,
    Password TEXT NOT NULL,
    RemoteRootPath TEXT NOT NULL DEFAULT '',
    LocalRootPath TEXT NOT NULL DEFAULT '',
    Enabled INTEGER NOT NULL,
    UpdatedAtUtc TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS IntegrationSyncStates (
    Id INTEGER NOT NULL CONSTRAINT PK_IntegrationSyncStates PRIMARY KEY AUTOINCREMENT,
    IntegrationId INTEGER NOT NULL,
    LastAttemptedAtUtc TEXT NULL,
    LastSuccessfulAtUtc TEXT NULL,
    LastCursor TEXT NOT NULL DEFAULT '',
    LastEtag TEXT NOT NULL DEFAULT '',
    LastError TEXT NOT NULL DEFAULT '',
    ConsecutiveFailureCount INTEGER NOT NULL DEFAULT 0,
    UpdatedAtUtc TEXT NOT NULL,
    FOREIGN KEY (IntegrationId) REFERENCES IntegrationConfigs(Id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_IntegrationSyncStates_IntegrationId ON IntegrationSyncStates(IntegrationId);
CREATE TABLE IF NOT EXISTS LibraryPathMappings (
    Id INTEGER NOT NULL CONSTRAINT PK_LibraryPathMappings PRIMARY KEY AUTOINCREMENT,
    IntegrationId INTEGER NOT NULL,
    RemoteRootPath TEXT NOT NULL,
    LocalRootPath TEXT NOT NULL,
    UpdatedAtUtc TEXT NOT NULL,
    FOREIGN KEY (IntegrationId) REFERENCES IntegrationConfigs(Id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_LibraryPathMappings_IntegrationId ON LibraryPathMappings(IntegrationId);
CREATE TABLE IF NOT EXISTS LibraryItems (
    Id INTEGER NOT NULL CONSTRAINT PK_LibraryItems PRIMARY KEY AUTOINCREMENT,
    CanonicalKey TEXT NOT NULL,
    MediaType TEXT NOT NULL,
    Title TEXT NOT NULL,
    SortTitle TEXT NOT NULL DEFAULT '',
    Year INTEGER NULL,
    TmdbId INTEGER NULL,
    TvdbId INTEGER NULL,
    ImdbId TEXT NOT NULL DEFAULT '',
    PlexRatingKey TEXT NOT NULL DEFAULT '',
    RuntimeMinutes REAL NULL,
    ActualRuntimeMinutes REAL NULL,
    PrimaryFilePath TEXT NOT NULL DEFAULT '',
    AudioLanguagesJson TEXT NOT NULL DEFAULT '[]',
    SubtitleLanguagesJson TEXT NOT NULL DEFAULT '[]',
    PlayabilityScore TEXT NOT NULL DEFAULT '',
    PlayabilitySummary TEXT NOT NULL DEFAULT '',
    PlayabilityDetailsJson TEXT NOT NULL DEFAULT '',
    PlayabilityCheckedAtUtc TEXT NULL,
    IsAvailable INTEGER NOT NULL DEFAULT 0,
    QualityProfile TEXT NOT NULL DEFAULT '',
    SourceUpdatedAtUtc TEXT NULL,
    CreatedAtUtc TEXT NOT NULL,
    UpdatedAtUtc TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_LibraryItems_CanonicalKey ON LibraryItems(CanonicalKey);
CREATE INDEX IF NOT EXISTS IX_LibraryItems_MediaType ON LibraryItems(MediaType);
CREATE TABLE IF NOT EXISTS LibraryItemSourceLinks (
    Id INTEGER NOT NULL CONSTRAINT PK_LibraryItemSourceLinks PRIMARY KEY AUTOINCREMENT,
    LibraryItemId INTEGER NOT NULL,
    IntegrationId INTEGER NOT NULL,
    ExternalId TEXT NOT NULL,
    ExternalType TEXT NOT NULL DEFAULT '',
    ExternalUpdatedAtUtc TEXT NULL,
    LastSeenAtUtc TEXT NOT NULL,
    FirstSeenAtUtc TEXT NOT NULL,
    SourcePayloadHash TEXT NOT NULL DEFAULT '',
    IsDeletedAtSource INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (LibraryItemId) REFERENCES LibraryItems(Id) ON DELETE CASCADE,
    FOREIGN KEY (IntegrationId) REFERENCES IntegrationConfigs(Id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_ItemLinks_Item_Integration_External ON LibraryItemSourceLinks(LibraryItemId, IntegrationId, ExternalId);
CREATE INDEX IF NOT EXISTS IX_ItemLinks_IntegrationId ON LibraryItemSourceLinks(IntegrationId);
CREATE TABLE IF NOT EXISTS LibraryIssues (
    Id INTEGER NOT NULL CONSTRAINT PK_LibraryIssues PRIMARY KEY AUTOINCREMENT,
    LibraryItemId INTEGER NOT NULL,
    IssueType TEXT NOT NULL,
    Severity TEXT NOT NULL,
    Status TEXT NOT NULL,
    PolicyVersion TEXT NOT NULL DEFAULT 'v1',
    Summary TEXT NOT NULL,
    DetailsJson TEXT NOT NULL DEFAULT '{{}}',
    SuggestedAction TEXT NOT NULL DEFAULT '',
    FirstDetectedAtUtc TEXT NOT NULL,
    LastDetectedAtUtc TEXT NOT NULL,
    ResolvedAtUtc TEXT NULL,
    FOREIGN KEY (LibraryItemId) REFERENCES LibraryItems(Id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS IX_LibraryIssues_LibraryItemId_Status ON LibraryIssues(LibraryItemId, Status);
CREATE INDEX IF NOT EXISTS IX_LibraryIssues_IssueType_Status ON LibraryIssues(IssueType, Status);
CREATE TABLE IF NOT EXISTS LibraryRemediationJobs (
    Id INTEGER NOT NULL CONSTRAINT PK_LibraryRemediationJobs PRIMARY KEY AUTOINCREMENT,
    LibraryItemId INTEGER NOT NULL,
    LibraryIssueId INTEGER NULL,
    ServiceKey TEXT NOT NULL DEFAULT '',
    ServiceDisplayName TEXT NOT NULL DEFAULT '',
    RequestedAction TEXT NOT NULL DEFAULT '',
    CommandName TEXT NOT NULL DEFAULT '',
    ExternalItemId INTEGER NULL,
    IssueType TEXT NOT NULL DEFAULT '',
    Reason TEXT NOT NULL DEFAULT '',
    Notes TEXT NOT NULL DEFAULT '',
    ReasonCategory TEXT NOT NULL DEFAULT '',
    Confidence TEXT NOT NULL DEFAULT '',
    ShouldSearchNow INTEGER NOT NULL DEFAULT 0,
    ShouldBlacklistCurrentRelease INTEGER NOT NULL DEFAULT 0,
    NeedsManualReview INTEGER NOT NULL DEFAULT 0,
    NotesRecordedOnly INTEGER NOT NULL DEFAULT 1,
    LookedUpRemotely INTEGER NOT NULL DEFAULT 0,
    PolicySummary TEXT NOT NULL DEFAULT '',
    NotesHandling TEXT NOT NULL DEFAULT '',
    ProfileDecision TEXT NOT NULL DEFAULT '',
    ProfileSummary TEXT NOT NULL DEFAULT '',
    Status TEXT NOT NULL DEFAULT '',
    SearchStatus TEXT NOT NULL DEFAULT '',
    BlacklistStatus TEXT NOT NULL DEFAULT '',
    OutcomeSummary TEXT NOT NULL DEFAULT '',
    ResultMessage TEXT NOT NULL DEFAULT '',
    ReleaseSummary TEXT NOT NULL DEFAULT '',
    ReleaseContextJson TEXT NOT NULL DEFAULT '',
    RequestedBy TEXT NOT NULL DEFAULT '',
    RequestedAtUtc TEXT NOT NULL,
    FinishedAtUtc TEXT NULL,
    CreatedAtUtc TEXT NOT NULL,
    UpdatedAtUtc TEXT NOT NULL,
    FOREIGN KEY (LibraryItemId) REFERENCES LibraryItems(Id) ON DELETE CASCADE,
    FOREIGN KEY (LibraryIssueId) REFERENCES LibraryIssues(Id) ON DELETE SET NULL
);
CREATE INDEX IF NOT EXISTS IX_LibraryRemediationJobs_Item_RequestedAtUtc ON LibraryRemediationJobs(LibraryItemId, RequestedAtUtc);
CREATE INDEX IF NOT EXISTS IX_LibraryRemediationJobs_Item_Status ON LibraryRemediationJobs(LibraryItemId, Status);
CREATE TABLE IF NOT EXISTS PlaybackDiagnosticEntries (
    Id INTEGER NOT NULL CONSTRAINT PK_PlaybackDiagnosticEntries PRIMARY KEY AUTOINCREMENT,
    LibraryItemId INTEGER NOT NULL,
    IntegrationId INTEGER NULL,
    SourceService TEXT NOT NULL,
    ExternalId TEXT NOT NULL DEFAULT '',
    OccurredAtUtc TEXT NOT NULL,
    ImportedAtUtc TEXT NOT NULL,
    StartedAtUtc TEXT NULL,
    StoppedAtUtc TEXT NULL,
    UserName TEXT NOT NULL DEFAULT '',
    ClientName TEXT NOT NULL DEFAULT '',
    Player TEXT NOT NULL DEFAULT '',
    Product TEXT NOT NULL DEFAULT '',
    Platform TEXT NOT NULL DEFAULT '',
    Decision TEXT NOT NULL DEFAULT '',
    TranscodeDecision TEXT NOT NULL DEFAULT '',
    VideoDecision TEXT NOT NULL DEFAULT '',
    AudioDecision TEXT NOT NULL DEFAULT '',
    SubtitleDecision TEXT NOT NULL DEFAULT '',
    Container TEXT NOT NULL DEFAULT '',
    VideoCodec TEXT NOT NULL DEFAULT '',
    AudioCodec TEXT NOT NULL DEFAULT '',
    SubtitleCodec TEXT NOT NULL DEFAULT '',
    QualityProfile TEXT NOT NULL DEFAULT '',
    HealthLabel TEXT NOT NULL DEFAULT '',
    Summary TEXT NOT NULL DEFAULT '',
    SuspectedCause TEXT NOT NULL DEFAULT '',
    ErrorMessage TEXT NOT NULL DEFAULT '',
    LogSnippet TEXT NOT NULL DEFAULT '',
    RawPayloadJson TEXT NOT NULL DEFAULT '',
    FOREIGN KEY (LibraryItemId) REFERENCES LibraryItems(Id) ON DELETE CASCADE,
    FOREIGN KEY (IntegrationId) REFERENCES IntegrationConfigs(Id) ON DELETE SET NULL
);
CREATE INDEX IF NOT EXISTS IX_PlaybackDiagnosticEntries_Item_OccurredAtUtc ON PlaybackDiagnosticEntries(LibraryItemId, OccurredAtUtc);
CREATE UNIQUE INDEX IF NOT EXISTS IX_PlaybackDiagnosticEntries_Item_Source_External ON PlaybackDiagnosticEntries(LibraryItemId, SourceService, ExternalId);
");

    EnsureSqliteColumn(db, "IntegrationConfigs", "InstanceName", "TEXT NOT NULL DEFAULT 'Default'");
    EnsureSqliteColumn(db, "IntegrationConfigs", "AuthType", "TEXT NOT NULL DEFAULT 'ApiKey'");
    EnsureSqliteColumn(db, "IntegrationConfigs", "Username", "TEXT NOT NULL DEFAULT ''");
    EnsureSqliteColumn(db, "IntegrationConfigs", "Password", "TEXT NOT NULL DEFAULT ''");
    EnsureSqliteColumn(db, "IntegrationConfigs", "RemoteRootPath", "TEXT NOT NULL DEFAULT ''");
    EnsureSqliteColumn(db, "IntegrationConfigs", "LocalRootPath", "TEXT NOT NULL DEFAULT ''");
    EnsureSqliteColumn(db, "LibraryItems", "Description", "TEXT NOT NULL DEFAULT ''");
    EnsureSqliteColumn(db, "LibraryItems", "DescriptionSourceService", "TEXT NOT NULL DEFAULT ''");
    EnsureSqliteColumn(db, "LibraryItems", "ActualRuntimeMinutes", "REAL NULL");
    EnsureSqliteColumn(db, "LibraryItems", "PrimaryFilePath", "TEXT NOT NULL DEFAULT ''");
    EnsureSqliteColumn(db, "LibraryItems", "PlayabilityScore", "TEXT NOT NULL DEFAULT ''");
    EnsureSqliteColumn(db, "LibraryItems", "PlayabilitySummary", "TEXT NOT NULL DEFAULT ''");
    EnsureSqliteColumn(db, "LibraryItems", "PlayabilityDetailsJson", "TEXT NOT NULL DEFAULT ''");
    EnsureSqliteColumn(db, "LibraryItems", "PlayabilityCheckedAtUtc", "TEXT NULL");
    EnsureSqliteColumn(db, "LibraryItemSourceLinks", "SourceTitle", "TEXT NOT NULL DEFAULT ''");
    EnsureSqliteColumn(db, "LibraryItemSourceLinks", "SourceSortTitle", "TEXT NOT NULL DEFAULT ''");
    EnsureSqliteColumn(db, "LibraryRemediationJobs", "ReleaseSummary", "TEXT NOT NULL DEFAULT ''");
    EnsureSqliteColumn(db, "LibraryRemediationJobs", "ReleaseContextJson", "TEXT NOT NULL DEFAULT ''");
    EnsureSqliteColumn(db, "LibraryRemediationJobs", "ProfileDecision", "TEXT NOT NULL DEFAULT ''");
    EnsureSqliteColumn(db, "LibraryRemediationJobs", "ProfileSummary", "TEXT NOT NULL DEFAULT ''");
    EnsureSqliteColumn(db, "LibraryRemediationJobs", "SearchStatus", "TEXT NOT NULL DEFAULT ''");
    EnsureSqliteColumn(db, "LibraryRemediationJobs", "BlacklistStatus", "TEXT NOT NULL DEFAULT ''");
    EnsureSqliteColumn(db, "LibraryRemediationJobs", "OutcomeSummary", "TEXT NOT NULL DEFAULT ''");
    EnsureSqliteColumn(db, "LibraryRemediationJobs", "LastCheckedAtUtc", "TEXT NULL");

    db.Database.ExecuteSqlRaw(@"INSERT INTO LibraryPathMappings (IntegrationId, RemoteRootPath, LocalRootPath, UpdatedAtUtc)
SELECT c.Id, c.RemoteRootPath, c.LocalRootPath, c.UpdatedAtUtc
FROM IntegrationConfigs c
WHERE c.RemoteRootPath <> '' AND c.LocalRootPath <> ''
  AND NOT EXISTS (SELECT 1 FROM LibraryPathMappings m WHERE m.IntegrationId = c.Id);");

    db.Database.ExecuteSqlRaw("DROP INDEX IF EXISTS IX_IntegrationConfigs_ServiceKey;");
    db.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IF NOT EXISTS IX_IntegrationConfigs_Service_Instance ON IntegrationConfigs(ServiceKey, InstanceName);");

    var registrationSetting = db.AppConfigEntries.FirstOrDefault(x => x.Key == allowSelfRegistrationKey);
    if (registrationSetting is null)
    {
        db.AppConfigEntries.Add(new AppConfigEntry { Key = allowSelfRegistrationKey, Value = allowSelfRegistrationDefault ? "true" : "false" });
        db.SaveChanges();
    }

    if (!db.Users.Any())
    {
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
        var admin = new AppUser { Username = "admin", Role = UserRole.Admin };
        admin.PasswordHash = hasher.HashPassword(admin, "changeme");
        db.Users.Add(admin);
        db.SaveChanges();
    }
}

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", timestampUtc = DateTimeOffset.UtcNow }));

app.MapGet("/api/dashboard/source-truth", async (MediaCloudDbContext db) =>
{
    var integrations = await db.IntegrationConfigs
        .OrderBy(x => x.ServiceKey)
        .ThenBy(x => x.InstanceName)
        .ToListAsync();

    var syncStates = await db.IntegrationSyncStates.ToListAsync();
    var linkContributions = await db.LibraryItemSourceLinks
        .GroupBy(x => x.IntegrationId)
        .Select(g => new { IntegrationId = g.Key, Count = g.Count() })
        .ToDictionaryAsync(x => x.IntegrationId, x => x.Count);

    var entries = integrations.Select(integration =>
    {
        var state = syncStates.FirstOrDefault(x => x.IntegrationId == integration.Id);
        var links = linkContributions.TryGetValue(integration.Id, out var count) ? count : 0;
        var role = GetServiceRoleSummary(integration.ServiceKey);
        return new DashboardSourceTruthIntegrationDto(
            integration.Id,
            integration.ServiceKey,
            IntegrationCatalog.GetName(integration.ServiceKey),
            integration.InstanceName,
            integration.Enabled,
            role,
            links,
            state?.LastAttemptedAtUtc,
            state?.LastSuccessfulAtUtc,
            state?.LastError ?? string.Empty,
            state?.ConsecutiveFailureCount ?? 0);
    }).ToList();

    return Results.Ok(new DashboardSourceTruthResponse(entries));
}).RequireAuthorization();

app.MapPost("/api/reconcile/plex-backfill/preview", async (MediaCloudDbContext db, IHttpClientFactory httpClientFactory, PlexBackfillPreviewRequest? request) =>
{
    var limit = Math.Clamp(request?.Take ?? 100, 1, 1000);
    var includeRequested = request?.IncludeItemsAlreadyRequested ?? false;
    var plan = await BuildPlexBackfillPlanAsync(db, httpClientFactory, limit, includeRequested);
    return Results.Ok(plan);
}).RequireAuthorization("AdminOnly");

app.MapPost("/api/reconcile/plex-backfill/enrich-ids", async (PlexBackfillEnrichRequest? request, MediaCloudDbContext db, IHttpClientFactory httpClientFactory) =>
{
    var take = Math.Clamp(request?.Take ?? 200, 1, 1000);
    var result = await EnrichPlexBackfillIdsAsync(db, httpClientFactory, take);
    return Results.Ok(result);
}).RequireAuthorization("AdminOnly");

app.MapPost("/api/reconcile/plex-backfill/apply", async (PlexBackfillApplyRequest? request, MediaCloudDbContext db, IHttpClientFactory httpClientFactory) =>
{
    var includeRequested = request?.IncludeItemsAlreadyRequested ?? false;
    var createOverseerrRequests = request?.CreateOverseerrRequests ?? false;
    var take = Math.Clamp(request?.Take ?? 200, 1, 1000);

    var plan = await BuildPlexBackfillPlanAsync(db, httpClientFactory, take, includeRequested);
    var candidates = plan.Items.Where(x => x.CanBackfill).ToList();

    var selectedIds = request?.SelectedLibraryItemIds?
        .Where(x => x > 0)
        .Distinct()
        .ToHashSet();

    if (selectedIds is not null)
    {
        candidates = candidates.Where(x => selectedIds.Contains(x.LibraryItemId)).ToList();
    }

    var radarr = await GetEnabledIntegrationByServiceAsync(db, "radarr");
    if (radarr is null)
    {
        return Results.BadRequest(new ErrorResponse("No enabled Radarr integration is configured."));
    }

    IntegrationConfig? overseerr = null;
    if (createOverseerrRequests)
    {
        overseerr = await GetEnabledIntegrationByServiceAsync(db, "overseerr");
        if (overseerr is null)
        {
            return Results.BadRequest(new ErrorResponse("No enabled Overseerr integration is configured for request creation."));
        }
    }

    var appliedRadarr = 0;
    var appliedOverseerr = 0;
    var skipped = 0;
    var errors = new List<string>();

    foreach (var candidate in candidates)
    {
        var addedRadarr = false;
        var addedOverseerr = false;

        if (candidate.MissingRadarr)
        {
            var radarrResult = await EnsureMovieInRadarrAsync(candidate, radarr, httpClientFactory);
            if (radarrResult.Success)
            {
                appliedRadarr += radarrResult.PerformedAction ? 1 : 0;
                addedRadarr = radarrResult.PerformedAction;
            }
            else
            {
                errors.Add($"{candidate.Title} ({candidate.Year}): Radarr → {radarrResult.Message}");
            }
        }

        if (createOverseerrRequests && candidate.MissingOverseerrRequest && !candidate.InOverseerrMedia && overseerr is not null)
        {
            var overseerrResult = await EnsureMovieRequestedInOverseerrAsync(candidate, overseerr, httpClientFactory);
            if (overseerrResult.Success)
            {
                appliedOverseerr += overseerrResult.PerformedAction ? 1 : 0;
                addedOverseerr = overseerrResult.PerformedAction;
            }
            else
            {
                errors.Add($"{candidate.Title} ({candidate.Year}): Overseerr → {overseerrResult.Message}");
            }
        }

        if (!addedRadarr && !addedOverseerr)
        {
            skipped++;
        }
    }

    if (candidates.Count > 0)
    {
        var runtimePolicy = await LoadRuntimePolicyValuesAsync(db,
            runtimeToleranceMinutesFloorKey,
            runtimeTolerancePercentKey,
            runtimeWarningPercentKey,
            runtimeHighMinutesKey,
            runtimeCriticalPercentKey,
            runtimeCriticalMinutesKey,
            runtimeToleranceMinutesFloorDefault,
            runtimeTolerancePercentDefault,
            runtimeWarningPercentDefault,
            runtimeHighMinutesDefault,
            runtimeCriticalPercentDefault,
            runtimeCriticalMinutesDefault);

        var radarrSyncOutcome = await ExecuteIntegrationSyncAsync(radarr, db, httpClientFactory, runtimePolicy, runtimeMismatchIssueType, runtimePolicyVersion);
        if (!radarrSyncOutcome.Success)
        {
            errors.Add($"Post-apply Radarr sync failed: {radarrSyncOutcome.Message}");
        }

        if (createOverseerrRequests && overseerr is not null)
        {
            var overseerrSyncOutcome = await ExecuteIntegrationSyncAsync(overseerr, db, httpClientFactory, runtimePolicy, runtimeMismatchIssueType, runtimePolicyVersion);
            if (!overseerrSyncOutcome.Success)
            {
                errors.Add($"Post-apply Overseerr sync failed: {overseerrSyncOutcome.Message}");
            }
        }

        await ConsolidateMovieDuplicatesAsync(db, DateTimeOffset.UtcNow);
    }

    return Results.Ok(new PlexBackfillApplyResponse(
        plan.TotalPlexMovies,
        plan.MissingRadarrCount,
        plan.MissingOverseerrRequestCount,
        plan.MissingBothCount,
        candidates.Count,
        appliedRadarr,
        appliedOverseerr,
        skipped,
        errors.Take(50).ToList()));
}).RequireAuthorization("AdminOnly");

app.MapPost("/api/auth/login", async (LoginRequest request, MediaCloudDbContext db, IPasswordHasher<AppUser> hasher, IJwtTokenFactory tokenFactory) =>
{
    var username = (request.Username ?? string.Empty).Trim();
    var user = await db.Users.FirstOrDefaultAsync(x => x.Username == username);
    if (user is null) return Results.Unauthorized();

    var result = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
    if (result == PasswordVerificationResult.Failed) return Results.Unauthorized();

    var token = tokenFactory.Create(user);
    return Results.Ok(new LoginResponse(user.Id, user.Username, user.Role.ToString(), token));
});

app.MapGet("/api/auth/me", async (ClaimsPrincipal principal, MediaCloudDbContext db) =>
{
    var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
    if (!Guid.TryParse(sub, out var id)) return Results.Unauthorized();
    var user = await db.Users.FirstOrDefaultAsync(x => x.Id == id);
    if (user is null) return Results.Unauthorized();
    return Results.Ok(new MeResponse(user.Id, user.Username, user.Role.ToString()));
}).RequireAuthorization();

app.MapGet("/api/public/auth-status", async (MediaCloudDbContext db) =>
{
    var enabled = await AppAuthSettings.IsSelfRegistrationAllowedAsync(db, allowSelfRegistrationDefault, allowSelfRegistrationKey);
    return Results.Ok(new AuthSettingsResponse(enabled));
}).AllowAnonymous();

app.MapGet("/api/settings/auth", async (MediaCloudDbContext db) =>
{
    var enabled = await AppAuthSettings.IsSelfRegistrationAllowedAsync(db, allowSelfRegistrationDefault, allowSelfRegistrationKey);
    return Results.Ok(new AuthSettingsResponse(enabled));
}).RequireAuthorization("AdminOnly");

app.MapPut("/api/settings/auth", async (UpdateAuthSettingsRequest request, MediaCloudDbContext db, ClaimsPrincipal principal) =>
{
    var setting = await db.AppConfigEntries.FirstOrDefaultAsync(x => x.Key == allowSelfRegistrationKey);
    if (setting is null)
    {
        setting = new AppConfigEntry { Key = allowSelfRegistrationKey };
        db.AppConfigEntries.Add(setting);
    }

    setting.Value = request.AllowSelfRegistration ? "true" : "false";
    setting.UpdatedAtUtc = DateTimeOffset.UtcNow;

    await WriteAuditAsync(db, principal, "auth_settings", principal.Identity?.Name ?? "admin", $"Set allow_self_registration={request.AllowSelfRegistration}");
    await db.SaveChangesAsync();
    return Results.Ok(new AuthSettingsResponse(request.AllowSelfRegistration));
}).RequireAuthorization("AdminOnly");

app.MapGet("/api/settings/runtime-policy", async (MediaCloudDbContext db) =>
{
    var settings = await RuntimePolicySettings.LoadAsync(db,
        runtimeToleranceMinutesFloorKey,
        runtimeTolerancePercentKey,
        runtimeWarningPercentKey,
        runtimeHighMinutesKey,
        runtimeCriticalPercentKey,
        runtimeCriticalMinutesKey,
        runtimeToleranceMinutesFloorDefault,
        runtimeTolerancePercentDefault,
        runtimeWarningPercentDefault,
        runtimeHighMinutesDefault,
        runtimeCriticalPercentDefault,
        runtimeCriticalMinutesDefault);
    return Results.Ok(settings);
}).RequireAuthorization("AdminOnly");

app.MapPut("/api/settings/runtime-policy", async (UpdateRuntimePolicySettingsRequest request, MediaCloudDbContext db, ClaimsPrincipal principal) =>
{
    if (request.ToleranceMinutesFloor <= 0 || request.TolerancePercent <= 0)
    {
        return Results.BadRequest(new ErrorResponse("Tolerance values must be greater than zero."));
    }

    if (request.WarningPercent < request.TolerancePercent)
    {
        return Results.BadRequest(new ErrorResponse("Warning percent must be >= tolerance percent."));
    }

    if (request.CriticalPercent < request.WarningPercent)
    {
        return Results.BadRequest(new ErrorResponse("Critical percent must be >= warning percent."));
    }

    if (request.CriticalMinutes < request.HighMinutes)
    {
        return Results.BadRequest(new ErrorResponse("Critical minutes must be >= high minutes."));
    }

    var now = DateTimeOffset.UtcNow;
    await RuntimePolicySettings.UpsertAsync(db, runtimeToleranceMinutesFloorKey, request.ToleranceMinutesFloor, now);
    await RuntimePolicySettings.UpsertAsync(db, runtimeTolerancePercentKey, request.TolerancePercent, now);
    await RuntimePolicySettings.UpsertAsync(db, runtimeWarningPercentKey, request.WarningPercent, now);
    await RuntimePolicySettings.UpsertAsync(db, runtimeHighMinutesKey, request.HighMinutes, now);
    await RuntimePolicySettings.UpsertAsync(db, runtimeCriticalPercentKey, request.CriticalPercent, now);
    await RuntimePolicySettings.UpsertAsync(db, runtimeCriticalMinutesKey, request.CriticalMinutes, now);

    await WriteAuditAsync(db, principal, "runtime_policy", principal.Identity?.Name ?? "admin", $"Updated runtime tolerance policy: floor={request.ToleranceMinutesFloor}m tolerance={request.TolerancePercent}% warning={request.WarningPercent}% high={request.HighMinutes}m critical={request.CriticalPercent}%/{request.CriticalMinutes}m");
    await db.SaveChangesAsync();

    return Results.Ok(new RuntimePolicySettingsResponse(
        request.ToleranceMinutesFloor,
        request.TolerancePercent,
        request.WarningPercent,
        request.HighMinutes,
        request.CriticalPercent,
        request.CriticalMinutes));
}).RequireAuthorization("AdminOnly");

app.MapPost("/api/settings/runtime-policy/reset", async (MediaCloudDbContext db, ClaimsPrincipal principal) =>
{
    var now = DateTimeOffset.UtcNow;
    await RuntimePolicySettings.UpsertAsync(db, runtimeToleranceMinutesFloorKey, runtimeToleranceMinutesFloorDefault, now);
    await RuntimePolicySettings.UpsertAsync(db, runtimeTolerancePercentKey, runtimeTolerancePercentDefault, now);
    await RuntimePolicySettings.UpsertAsync(db, runtimeWarningPercentKey, runtimeWarningPercentDefault, now);
    await RuntimePolicySettings.UpsertAsync(db, runtimeHighMinutesKey, runtimeHighMinutesDefault, now);
    await RuntimePolicySettings.UpsertAsync(db, runtimeCriticalPercentKey, runtimeCriticalPercentDefault, now);
    await RuntimePolicySettings.UpsertAsync(db, runtimeCriticalMinutesKey, runtimeCriticalMinutesDefault, now);

    await WriteAuditAsync(db, principal, "runtime_policy", principal.Identity?.Name ?? "admin", "Reset runtime tolerance policy to defaults");
    await db.SaveChangesAsync();

    return Results.Ok(new RuntimePolicySettingsResponse(
        runtimeToleranceMinutesFloorDefault,
        runtimeTolerancePercentDefault,
        runtimeWarningPercentDefault,
        runtimeHighMinutesDefault,
        runtimeCriticalPercentDefault,
        runtimeCriticalMinutesDefault));
}).RequireAuthorization("AdminOnly");

app.MapGet("/api/settings/monitoring", async (MediaCloudDbContext db) =>
{
    var managedByMediaCloud = await GetMonitoringManagedByMediaCloudAsync(db);
    var autoSyncEnabled = await GetMonitoringAutoSyncEnabledAsync(db);
    return Results.Ok(new MonitoringSettingsResponse(managedByMediaCloud, autoSyncEnabled));
}).RequireAuthorization("AdminOnly");

app.MapPut("/api/settings/monitoring", async (UpdateMonitoringSettingsRequest request, MediaCloudDbContext db, ClaimsPrincipal principal) =>
{
    var now = DateTimeOffset.UtcNow;
    await SetMonitoringManagedByMediaCloudAsync(db, request.ManagedByMediaCloud, now);
    await SetMonitoringAutoSyncEnabledAsync(db, request.AutoSyncEnabled, now);

    await WriteAuditAsync(db, principal, "monitoring_settings", principal.Identity?.Name ?? "admin", $"Set managed_by_mediacloud={request.ManagedByMediaCloud}, auto_sync_enabled={request.AutoSyncEnabled}");
    await db.SaveChangesAsync();

    return Results.Ok(new MonitoringSettingsResponse(request.ManagedByMediaCloud, request.AutoSyncEnabled));
}).RequireAuthorization("AdminOnly");

app.MapGet("/api/settings/tv-display", async (MediaCloudDbContext db) =>
{
    var settings = await TvDisplaySettings.LoadAsync(db, tvHideSpecialsByDefaultKey, fallbackHideSpecials: false);
    return Results.Ok(settings);
}).RequireAuthorization();

app.MapPut("/api/settings/tv-display", async (UpdateTvDisplaySettingsRequest request, MediaCloudDbContext db, ClaimsPrincipal principal) =>
{
    var now = DateTimeOffset.UtcNow;
    await TvDisplaySettings.UpsertAsync(db, tvHideSpecialsByDefaultKey, request.HideSpecialsByDefault, now);
    await WriteAuditAsync(db, principal, "tv_display_settings", principal.Identity?.Name ?? "admin", $"Set hide_specials_by_default={request.HideSpecialsByDefault}");
    await db.SaveChangesAsync();

    return Results.Ok(new TvDisplaySettingsResponse(request.HideSpecialsByDefault));
}).RequireAuthorization("AdminOnly");

app.MapPost("/api/auth/register", async (RegisterRequest request, MediaCloudDbContext db, IPasswordHasher<AppUser> hasher) =>
{
    var allow = await AppAuthSettings.IsSelfRegistrationAllowedAsync(db, allowSelfRegistrationDefault, allowSelfRegistrationKey);
    if (!allow) return Results.StatusCode(StatusCodes.Status403Forbidden);

    var username = (request.Username ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(username) || username.Length is < 3 or > 64)
        return Results.BadRequest(new ErrorResponse("Username must be 3-64 characters."));
    if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
        return Results.BadRequest(new ErrorResponse("Password must be at least 8 characters."));
    if (await db.Users.AnyAsync(x => x.Username == username))
        return Results.Conflict(new ErrorResponse("Username already exists."));

    var user = new AppUser { Username = username, Role = UserRole.Viewer };
    user.PasswordHash = hasher.HashPassword(user, request.Password);
    db.Users.Add(user);

    db.UserAuditLogs.Add(new UserAuditLog
    {
        ActorUserId = user.Id,
        ActorUsername = user.Username,
        TargetUserId = user.Id,
        TargetUsername = user.Username,
        Action = "self_register",
        Summary = "Self-registration created viewer account"
    });

    await db.SaveChangesAsync();
    return Results.Created($"/api/users/{user.Id}", new UserSummaryResponse(user.Id, user.Username, user.Role.ToString(), user.CreatedAtUtc));
});

app.MapGet("/api/users", async (MediaCloudDbContext db) =>
{
    var users = await db.Users.OrderBy(x => x.Username)
        .Select(x => new UserSummaryResponse(x.Id, x.Username, x.Role.ToString(), x.CreatedAtUtc))
        .ToListAsync();
    return Results.Ok(users);
}).RequireAuthorization("AdminOnly");

app.MapGet("/api/users/audit", async (MediaCloudDbContext db, int? take) =>
{
    var limit = Math.Clamp(take ?? 50, 1, 200);
    var logs = await db.UserAuditLogs.OrderByDescending(x => x.Id).Take(limit).ToListAsync();
    var entries = logs.Select(x => new UserAuditLogResponse(x.Id, x.OccurredAtUtc, x.ActorUserId, x.ActorUsername, x.TargetUserId, x.TargetUsername, x.Action, x.Summary)).ToList();
    return Results.Ok(entries);
}).RequireAuthorization("AdminOnly");

app.MapPost("/api/users", async (CreateUserRequest request, ClaimsPrincipal principal, MediaCloudDbContext db, IPasswordHasher<AppUser> hasher) =>
{
    var username = (request.Username ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(username) || username.Length is < 3 or > 64)
        return Results.BadRequest(new ErrorResponse("Username must be 3-64 characters."));
    if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
        return Results.BadRequest(new ErrorResponse("Password must be at least 8 characters."));
    if (!Enum.TryParse<UserRole>(request.Role, true, out var role))
        return Results.BadRequest(new ErrorResponse("Role must be Admin, User, or Viewer."));
    if (await db.Users.AnyAsync(x => x.Username == username))
        return Results.Conflict(new ErrorResponse("Username already exists."));

    var user = new AppUser { Username = username, Role = role };
    user.PasswordHash = hasher.HashPassword(user, request.Password);
    db.Users.Add(user);

    await WriteAuditAsync(db, principal, "create_user", user.Username, $"Created user with role {user.Role}", user.Id);
    await db.SaveChangesAsync();
    return Results.Created($"/api/users/{user.Id}", new UserSummaryResponse(user.Id, user.Username, user.Role.ToString(), user.CreatedAtUtc));
}).RequireAuthorization("AdminOnly");

app.MapPut("/api/users/{id:guid}", async (Guid id, UpdateUserRequest request, ClaimsPrincipal principal, MediaCloudDbContext db, IPasswordHasher<AppUser> hasher) =>
{
    var user = await db.Users.FirstOrDefaultAsync(x => x.Id == id);
    if (user is null) return Results.NotFound(new ErrorResponse("User not found."));

    var username = (request.Username ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(username) || username.Length is < 3 or > 64)
        return Results.BadRequest(new ErrorResponse("Username must be 3-64 characters."));
    if (!Enum.TryParse<UserRole>(request.Role, true, out var role))
        return Results.BadRequest(new ErrorResponse("Role must be Admin, User, or Viewer."));
    if (await db.Users.AnyAsync(x => x.Id != id && x.Username == username))
        return Results.Conflict(new ErrorResponse("Username already exists."));

    user.Username = username;
    user.Role = role;
    if (!string.IsNullOrWhiteSpace(request.Password))
    {
        if (request.Password.Length < 8) return Results.BadRequest(new ErrorResponse("Password must be at least 8 characters."));
        user.PasswordHash = hasher.HashPassword(user, request.Password);
    }

    await WriteAuditAsync(db, principal, "update_user", user.Username, $"Updated user with role {user.Role}", user.Id);
    await db.SaveChangesAsync();
    return Results.Ok(new UserSummaryResponse(user.Id, user.Username, user.Role.ToString(), user.CreatedAtUtc));
}).RequireAuthorization("AdminOnly");

app.MapDelete("/api/users/{id:guid}", async (Guid id, ClaimsPrincipal principal, MediaCloudDbContext db) =>
{
    var user = await db.Users.FirstOrDefaultAsync(x => x.Id == id);
    if (user is null) return Results.NotFound(new ErrorResponse("User not found."));

    await WriteAuditAsync(db, principal, "delete_user", user.Username, $"Deleted user with role {user.Role}", user.Id);
    db.Users.Remove(user);
    await db.SaveChangesAsync();
    return Results.Ok(new SuccessResponse(true));
}).RequireAuthorization("AdminOnly");

app.MapGet("/api/integration-services", () =>
{
    var services = IntegrationCatalog.SupportedServices
        .Select(x => new IntegrationServiceResponse(x.Key, x.Name, IntegrationCatalog.ServiceRequiresCredentials(x.Key), IntegrationCatalog.GetAllowedAuthTypesForService(x.Key)))
        .ToList();
    return Results.Ok(services);
}).RequireAuthorization("AdminOnly");

app.MapGet("/api/integrations", async (MediaCloudDbContext db) =>
{
    var rows = await db.IntegrationConfigs.OrderBy(x => x.ServiceKey).ThenBy(x => x.InstanceName).ToListAsync();
    var items = rows.Select(x => new IntegrationInstanceResponse(x.Id, x.ServiceKey, IntegrationCatalog.GetName(x.ServiceKey), x.InstanceName, x.BaseUrl, x.AuthType, x.ApiKey, x.Username, x.Password, x.Enabled, x.UpdatedAtUtc)).ToList();
    return Results.Ok(items);
}).RequireAuthorization("AdminOnly");

app.MapPost("/api/integrations", async (CreateIntegrationInstanceRequest request, MediaCloudDbContext db, ClaimsPrincipal principal) =>
{
    var serviceKey = (request.ServiceKey ?? string.Empty).Trim().ToLowerInvariant();
    if (!IntegrationCatalog.IsSupported(serviceKey)) return Results.BadRequest(new ErrorResponse("Unsupported integration service key."));

    var instanceName = (request.InstanceName ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(instanceName) || instanceName.Length > 64) return Results.BadRequest(new ErrorResponse("Instance name is required (1-64 chars)."));

    if (!IntegrationCatalog.IsSupportedAuthType(request.AuthType)) return Results.BadRequest(new ErrorResponse("Auth type must be None, ApiKey, or Basic."));
    if (!IntegrationCatalog.IsAuthTypeAllowedForService(serviceKey, request.AuthType))
        return Results.BadRequest(new ErrorResponse($"Auth type '{request.AuthType}' is not allowed for service '{serviceKey}'."));

    var authType = IntegrationCatalog.NormalizeAuthType(request.AuthType);
    if (authType == "ApiKey" && string.IsNullOrWhiteSpace(request.ApiKey))
        return Results.BadRequest(new ErrorResponse("API key/token is required for ApiKey auth."));
    if (authType == "Basic" && (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password)))
        return Results.BadRequest(new ErrorResponse("Username and password are required for Basic auth."));

    var baseUrl = (request.BaseUrl ?? string.Empty).Trim();
    if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _)) return Results.BadRequest(new ErrorResponse("BaseUrl must be an absolute URL."));

    if (await db.IntegrationConfigs.AnyAsync(x => x.ServiceKey == serviceKey && x.InstanceName == instanceName))
        return Results.Conflict(new ErrorResponse("An instance with this service + name already exists."));

    var entity = new IntegrationConfig
    {
        ServiceKey = serviceKey,
        InstanceName = instanceName,
        BaseUrl = baseUrl.TrimEnd('/'),
        AuthType = authType,
        ApiKey = (request.ApiKey ?? string.Empty).Trim(),
        Username = (request.Username ?? string.Empty).Trim(),
        Password = request.Password ?? string.Empty,
        Enabled = request.Enabled,
        UpdatedAtUtc = DateTimeOffset.UtcNow
    };

    db.IntegrationConfigs.Add(entity);
    await WriteAuditAsync(db, principal, "integration_config", principal.Identity?.Name ?? "admin", $"Created integration instance '{entity.ServiceKey}:{entity.InstanceName}' (enabled={entity.Enabled}, auth={entity.AuthType})");
    await db.SaveChangesAsync();

    return Results.Ok(new IntegrationInstanceResponse(entity.Id, entity.ServiceKey, IntegrationCatalog.GetName(entity.ServiceKey), entity.InstanceName, entity.BaseUrl, entity.AuthType, entity.ApiKey, entity.Username, entity.Password, entity.Enabled, entity.UpdatedAtUtc));
}).RequireAuthorization("AdminOnly");

app.MapPut("/api/integrations/{id:long}", async (long id, UpdateIntegrationInstanceRequest request, MediaCloudDbContext db, ClaimsPrincipal principal) =>
{
    var entity = await db.IntegrationConfigs.FirstOrDefaultAsync(x => x.Id == id);
    if (entity is null) return Results.NotFound(new ErrorResponse("Integration instance not found."));

    var instanceName = (request.InstanceName ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(instanceName) || instanceName.Length > 64) return Results.BadRequest(new ErrorResponse("Instance name is required (1-64 chars)."));

    if (!IntegrationCatalog.IsSupportedAuthType(request.AuthType)) return Results.BadRequest(new ErrorResponse("Auth type must be None, ApiKey, or Basic."));
    if (!IntegrationCatalog.IsAuthTypeAllowedForService(entity.ServiceKey, request.AuthType))
        return Results.BadRequest(new ErrorResponse($"Auth type '{request.AuthType}' is not allowed for service '{entity.ServiceKey}'."));

    var authType = IntegrationCatalog.NormalizeAuthType(request.AuthType);
    if (authType == "ApiKey" && string.IsNullOrWhiteSpace(request.ApiKey))
        return Results.BadRequest(new ErrorResponse("API key/token is required for ApiKey auth."));
    if (authType == "Basic" && (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password)))
        return Results.BadRequest(new ErrorResponse("Username and password are required for Basic auth."));

    var baseUrl = (request.BaseUrl ?? string.Empty).Trim();
    if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _)) return Results.BadRequest(new ErrorResponse("BaseUrl must be an absolute URL."));

    if (await db.IntegrationConfigs.AnyAsync(x => x.Id != id && x.ServiceKey == entity.ServiceKey && x.InstanceName == instanceName))
        return Results.Conflict(new ErrorResponse("Another instance already uses this name for the selected service."));

    entity.InstanceName = instanceName;
    entity.BaseUrl = baseUrl.TrimEnd('/');
    entity.AuthType = authType;
    entity.ApiKey = (request.ApiKey ?? string.Empty).Trim();
    entity.Username = (request.Username ?? string.Empty).Trim();
    entity.Password = request.Password ?? string.Empty;
    entity.Enabled = request.Enabled;
    entity.UpdatedAtUtc = DateTimeOffset.UtcNow;

    await WriteAuditAsync(db, principal, "integration_config", principal.Identity?.Name ?? "admin", $"Updated integration instance '{entity.ServiceKey}:{entity.InstanceName}' (enabled={entity.Enabled}, auth={entity.AuthType})");
    await db.SaveChangesAsync();

    return Results.Ok(new IntegrationInstanceResponse(entity.Id, entity.ServiceKey, IntegrationCatalog.GetName(entity.ServiceKey), entity.InstanceName, entity.BaseUrl, entity.AuthType, entity.ApiKey, entity.Username, entity.Password, entity.Enabled, entity.UpdatedAtUtc));
}).RequireAuthorization("AdminOnly");

app.MapDelete("/api/integrations/{id:long}", async (long id, MediaCloudDbContext db, ClaimsPrincipal principal) =>
{
    var entity = await db.IntegrationConfigs.FirstOrDefaultAsync(x => x.Id == id);
    if (entity is null) return Results.NotFound(new ErrorResponse("Integration instance not found."));

    await WriteAuditAsync(db, principal, "integration_config", principal.Identity?.Name ?? "admin", $"Deleted integration instance '{entity.ServiceKey}:{entity.InstanceName}'");
    db.IntegrationConfigs.Remove(entity);
    await db.SaveChangesAsync();
    return Results.Ok(new SuccessResponse(true));
}).RequireAuthorization("AdminOnly");

app.MapGet("/api/system/local-directories", (string? path) =>
{
    var requestedPath = string.IsNullOrWhiteSpace(path)
        ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        : path.Trim();

    if (!Path.IsPathRooted(requestedPath))
        return Results.BadRequest(new ErrorResponse("Path must be absolute."));

    if (!Directory.Exists(requestedPath))
        return Results.NotFound(new ErrorResponse("Directory not found."));

    try
    {
        var normalized = Path.GetFullPath(requestedPath);
        var parent = Directory.GetParent(normalized)?.FullName ?? string.Empty;
        var directories = Directory
            .EnumerateDirectories(normalized)
            .Select(x => Path.GetFileName(x) ?? string.Empty)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Results.Ok(new LocalDirectoryBrowseResponse(normalized, parent, directories));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new ErrorResponse($"Failed to browse directory: {ex.Message}"));
    }
}).RequireAuthorization("AdminOnly");

app.MapGet("/api/library-paths/local-browse", (string? path) =>
{
    var requestedPath = string.IsNullOrWhiteSpace(path)
        ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        : path.Trim();

    if (!Path.IsPathRooted(requestedPath))
        return Results.BadRequest(new ErrorResponse("Path must be absolute."));

    if (!Directory.Exists(requestedPath))
        return Results.NotFound(new ErrorResponse("Directory not found."));

    try
    {
        var normalized = Path.GetFullPath(requestedPath);
        var parent = Directory.GetParent(normalized)?.FullName ?? string.Empty;
        var directories = Directory
            .EnumerateDirectories(normalized)
            .Select(x => Path.GetFileName(x) ?? string.Empty)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Results.Ok(new LocalDirectoryBrowseResponse(normalized, parent, directories));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new ErrorResponse($"Failed to browse directory: {ex.Message}"));
    }
}).RequireAuthorization("AdminOnly");

app.MapGet("/api/library-paths/browse-local", (string? path) =>
{
    var requestedPath = string.IsNullOrWhiteSpace(path)
        ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        : path.Trim();

    if (!Path.IsPathRooted(requestedPath))
        return Results.BadRequest(new ErrorResponse("Path must be absolute."));

    if (!Directory.Exists(requestedPath))
        return Results.NotFound(new ErrorResponse("Directory not found."));

    try
    {
        var normalized = Path.GetFullPath(requestedPath);
        var parent = Directory.GetParent(normalized)?.FullName ?? string.Empty;
        var directories = Directory
            .EnumerateDirectories(normalized)
            .Select(x => Path.GetFileName(x) ?? string.Empty)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Results.Ok(new LocalDirectoryBrowseResponse(normalized, parent, directories));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new ErrorResponse($"Failed to browse directory: {ex.Message}"));
    }
}).RequireAuthorization("AdminOnly");

app.MapGet("/api/library-path-mappings", async (MediaCloudDbContext db) =>
{
    var integrations = await db.IntegrationConfigs.ToListAsync();
    var mappings = await db.LibraryPathMappings.OrderBy(x => x.IntegrationId).ToListAsync();

    var response = mappings.Select(m =>
    {
        var integration = integrations.FirstOrDefault(i => i.Id == m.IntegrationId);
        return new LibraryPathMappingResponse(
            m.Id,
            m.IntegrationId,
            integration?.ServiceKey ?? string.Empty,
            integration?.InstanceName ?? string.Empty,
            integration is null ? string.Empty : IntegrationCatalog.GetName(integration.ServiceKey),
            m.RemoteRootPath,
            m.LocalRootPath,
            m.UpdatedAtUtc);
    }).ToList();

    return Results.Ok(response);
}).RequireAuthorization("AdminOnly");

app.MapPost("/api/library-path-mappings", async (CreateLibraryPathMappingRequest request, MediaCloudDbContext db, ClaimsPrincipal principal) =>
{
    var integration = await db.IntegrationConfigs.FirstOrDefaultAsync(x => x.Id == request.IntegrationId);
    if (integration is null) return Results.NotFound(new ErrorResponse("Integration instance not found."));

    var remoteRootPath = (request.RemoteRootPath ?? string.Empty).Trim();
    var localRootPath = (request.LocalRootPath ?? string.Empty).Trim();
    if (!ValidatePathMapping(remoteRootPath, localRootPath, out var pathError))
        return Results.BadRequest(new ErrorResponse(pathError));

    if (await db.LibraryPathMappings.AnyAsync(x => x.IntegrationId == request.IntegrationId))
        return Results.Conflict(new ErrorResponse("A path mapping already exists for this integration."));

    var mapping = new LibraryPathMapping
    {
        IntegrationId = request.IntegrationId,
        RemoteRootPath = remoteRootPath,
        LocalRootPath = localRootPath,
        UpdatedAtUtc = DateTimeOffset.UtcNow
    };

    db.LibraryPathMappings.Add(mapping);
    await WriteAuditAsync(db, principal, "library_path_mapping", principal.Identity?.Name ?? "admin", $"Created path mapping for integration '{integration.ServiceKey}:{integration.InstanceName}'");
    await db.SaveChangesAsync();

    return Results.Ok(new LibraryPathMappingResponse(mapping.Id, mapping.IntegrationId, integration.ServiceKey, integration.InstanceName, IntegrationCatalog.GetName(integration.ServiceKey), mapping.RemoteRootPath, mapping.LocalRootPath, mapping.UpdatedAtUtc));
}).RequireAuthorization("AdminOnly");

app.MapPut("/api/library-path-mappings/{id:long}", async (long id, UpdateLibraryPathMappingRequest request, MediaCloudDbContext db, ClaimsPrincipal principal) =>
{
    var mapping = await db.LibraryPathMappings.FirstOrDefaultAsync(x => x.Id == id);
    if (mapping is null) return Results.NotFound(new ErrorResponse("Library path mapping not found."));

    var integration = await db.IntegrationConfigs.FirstOrDefaultAsync(x => x.Id == mapping.IntegrationId);
    if (integration is null) return Results.NotFound(new ErrorResponse("Integration instance not found."));

    var remoteRootPath = (request.RemoteRootPath ?? string.Empty).Trim();
    var localRootPath = (request.LocalRootPath ?? string.Empty).Trim();
    if (!ValidatePathMapping(remoteRootPath, localRootPath, out var pathError))
        return Results.BadRequest(new ErrorResponse(pathError));

    mapping.RemoteRootPath = remoteRootPath;
    mapping.LocalRootPath = localRootPath;
    mapping.UpdatedAtUtc = DateTimeOffset.UtcNow;

    await WriteAuditAsync(db, principal, "library_path_mapping", principal.Identity?.Name ?? "admin", $"Updated path mapping for integration '{integration.ServiceKey}:{integration.InstanceName}'");
    await db.SaveChangesAsync();

    return Results.Ok(new LibraryPathMappingResponse(mapping.Id, mapping.IntegrationId, integration.ServiceKey, integration.InstanceName, IntegrationCatalog.GetName(integration.ServiceKey), mapping.RemoteRootPath, mapping.LocalRootPath, mapping.UpdatedAtUtc));
}).RequireAuthorization("AdminOnly");

app.MapDelete("/api/library-path-mappings/{id:long}", async (long id, MediaCloudDbContext db, ClaimsPrincipal principal) =>
{
    var mapping = await db.LibraryPathMappings.FirstOrDefaultAsync(x => x.Id == id);
    if (mapping is null) return Results.NotFound(new ErrorResponse("Library path mapping not found."));

    var integration = await db.IntegrationConfigs.FirstOrDefaultAsync(x => x.Id == mapping.IntegrationId);
    db.LibraryPathMappings.Remove(mapping);
    await WriteAuditAsync(db, principal, "library_path_mapping", principal.Identity?.Name ?? "admin", $"Deleted path mapping for integration '{integration?.ServiceKey}:{integration?.InstanceName}'");
    await db.SaveChangesAsync();
    return Results.Ok(new SuccessResponse(true));
}).RequireAuthorization("AdminOnly");

app.MapPost("/api/library-path-mappings/{id:long}/test", async (long id, MediaCloudDbContext db, IHttpClientFactory httpClientFactory) =>
{
    var mapping = await db.LibraryPathMappings.FirstOrDefaultAsync(x => x.Id == id);
    if (mapping is null) return Results.NotFound(new ErrorResponse("Library path mapping not found."));

    var integration = await db.IntegrationConfigs.FirstOrDefaultAsync(x => x.Id == mapping.IntegrationId);
    if (integration is null) return Results.NotFound(new ErrorResponse("Integration instance not found."));

    var localExists = Directory.Exists(mapping.LocalRootPath);
    var discoveredRoots = new List<string>();
    var messages = new List<string>();
    var deepTestAttempted = false;
    var sourceFilePath = string.Empty;
    var resolvedLocalFilePath = string.Empty;
    var resolvedLocalFileExists = false;

    try
    {
        var service = (integration.ServiceKey ?? string.Empty).Trim().ToLowerInvariant();
        if (service is "radarr" or "sonarr" or "lidarr")
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(20);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{integration.BaseUrl.TrimEnd('/')}/api/v3/rootfolder");
            ApplyIntegrationAuthHeaders(integration, request);
            using var response = await client.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var payload = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(payload);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    discoveredRoots = doc.RootElement
                        .EnumerateArray()
                        .Select(x => GetJsonString(x, "path"))
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x!.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
            }
            else
            {
                messages.Add($"Could not read root folders from integration (HTTP {(int)response.StatusCode}).");
            }

            if (service == "radarr")
            {
                using var movieRequest = new HttpRequestMessage(HttpMethod.Get, $"{integration.BaseUrl.TrimEnd('/')}/api/v3/movie?includeMovieFile=true");
                ApplyIntegrationAuthHeaders(integration, movieRequest);
                using var movieResponse = await client.SendAsync(movieRequest);
                if (movieResponse.IsSuccessStatusCode)
                {
                    var moviePayload = await movieResponse.Content.ReadAsStringAsync();
                    using var movieDoc = JsonDocument.Parse(moviePayload);
                    if (movieDoc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        var sampleFilePath = movieDoc.RootElement
                            .EnumerateArray()
                            .Select(x => GetNestedJsonString(x, "movieFile", "path"))
                            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x) && x.Trim().StartsWith(mapping.RemoteRootPath, StringComparison.OrdinalIgnoreCase))
                            ?? movieDoc.RootElement
                                .EnumerateArray()
                                .Select(x => GetNestedJsonString(x, "movieFile", "path"))
                                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

                        if (!string.IsNullOrWhiteSpace(sampleFilePath))
                        {
                            deepTestAttempted = true;
                            sourceFilePath = sampleFilePath.Trim();
                            resolvedLocalFilePath = ResolveLocalPath(mapping, sourceFilePath);
                            resolvedLocalFileExists = File.Exists(resolvedLocalFilePath);
                        }
                        else
                        {
                            messages.Add("Deep test skipped: no Radarr movie file path available.");
                        }
                    }
                    else
                    {
                        messages.Add("Deep test skipped: unexpected Radarr movie payload.");
                    }
                }
                else
                {
                    messages.Add($"Deep test skipped: could not read Radarr movies (HTTP {(int)movieResponse.StatusCode}).");
                }
            }
            else if (service == "sonarr")
            {
                using var seriesRequest = new HttpRequestMessage(HttpMethod.Get, $"{integration.BaseUrl.TrimEnd('/')}/api/v3/series");
                ApplyIntegrationAuthHeaders(integration, seriesRequest);
                using var seriesResponse = await client.SendAsync(seriesRequest);
                if (!seriesResponse.IsSuccessStatusCode)
                {
                    messages.Add($"Deep test skipped: could not read Sonarr series (HTTP {(int)seriesResponse.StatusCode}).");
                }
                else
                {
                    var seriesPayload = await seriesResponse.Content.ReadAsStringAsync();
                    using var seriesDoc = JsonDocument.Parse(seriesPayload);
                    if (seriesDoc.RootElement.ValueKind != JsonValueKind.Array)
                    {
                        messages.Add("Deep test skipped: unexpected Sonarr series payload.");
                    }
                    else
                    {
                        var seriesIds = seriesDoc.RootElement
                            .EnumerateArray()
                            .Select(x => GetJsonInt(x, "id"))
                            .Where(x => x.HasValue)
                            .Select(x => x!.Value)
                            .Distinct()
                            .ToList();

                        var episodeFilesResult = await FetchSonarrCollectionBySeriesAsync(client, integration, "episodefile", seriesIds, "episode files");
                        if (!episodeFilesResult.Success)
                        {
                            messages.Add($"Deep test skipped: {episodeFilesResult.ErrorMessage}");
                        }
                        else
                        {
                            var sampleFilePath = episodeFilesResult.Items
                                .Select(x => GetJsonString(x, "path"))
                                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x) && x.Trim().StartsWith(mapping.RemoteRootPath, StringComparison.OrdinalIgnoreCase))
                                ?? episodeFilesResult.Items
                                    .Select(x => GetJsonString(x, "path"))
                                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

                            if (!string.IsNullOrWhiteSpace(sampleFilePath))
                            {
                                deepTestAttempted = true;
                                sourceFilePath = sampleFilePath.Trim();
                                resolvedLocalFilePath = ResolveLocalPath(mapping, sourceFilePath);
                                resolvedLocalFileExists = File.Exists(resolvedLocalFilePath);
                            }
                            else
                            {
                                messages.Add("Deep test skipped: no Sonarr episode file path available.");
                            }
                        }
                    }
                }
            }
            else if (service == "lidarr")
            {
                string? sampleFilePath = null;
                foreach (var endpoint in new[] { "/api/v1/trackfile", "/api/v1/trackFile" })
                {
                    using var trackFileRequest = new HttpRequestMessage(HttpMethod.Get, $"{integration.BaseUrl.TrimEnd('/')}{endpoint}");
                    ApplyIntegrationAuthHeaders(integration, trackFileRequest);
                    using var trackFileResponse = await client.SendAsync(trackFileRequest);
                    if (!trackFileResponse.IsSuccessStatusCode)
                    {
                        continue;
                    }

                    var trackPayload = await trackFileResponse.Content.ReadAsStringAsync();
                    using var trackDoc = JsonDocument.Parse(trackPayload);
                    if (trackDoc.RootElement.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    sampleFilePath = trackDoc.RootElement
                        .EnumerateArray()
                        .Select(x => GetJsonString(x, "path"))
                        .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x) && x.Trim().StartsWith(mapping.RemoteRootPath, StringComparison.OrdinalIgnoreCase))
                        ?? trackDoc.RootElement
                            .EnumerateArray()
                            .Select(x => GetJsonString(x, "path"))
                            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

                    if (!string.IsNullOrWhiteSpace(sampleFilePath))
                    {
                        break;
                    }
                }

                if (!string.IsNullOrWhiteSpace(sampleFilePath))
                {
                    deepTestAttempted = true;
                    sourceFilePath = sampleFilePath.Trim();
                    resolvedLocalFilePath = ResolveLocalPath(mapping, sourceFilePath);
                    resolvedLocalFileExists = File.Exists(resolvedLocalFilePath);
                }
                else
                {
                    messages.Add("Deep test skipped: no Lidarr track file path available.");
                }
            }
        }
    }
    catch (Exception ex)
    {
        messages.Add($"Remote root discovery failed: {ex.Message}");
    }

    var remoteMatchesDiscovered = discoveredRoots.Count == 0 || discoveredRoots.Any(x => string.Equals(x, mapping.RemoteRootPath, StringComparison.OrdinalIgnoreCase));
    if (!localExists) messages.Add("Local root path does not exist on this host.");
    if (!remoteMatchesDiscovered) messages.Add("Remote root path not found in integration root folders.");
    if (deepTestAttempted && !resolvedLocalFileExists)
    {
        messages.Add("Deep test failed: sample media file did not resolve to an existing local file.");
    }

    var success = localExists && remoteMatchesDiscovered && (!deepTestAttempted || resolvedLocalFileExists);
    var summary = success
        ? (deepTestAttempted ? "Mapping test passed (including deep file-resolution check)." : "Mapping test passed.")
        : (messages.Count > 0 ? string.Join(" ", messages) : "Mapping test failed.");

    return Results.Ok(new LibraryPathMappingTestResponse(
        mapping.Id,
        mapping.IntegrationId,
        integration.ServiceKey ?? string.Empty,
        mapping.RemoteRootPath,
        mapping.LocalRootPath,
        localExists,
        remoteMatchesDiscovered,
        deepTestAttempted,
        sourceFilePath,
        resolvedLocalFilePath,
        resolvedLocalFileExists,
        discoveredRoots,
        success,
        summary));
}).RequireAuthorization("AdminOnly");

app.MapGet("/api/integrations/{id:long}/remote-roots", async (long id, MediaCloudDbContext db, IHttpClientFactory httpClientFactory) =>
{
    var integration = await db.IntegrationConfigs.FirstOrDefaultAsync(x => x.Id == id);
    if (integration is null) return Results.NotFound(new ErrorResponse("Integration instance not found."));

    var serviceKey = integration.ServiceKey ?? string.Empty;
    var service = serviceKey.Trim().ToLowerInvariant();
    if (service is not ("radarr" or "sonarr" or "lidarr"))
    {
        return Results.Ok(new IntegrationRemoteRootsResponse(integration.Id, serviceKey, [], "Remote root discovery is supported for Radarr/Sonarr/Lidarr."));
    }

    try
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        var uri = $"{integration.BaseUrl.TrimEnd('/')}/api/v3/rootfolder";
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        ApplyIntegrationAuthHeaders(integration, request);

        using var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return Results.Ok(new IntegrationRemoteRootsResponse(integration.Id, serviceKey, [], $"Failed to fetch root folders (HTTP {(int)response.StatusCode})."));
        }

        var payload = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(payload);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return Results.Ok(new IntegrationRemoteRootsResponse(integration.Id, serviceKey, [], "Unexpected root folder payload."));
        }

        var roots = doc.RootElement
            .EnumerateArray()
            .Select(x => GetJsonString(x, "path"))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Results.Ok(new IntegrationRemoteRootsResponse(integration.Id, serviceKey, roots, roots.Count == 0 ? "No root folders returned by integration." : string.Empty));
    }
    catch (Exception ex)
    {
        return Results.Ok(new IntegrationRemoteRootsResponse(integration.Id, serviceKey, [], $"Failed to discover root folders: {ex.Message}"));
    }
}).RequireAuthorization("AdminOnly");

app.MapPost("/api/integrations/{id:long}/test", async (long id, MediaCloudDbContext db, IHttpClientFactory httpClientFactory) =>
{
    var config = await db.IntegrationConfigs.FirstOrDefaultAsync(x => x.Id == id);
    if (config is null) return Results.NotFound(new ErrorResponse("Integration instance not found."));

    var result = await IntegrationCatalog.TestConnectionAsync(config.ServiceKey, config, httpClientFactory);
    return Results.Ok(new IntegrationTestResponse(config.Id, config.ServiceKey, config.InstanceName, result.Success, result.StatusCode, result.Message));
}).RequireAuthorization("AdminOnly");

app.MapPost("/api/integrations/{id:long}/sync", async (long id, TriggerIntegrationSyncRequest request, MediaCloudDbContext db, IHttpClientFactory httpClientFactory, IServiceScopeFactory scopeFactory) =>
{
    var integration = await db.IntegrationConfigs.FirstOrDefaultAsync(x => x.Id == id);
    if (integration is null)
    {
        return Results.NotFound(new ErrorResponse("Integration instance not found."));
    }

    var now = DateTimeOffset.UtcNow;
    var state = await db.IntegrationSyncStates.FirstOrDefaultAsync(x => x.IntegrationId == id);
    if (state is null)
    {
        state = new IntegrationSyncState
        {
            IntegrationId = id,
            LastAttemptedAtUtc = now,
            UpdatedAtUtc = now,
            LastError = string.Empty,
            ConsecutiveFailureCount = 0
        };
        db.IntegrationSyncStates.Add(state);
    }
    else
    {
        state.LastAttemptedAtUtc = now;
        state.UpdatedAtUtc = now;
        if (request.ForceFullResync)
        {
            state.LastCursor = string.Empty;
            state.LastEtag = string.Empty;
        }
    }

    if (!integration.Enabled)
    {
        state.LastError = "Integration is disabled.";
        state.ConsecutiveFailureCount += 1;
        await db.SaveChangesAsync();
        return Results.Ok(new TriggerIntegrationSyncResponse(id, false, "Integration is disabled."));
    }

    try
    {
        var runtimePolicy = await LoadRuntimePolicyValuesAsync(db,
            runtimeToleranceMinutesFloorKey,
            runtimeTolerancePercentKey,
            runtimeWarningPercentKey,
            runtimeHighMinutesKey,
            runtimeCriticalPercentKey,
            runtimeCriticalMinutesKey,
            runtimeToleranceMinutesFloorDefault,
            runtimeTolerancePercentDefault,
            runtimeWarningPercentDefault,
            runtimeHighMinutesDefault,
            runtimeCriticalPercentDefault,
            runtimeCriticalMinutesDefault);

        var outcome = await ExecuteIntegrationSyncAsync(
            integration,
            db,
            httpClientFactory,
            runtimePolicy,
            runtimeMismatchIssueType,
            runtimePolicyVersion,
            request.MediaScope);

        if (!outcome.Success)
        {
            state.LastError = outcome.Message;
            state.ConsecutiveFailureCount += 1;
            state.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new TriggerIntegrationSyncResponse(id, false, outcome.Message));
        }

        await ConsolidateMovieDuplicatesAsync(db, outcome.SyncSeenAtUtc);
        await ConsolidateTelevisionDuplicatesAsync(db, outcome.SyncSeenAtUtc);

        state.LastSuccessfulAtUtc = outcome.SyncSeenAtUtc;
        state.LastError = string.Empty;
        state.ConsecutiveFailureCount = 0;
        state.UpdatedAtUtc = outcome.SyncSeenAtUtc;
        await db.SaveChangesAsync();

        var responseMessage = outcome.Message;
        if (string.Equals(integration.ServiceKey, "sonarr", StringComparison.OrdinalIgnoreCase))
        {
            var jobId = StartRuntimeReprobeJob(
                runtimeReprobeJobs,
                scopeFactory,
                new BatchRuntimeReprobeRequest("Episode", 5000, false),
                "Queued TV runtime probe after Sonarr sync...");
            responseMessage = $"{responseMessage} Background runtime probe queued ({jobId}).";
        }

        return Results.Ok(new TriggerIntegrationSyncResponse(id, true, responseMessage));
    }
    catch (Exception ex)
    {
        state.LastError = ex.Message[..Math.Min(250, ex.Message.Length)];
        state.ConsecutiveFailureCount += 1;
        state.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return Results.Ok(new TriggerIntegrationSyncResponse(id, false, $"Sync failed: {state.LastError}"));
    }
}).RequireAuthorization("AdminOnly");

app.MapGet("/api/integrations/{id:long}/sync-state", async (long id, MediaCloudDbContext db) =>
{
    var integrationExists = await db.IntegrationConfigs.AnyAsync(x => x.Id == id);
    if (!integrationExists)
    {
        return Results.NotFound(new ErrorResponse("Integration instance not found."));
    }

    var state = await db.IntegrationSyncStates.FirstOrDefaultAsync(x => x.IntegrationId == id);
    if (state is null)
    {
        return Results.Ok(new IntegrationSyncStateDto(
            id,
            null,
            null,
            string.Empty,
            string.Empty,
            string.Empty,
            0,
            DateTimeOffset.UtcNow));
    }

    return Results.Ok(new IntegrationSyncStateDto(
        state.IntegrationId,
        state.LastAttemptedAtUtc,
        state.LastSuccessfulAtUtc,
        state.LastCursor,
        state.LastEtag,
        state.LastError,
        state.ConsecutiveFailureCount,
        state.UpdatedAtUtc));
}).RequireAuthorization("AdminOnly");

app.MapGet("/api/library/items", async (MediaCloudDbContext db, string? mediaType, string? q, bool? available, string? sortBy, string? sortDir, int? take, int? offset) =>
{
    var limit = Math.Clamp(take ?? 100, 1, 500);
    var skip = Math.Max(offset ?? 0, 0);

    var query = ApplyLibraryItemFilters(db, db.LibraryItems.AsQueryable(), mediaType, q, available);
    query = ApplyLibraryItemSort(query, sortBy, sortDir);

    var rows = await query
        .Skip(skip)
        .Take(limit)
        .ToListAsync();

    var rowIds = rows.Select(x => x.Id).ToList();
    var sourceServiceMap = new Dictionary<long, IReadOnlyList<string>>();
    var sourceTitleMap = new Dictionary<long, IReadOnlyList<LibraryItemSourceTitleInfo>>();

    if (rowIds.Count > 0)
    {
        var sourceRows = await (
            from link in db.LibraryItemSourceLinks
            join integration in db.IntegrationConfigs on link.IntegrationId equals integration.Id
            where rowIds.Contains(link.LibraryItemId)
            select new LibraryItemSourceTitleInfo(
                link.LibraryItemId,
                integration.ServiceKey,
                integration.InstanceName,
                link.SourceTitle,
                link.SourceSortTitle))
            .ToListAsync();

        sourceServiceMap = sourceRows
            .GroupBy(x => x.LibraryItemId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g
                    .Select(x => x.ServiceKey)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x)
                    .ToList());

        sourceTitleMap = sourceRows
            .GroupBy(x => x.LibraryItemId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<LibraryItemSourceTitleInfo>)g.ToList());
    }

    var items = rows
        .Select(x => MapLibraryItemDto(
            x,
            sourceServiceMap.TryGetValue(x.Id, out var sources) ? sources : [],
            sourceTitleMap.TryGetValue(x.Id, out var sourceTitles) ? sourceTitles : []))
        .ToList();

    return Results.Ok(items);
}).RequireAuthorization();

app.MapGet("/api/library/items/count", async (MediaCloudDbContext db, string? mediaType, string? q, bool? available) =>
{
    var query = ApplyLibraryItemFilters(db, db.LibraryItems.AsQueryable(), mediaType, q, available);
    var total = await query.CountAsync();
    return Results.Ok(new LibraryItemCountResponse(total));
}).RequireAuthorization();

app.MapPost("/api/library/purge", async (PurgeLibraryRequest request, MediaCloudDbContext db, ClaimsPrincipal principal) =>
{
    var normalizedMediaType = (request.MediaType ?? string.Empty).Trim();
    IQueryable<LibraryItem> itemQuery = db.LibraryItems;
    if (!string.IsNullOrWhiteSpace(normalizedMediaType))
    {
        itemQuery = itemQuery.Where(x => x.MediaType == normalizedMediaType);
    }

    var itemIds = await itemQuery.Select(x => x.Id).ToListAsync();
    if (itemIds.Count == 0)
    {
        return Results.Ok(new PurgeLibraryResponse(normalizedMediaType, 0, 0, 0, 0));
    }

    var issuesDeleted = await db.LibraryIssues.Where(x => itemIds.Contains(x.LibraryItemId)).ExecuteDeleteAsync();
    var linksDeleted = await db.LibraryItemSourceLinks.Where(x => itemIds.Contains(x.LibraryItemId)).ExecuteDeleteAsync();
    var itemsDeleted = await db.LibraryItems.Where(x => itemIds.Contains(x.Id)).ExecuteDeleteAsync();

    var monitoringSettingsDeleted = 0;
    if (request.ClearMonitoringState)
    {
        var keys = itemIds.Select(GetMovieDesiredMonitoringKey).ToList();
        if (keys.Count > 0)
        {
            monitoringSettingsDeleted = await db.AppConfigEntries.Where(x => keys.Contains(x.Key)).ExecuteDeleteAsync();
        }
    }

    if (request.ResetSyncState)
    {
        var syncStates = await (
            from state in db.IntegrationSyncStates
            join integration in db.IntegrationConfigs on state.IntegrationId equals integration.Id
            where integration.Enabled
                && (integration.ServiceKey.ToLower() == "plex" || integration.ServiceKey.ToLower() == "radarr" || integration.ServiceKey.ToLower() == "overseerr" || integration.ServiceKey.ToLower() == "sonarr")
            select state)
            .ToListAsync();

        foreach (var sync in syncStates)
        {
            sync.LastCursor = string.Empty;
            sync.LastEtag = string.Empty;
            sync.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    await WriteAuditAsync(db, principal, "library_purge", principal.Identity?.Name ?? "admin", $"Purged library mediaType={(string.IsNullOrWhiteSpace(normalizedMediaType) ? "all" : normalizedMediaType)} items={itemsDeleted} links={linksDeleted} issues={issuesDeleted}");
    await db.SaveChangesAsync();

    return Results.Ok(new PurgeLibraryResponse(normalizedMediaType, itemsDeleted, linksDeleted, issuesDeleted, monitoringSettingsDeleted));
}).RequireAuthorization("AdminOnly");

app.MapGet("/api/library/items/jump", async (MediaCloudDbContext db, string token, string? mediaType, string? q, bool? available, string? sortBy, string? sortDir, int? pageSize) =>
{
    if (string.IsNullOrWhiteSpace(token))
    {
        return Results.BadRequest(new ErrorResponse("Token is required."));
    }

    var normalizedToken = token.Trim().ToUpperInvariant();
    var size = Math.Clamp(pageSize ?? 100, 1, 500);

    var query = ApplyLibraryItemFilters(db, db.LibraryItems.AsQueryable(), mediaType, q, available);
    query = ApplyLibraryItemSort(query, sortBy, sortDir);

    var rows = await query.Select(x => new { x.Id, x.Title }).ToListAsync();
    var index = rows.FindIndex(x => TitleMatchesJumpToken(x.Title, normalizedToken));
    if (index < 0)
    {
        return Results.Ok(new LibraryJumpResponse(false, normalizedToken, 0, null));
    }

    var pageIndex = index / size;
    var targetId = rows[index].Id;
    return Results.Ok(new LibraryJumpResponse(true, normalizedToken, pageIndex, targetId));
}).RequireAuthorization();

app.MapGet("/api/library/items/{id:long}", async (long id, MediaCloudDbContext db, IHttpClientFactory httpClientFactory) =>
{
    var row = await db.LibraryItems.FirstOrDefaultAsync(x => x.Id == id);
    if (row is null)
    {
        return Results.NotFound(new ErrorResponse("Library item not found."));
    }

    if (string.Equals(row.MediaType, "Series", StringComparison.OrdinalIgnoreCase))
    {
        await TryEnrichSeriesIdentifiersFromSourceAsync(row, db, httpClientFactory, persistChanges: false);
    }

    var sourceServices = await (
        from link in db.LibraryItemSourceLinks
        join integration in db.IntegrationConfigs on link.IntegrationId equals integration.Id
        where link.LibraryItemId == row.Id
        select integration.ServiceKey)
        .Distinct()
        .OrderBy(x => x)
        .ToListAsync();

    var sourceTitles = await (
        from link in db.LibraryItemSourceLinks
        join integration in db.IntegrationConfigs on link.IntegrationId equals integration.Id
        where link.LibraryItemId == row.Id
        select new LibraryItemSourceTitleInfo(
            link.LibraryItemId,
            integration.ServiceKey,
            integration.InstanceName,
            link.SourceTitle,
            link.SourceSortTitle))
        .ToListAsync();

    return Results.Ok(MapLibraryItemDto(row, sourceServices, sourceTitles));
}).RequireAuthorization();

app.MapGet("/api/library/items/{id:long}/series-parent", async (long id, MediaCloudDbContext db) =>
{
    var item = await db.LibraryItems.FirstOrDefaultAsync(x => x.Id == id);
    if (item is null)
    {
        return Results.NotFound(new ErrorResponse("Library item not found."));
    }

    if (!string.Equals(item.MediaType, "Episode", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new ErrorResponse("Series parent lookup is only available for Episode items."));
    }

    var candidateSeries = await db.LibraryItems
        .Where(x => x.MediaType == "Series")
        .ToListAsync();
    var parentSeries = TelevisionHierarchy.TryFindParentSeries(item, candidateSeries);
    if (parentSeries is null)
    {
        return Results.NotFound(new ErrorResponse("Parent series not found."));
    }

    var sourceServices = await (
        from link in db.LibraryItemSourceLinks
        join integration in db.IntegrationConfigs on link.IntegrationId equals integration.Id
        where link.LibraryItemId == parentSeries.Id
        select integration.ServiceKey)
        .Distinct()
        .OrderBy(x => x)
        .ToListAsync();

    var sourceTitles = await (
        from link in db.LibraryItemSourceLinks
        join integration in db.IntegrationConfigs on link.IntegrationId equals integration.Id
        where link.LibraryItemId == parentSeries.Id
        select new LibraryItemSourceTitleInfo(
            link.LibraryItemId,
            integration.ServiceKey,
            integration.InstanceName,
            link.SourceTitle,
            link.SourceSortTitle))
        .ToListAsync();

    return Results.Ok(MapLibraryItemDto(parentSeries, sourceServices, sourceTitles));
}).RequireAuthorization();

app.MapGet("/api/library/items/{id:long}/episodes", async (long id, MediaCloudDbContext db) =>
{
    var series = await db.LibraryItems.FirstOrDefaultAsync(x => x.Id == id);
    if (series is null)
    {
        return Results.NotFound(new ErrorResponse("Library item not found."));
    }

    if (!string.Equals(series.MediaType, "Series", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new ErrorResponse("Episode listing is only available for Series items."));
    }

    var scopePrefix = TelevisionGrouping.BuildEpisodeScopePrefixForSeries(series);
    var titlePrefix = $"{series.Title} — S";
    var sortPrefix = $"{series.SortTitle} s";
    var episodes = await db.LibraryItems
        .Where(x => x.MediaType == "Episode" && (
            x.CanonicalKey.StartsWith(scopePrefix) ||
            x.Title.StartsWith(titlePrefix) ||
            x.SortTitle.StartsWith(sortPrefix)))
        .OrderBy(x => x.SortTitle)
        .ToListAsync();

    var episodeIds = episodes.Select(x => x.Id).ToList();
    var sourceServicesByItem = await (
        from link in db.LibraryItemSourceLinks
        join integration in db.IntegrationConfigs on link.IntegrationId equals integration.Id
        where episodeIds.Contains(link.LibraryItemId)
        select new { link.LibraryItemId, integration.ServiceKey })
        .ToListAsync();

    var sourceTitlesByItem = await (
        from link in db.LibraryItemSourceLinks
        join integration in db.IntegrationConfigs on link.IntegrationId equals integration.Id
        where episodeIds.Contains(link.LibraryItemId)
        select new LibraryItemSourceTitleInfo(
            link.LibraryItemId,
            integration.ServiceKey,
            integration.InstanceName,
            link.SourceTitle,
            link.SourceSortTitle))
        .ToListAsync();

    var payload = episodes
        .Select(item => MapLibraryItemDto(
            item,
            sourceServicesByItem.Where(x => x.LibraryItemId == item.Id).Select(x => x.ServiceKey).Distinct().OrderBy(x => x).ToList(),
            sourceTitlesByItem.Where(x => x.LibraryItemId == item.Id).ToList()))
        .ToList();

    return Results.Ok(payload);
}).RequireAuthorization();

app.MapPost("/api/library/items/{id:long}/reprobe-runtime", async (long id, MediaCloudDbContext db, IHttpClientFactory httpClientFactory) =>
{
    var runtimePolicy = await LoadRuntimePolicyValuesAsync(db,
        runtimeToleranceMinutesFloorKey,
        runtimeTolerancePercentKey,
        runtimeWarningPercentKey,
        runtimeHighMinutesKey,
        runtimeCriticalPercentKey,
        runtimeCriticalMinutesKey,
        runtimeToleranceMinutesFloorDefault,
        runtimeTolerancePercentDefault,
        runtimeWarningPercentDefault,
        runtimeHighMinutesDefault,
        runtimeCriticalPercentDefault,
        runtimeCriticalMinutesDefault);

    var row = await db.LibraryItems.FirstOrDefaultAsync(x => x.Id == id);
    if (row is null)
    {
        return Results.NotFound(new ErrorResponse("Library item not found."));
    }

    var filePath = (row.PrimaryFilePath ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(filePath))
    {
        return Results.Ok(new LibraryItemRuntimeProbeResponse(row.Id, row.MediaType, row.Title, filePath, false, false, null, "No primary file path available to probe.", null, string.Empty, row.PlayabilityScore, row.PlayabilitySummary, row.PlayabilityCheckedAtUtc));
    }

    var fileExists = File.Exists(filePath);
    if (!fileExists)
    {
        var refreshedPath = await TryRefreshPrimaryFilePathFromSourceAsync(row, db, httpClientFactory);
        if (!string.IsNullOrWhiteSpace(refreshedPath))
        {
            filePath = refreshedPath;
            fileExists = File.Exists(filePath);
        }
    }

    if (!fileExists)
    {
        row.ActualRuntimeMinutes = null;
        ClearPlayability(row);
        row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await UpsertRuntimeProbeFailureIssueAsync(db, row, filePath, string.Empty, null, row.UpdatedAtUtc, runtimeProbeFailureIssueType);
        await UpsertRuntimeMismatchIssueAsync(db, row, runtimePolicy, row.UpdatedAtUtc, runtimeMismatchIssueType, runtimePolicyVersion);
        await db.SaveChangesAsync();
        return Results.Ok(new LibraryItemRuntimeProbeResponse(row.Id, row.MediaType, row.Title, filePath, false, false, null, "Resolved file path does not exist on this host. Try integration sync if mappings changed.", null, string.Empty, row.PlayabilityScore, row.PlayabilitySummary, row.PlayabilityCheckedAtUtc));
    }

    var probe = ProbeMediaFile(filePath);
    var runtimeMinutes = probe.RuntimeMinutes;
    row.PrimaryFilePath = filePath;
    row.ActualRuntimeMinutes = runtimeMinutes;
    ApplyPlayabilityProbe(row, probe, DateTimeOffset.UtcNow);
    row.UpdatedAtUtc = DateTimeOffset.UtcNow;
    await UpsertRuntimeProbeFailureIssueAsync(db, row, filePath, probe.Error, probe.ExitCode, row.UpdatedAtUtc, runtimeProbeFailureIssueType);
    await UpsertRuntimeMismatchIssueAsync(db, row, runtimePolicy, row.UpdatedAtUtc, runtimeMismatchIssueType, runtimePolicyVersion);
    await db.SaveChangesAsync();

    var success = runtimeMinutes.HasValue && runtimeMinutes.Value > 0;
    var message = success
        ? $"Runtime reprobe complete: {runtimeMinutes:0.##} min."
        : string.IsNullOrWhiteSpace(probe.Error)
            ? "Runtime reprobe completed but ffprobe did not return a usable duration."
            : $"Runtime reprobe failed: {probe.Error}";

    return Results.Ok(new LibraryItemRuntimeProbeResponse(row.Id, row.MediaType, row.Title, filePath, true, success, runtimeMinutes, message, probe.ExitCode, probe.Error, row.PlayabilityScore, row.PlayabilitySummary, row.PlayabilityCheckedAtUtc));
}).RequireAuthorization("AdminOnly");

app.MapGet("/api/library/items/{id:long}/playback-diagnostics", async (long id, MediaCloudDbContext db, int? take) =>
{
    var itemExists = await db.LibraryItems.AnyAsync(x => x.Id == id);
    if (!itemExists)
    {
        return Results.NotFound(new ErrorResponse("Library item not found."));
    }

    var limit = Math.Clamp(take ?? 20, 1, 100);
    var rows = await db.PlaybackDiagnosticEntries
        .Where(x => x.LibraryItemId == id)
        .ToListAsync();

    var payload = rows
        .OrderByDescending(x => x.OccurredAtUtc)
        .Take(limit)
        .Select(MapPlaybackDiagnosticDto)
        .ToList();
    return Results.Ok(payload);
}).RequireAuthorization();

app.MapPost("/api/library/items/{id:long}/playback-diagnostics/pull", async (long id, PullPlaybackDiagnosticsRequest? request, MediaCloudDbContext db, IHttpClientFactory httpClientFactory) =>
{
    var item = await db.LibraryItems.FirstOrDefaultAsync(x => x.Id == id);
    if (item is null)
    {
        return Results.NotFound(new ErrorResponse("Library item not found."));
    }

    var response = await PullPlaybackDiagnosticsAsync(item, db, httpClientFactory, request);
    return Results.Ok(response);
}).RequireAuthorization("AdminOnly");

app.MapGet("/api/library/items/{id:long}/remediation-jobs", async (long id, MediaCloudDbContext db, int? take) =>
{
    var itemExists = await db.LibraryItems.AnyAsync(x => x.Id == id);
    if (!itemExists)
    {
        return Results.NotFound(new ErrorResponse("Library item not found."));
    }

    var limit = Math.Clamp(take ?? 20, 1, 100);
    var rows = (await db.LibraryRemediationJobs
        .Where(x => x.LibraryItemId == id)
        .ToListAsync())
        .OrderByDescending(x => x.RequestedAtUtc)
        .Take(limit)
        .ToList();

    return Results.Ok(rows.Select(MapLibraryRemediationJobDto).ToList());
}).RequireAuthorization("AdminOnly");

app.MapGet("/api/library/items/{id:long}/sources", async (long id, MediaCloudDbContext db) =>
{
    var itemExists = await db.LibraryItems.AnyAsync(x => x.Id == id);
    if (!itemExists)
    {
        return Results.NotFound(new ErrorResponse("Library item not found."));
    }

    var rows = await (
        from link in db.LibraryItemSourceLinks
        join integration in db.IntegrationConfigs on link.IntegrationId equals integration.Id
        where link.LibraryItemId == id
        orderby integration.ServiceKey, integration.InstanceName
        select new LibraryItemSourceLinkDto(
            link.Id,
            link.LibraryItemId,
            link.IntegrationId,
            integration.ServiceKey,
            integration.InstanceName,
            link.SourceTitle,
            link.SourceSortTitle,
            link.ExternalId,
            link.ExternalType,
            link.ExternalUpdatedAtUtc,
            link.LastSeenAtUtc,
            link.IsDeletedAtSource))
        .ToListAsync();

    return Results.Ok(rows);
}).RequireAuthorization();

app.MapGet("/api/library/items/{id:long}/source-status", async (long id, MediaCloudDbContext db, IHttpClientFactory httpClientFactory) =>
{
    var item = await db.LibraryItems.FirstOrDefaultAsync(x => x.Id == id);
    if (item is null)
    {
        return Results.NotFound(new ErrorResponse("Library item not found."));
    }

    IReadOnlyList<LibraryItemSourceStatusDto> statuses;
    if (string.Equals(item.MediaType, "Movie", StringComparison.OrdinalIgnoreCase))
    {
        statuses = await GetMovieSourceStatusesAsync(item, db, httpClientFactory);
    }
    else if (string.Equals(item.MediaType, "Series", StringComparison.OrdinalIgnoreCase)
        || string.Equals(item.MediaType, "Episode", StringComparison.OrdinalIgnoreCase))
    {
        statuses = await GetTelevisionSourceStatusesAsync(item, db);
    }
    else
    {
        statuses = [];
    }

    return Results.Ok(statuses);
}).RequireAuthorization();

app.MapPost("/api/library/items/{id:long}/remediation/search-replacement", async (long id, SearchReplacementRequest? request, MediaCloudDbContext db, IHttpClientFactory httpClientFactory, ClaimsPrincipal principal) =>
{
    var item = await db.LibraryItems.FirstOrDefaultAsync(x => x.Id == id);
    if (item is null)
    {
        return Results.NotFound(new ErrorResponse("Library item not found."));
    }

    var sourceLinks = await GetLibraryRemediationSourceLinksAsync(db, item.Id);
    var plan = LibraryRemediationPlanner.PlanSearchReplacement(item.MediaType, sourceLinks);
    var normalizedIssueType = string.IsNullOrWhiteSpace(request?.IssueType)
        ? (!string.IsNullOrWhiteSpace(request?.Reason) ? request!.Reason.Trim() : "other")
        : request!.IssueType!.Trim();
    var normalizedReason = string.IsNullOrWhiteSpace(request?.Reason) ? "unspecified" : request!.Reason.Trim();
    var normalizedNotes = string.IsNullOrWhiteSpace(request?.Notes) ? string.Empty : request!.Notes.Trim();
    var actor = principal.Identity?.Name ?? "admin";
    var intent = LibraryRemediationPlanner.BuildIntent(normalizedIssueType, request?.Notes);
    if (!intent.ShouldSearchNow)
    {
        var blockedResult = LibraryRemediationExecution.BuildBlockedResult(item.Id, plan.ServiceKey, plan.DisplayName, plan.CommandName, plan.ExternalItemId, normalizedReason, normalizedNotes, intent.PolicySummary, intent);
        var blockedIssueId = await db.LibraryIssues
            .Where(x => x.LibraryItemId == item.Id && x.IssueType == intent.IssueType)
            .OrderByDescending(x => x.Id)
            .Select(x => (long?)x.Id)
            .FirstOrDefaultAsync();
        db.LibraryRemediationJobs.Add(LibraryRemediationJobFactory.Create(item.Id, blockedIssueId, intent, blockedResult, null, actor, DateTimeOffset.UtcNow, null));
        await db.SaveChangesAsync();
        return Results.BadRequest(new ErrorResponse(intent.PolicySummary));
    }

    if (!plan.IsSupported)
    {
        return Results.BadRequest(new ErrorResponse(plan.Message));
    }

    var latestDiagnostic = await db.PlaybackDiagnosticEntries
        .Where(x => x.LibraryItemId == item.Id)
        .OrderByDescending(x => x.OccurredAtUtc)
        .FirstOrDefaultAsync();
    intent = LibraryRemediationDiagnosticsDecisioning.ApplyLatestDiagnostic(intent, latestDiagnostic);
    intent = LibraryRemediationProfileDecisioning.Apply(intent, item);
    if (!intent.ShouldSearchNow)
    {
        var blockedMessage = intent.ProfileDecision is "review_language_profile" or "review_quality_profile"
            ? intent.ProfileSummary
            : intent.PolicySummary;
        var blockedResult = LibraryRemediationExecution.BuildBlockedResult(item.Id, plan.ServiceKey, plan.DisplayName, plan.CommandName, plan.ExternalItemId, normalizedReason, normalizedNotes, blockedMessage, intent);
        var blockedIssueId = await db.LibraryIssues
            .Where(x => x.LibraryItemId == item.Id && x.IssueType == intent.IssueType)
            .OrderByDescending(x => x.Id)
            .Select(x => (long?)x.Id)
            .FirstOrDefaultAsync();
        db.LibraryRemediationJobs.Add(LibraryRemediationJobFactory.Create(item.Id, blockedIssueId, intent, blockedResult, null, actor, DateTimeOffset.UtcNow, null));
        await db.SaveChangesAsync();
        return Results.BadRequest(new ErrorResponse(blockedMessage));
    }

    var preSearchReleaseContext = await BuildRemediationReleaseContextAsync(item, plan, db, httpClientFactory);
    var integrationForBlacklist = plan.IntegrationId.HasValue
        ? await db.IntegrationConfigs.FirstOrDefaultAsync(x => x.Id == plan.IntegrationId.Value && x.Enabled)
        : await GetEnabledIntegrationByServiceAsync(db, plan.ServiceKey);
    var blacklistPlan = LibraryRemediationBlacklistPlanner.BuildPlan(intent, preSearchReleaseContext);
    (bool Success, string Message)? blacklistOutcome = null;
    if (integrationForBlacklist is not null && blacklistPlan.ShouldAttempt)
    {
        blacklistOutcome = await ExecuteBlacklistPlanAsync(integrationForBlacklist, blacklistPlan, httpClientFactory);
    }

    var result = await ExecuteSearchReplacementAsync(item, plan, db, httpClientFactory);
    var combinedMessage = blacklistOutcome is null
        ? result.Message
        : blacklistOutcome.Value.Success
            ? $"Blacklisted current release. {result.Message}"
            : $"Blacklist skipped/failed: {blacklistOutcome.Value.Message} {result.Message}".Trim();
    var normalizedResult = result with { Reason = normalizedReason, Notes = normalizedNotes, Message = combinedMessage, Intent = ToIntentDto(intent) };
    var contextPlan = plan with { ExternalItemId = normalizedResult.ExternalItemId };
    var releaseContext = await BuildRemediationReleaseContextAsync(item, contextPlan, db, httpClientFactory);
    var relatedIssueId = await db.LibraryIssues
        .Where(x => x.LibraryItemId == item.Id && x.IssueType == intent.IssueType)
        .OrderByDescending(x => x.Id)
        .Select(x => (long?)x.Id)
        .FirstOrDefaultAsync();
    db.LibraryRemediationJobs.Add(LibraryRemediationJobFactory.Create(item.Id, relatedIssueId, intent, normalizedResult, releaseContext, actor, DateTimeOffset.UtcNow, blacklistOutcome?.Success));
    var summary = $"Search replacement for library item {item.Id} ({item.MediaType}) via {normalizedResult.ServiceKey}: success={normalizedResult.Success}, issueType={intent.IssueType}, action={intent.RequestedAction}, confidence={intent.Confidence}, reason={normalizedReason}, notes={normalizedNotes}";
    await WriteAuditAsync(db, principal, "library_remediation_search_replacement", actor, summary);
    await db.SaveChangesAsync();
    return Results.Ok(normalizedResult);
}).RequireAuthorization("AdminOnly");

app.MapPost("/api/library/items/{id:long}/sources/{serviceKey}/sync", async (long id, string serviceKey, MediaCloudDbContext db, IHttpClientFactory httpClientFactory) =>
{
    var item = await db.LibraryItems.FirstOrDefaultAsync(x => x.Id == id);
    if (item is null)
    {
        return Results.NotFound(new ErrorResponse("Library item not found."));
    }

    if (!string.Equals(item.MediaType, "Movie", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new ErrorResponse("Source sync actions currently support Movie items only."));
    }

    var normalizedService = (serviceKey ?? string.Empty).Trim().ToLowerInvariant();
    if (normalizedService is not ("plex" or "radarr" or "overseerr"))
    {
        return Results.BadRequest(new ErrorResponse("Unsupported source service. Use plex, radarr, or overseerr."));
    }

    var integration = await GetEnabledIntegrationByServiceAsync(db, normalizedService);
    if (integration is null)
    {
        return Results.BadRequest(new ErrorResponse($"No enabled {normalizedService} integration configured."));
    }

    var hadBefore = await HasSourceLinkForServiceAsync(db, item.Id, normalizedService);
    var attemptedAction = false;

    if (normalizedService == "radarr")
    {
        var candidate = new PlexBackfillCandidateDto(
            item.Id,
            item.Title,
            item.Year,
            item.TmdbId,
            item.ImdbId,
            !hadBefore,
            false,
            false,
            item.TmdbId.HasValue && item.TmdbId.Value > 0,
            string.Empty);

        var radarrResult = await EnsureMovieInRadarrAsync(candidate, integration, httpClientFactory);
        if (!radarrResult.Success)
        {
            return Results.Ok(new LibraryItemSourceSyncResponse(item.Id, normalizedService, false, hadBefore, hadBefore, false, $"Radarr sync action failed: {radarrResult.Message}"));
        }

        attemptedAction = radarrResult.PerformedAction;
    }
    else if (normalizedService == "overseerr")
    {
        var candidate = new PlexBackfillCandidateDto(
            item.Id,
            item.Title,
            item.Year,
            item.TmdbId,
            item.ImdbId,
            false,
            !hadBefore,
            false,
            item.TmdbId.HasValue && item.TmdbId.Value > 0,
            string.Empty);

        var overseerrResult = await EnsureMovieRequestedInOverseerrAsync(candidate, integration, httpClientFactory);
        if (!overseerrResult.Success)
        {
            return Results.Ok(new LibraryItemSourceSyncResponse(item.Id, normalizedService, false, hadBefore, hadBefore, false, $"Overseerr sync action failed: {overseerrResult.Message}"));
        }

        attemptedAction = overseerrResult.PerformedAction;
    }

    var syncOutcome = await SyncSingleIntegrationNowAsync(integration, db, httpClientFactory,
        runtimeToleranceMinutesFloorKey,
        runtimeTolerancePercentKey,
        runtimeWarningPercentKey,
        runtimeHighMinutesKey,
        runtimeCriticalPercentKey,
        runtimeCriticalMinutesKey,
        runtimeToleranceMinutesFloorDefault,
        runtimeTolerancePercentDefault,
        runtimeWarningPercentDefault,
        runtimeHighMinutesDefault,
        runtimeCriticalPercentDefault,
        runtimeCriticalMinutesDefault,
        runtimeMismatchIssueType,
        runtimePolicyVersion);

    if (!syncOutcome.Success)
    {
        return Results.Ok(new LibraryItemSourceSyncResponse(item.Id, normalizedService, false, hadBefore, hadBefore, attemptedAction, syncOutcome.Message));
    }

    var hasAfter = await HasSourceLinkForServiceAsync(db, item.Id, normalizedService);
    var message = hasAfter
        ? $"{IntegrationCatalog.GetName(normalizedService)} link verified after sync."
        : $"Sync completed, but {IntegrationCatalog.GetName(normalizedService)} link is still missing.";

    return Results.Ok(new LibraryItemSourceSyncResponse(item.Id, normalizedService, hasAfter, hadBefore, hasAfter, attemptedAction, message));
}).RequireAuthorization("AdminOnly");

app.MapGet("/api/library/items/{id:long}/monitoring", async (long id, MediaCloudDbContext db, IHttpClientFactory httpClientFactory) =>
{
    var item = await db.LibraryItems.FirstOrDefaultAsync(x => x.Id == id);
    if (item is null)
    {
        return Results.NotFound(new ErrorResponse("Library item not found."));
    }

    var radarr = await GetEnabledIntegrationByServiceAsync(db, "radarr");
    var overseerr = await GetEnabledIntegrationByServiceAsync(db, "overseerr");
    var radarrState = await FetchRadarrMovieStateAsync(item.TmdbId, radarr, httpClientFactory);
    var desired = await GetMovieDesiredMonitoringAsync(db, item.Id);
    var autoSync = await GetMonitoringAutoSyncEnabledAsync(db);
    var overseerrSignal = await HasSourceLinkForServiceAsync(db, item.Id, "overseerr");
    if (!overseerrSignal)
    {
        var overseerrState = await FetchOverseerrMovieStateAsync(item.TmdbId, overseerr, httpClientFactory);
        overseerrSignal = overseerrState.Exists;
    }

    var drift = desired.HasValue && radarrState.Monitored.HasValue
        ? desired.Value != radarrState.Monitored.Value
        : (bool?)null;

    return Results.Ok(new LibraryItemMonitoringStateResponse(item.Id, desired, radarrState.Monitored, drift, overseerrSignal, radarrState.Exists, autoSync));
}).RequireAuthorization();

app.MapPut("/api/library/items/{id:long}/monitoring/desired", async (long id, SetDesiredMonitoringRequest request, MediaCloudDbContext db, IHttpClientFactory httpClientFactory) =>
{
    var item = await db.LibraryItems.FirstOrDefaultAsync(x => x.Id == id);
    if (item is null)
    {
        return Results.NotFound(new ErrorResponse("Library item not found."));
    }

    var managedByMediaCloud = await GetMonitoringManagedByMediaCloudAsync(db);
    if (!managedByMediaCloud)
    {
        return Results.BadRequest(new ErrorResponse("MediaCloud-managed monitoring is disabled in Settings."));
    }

    await SetMovieDesiredMonitoringAsync(db, item.Id, request.DesiredMonitored, DateTimeOffset.UtcNow);
    await db.SaveChangesAsync();

    var autoSync = await GetMonitoringAutoSyncEnabledAsync(db);
    if (autoSync)
    {
        await SyncDesiredMonitoringToIntegrationAsync(item, "radarr", db, httpClientFactory);
        await SyncDesiredMonitoringToIntegrationAsync(item, "overseerr", db, httpClientFactory);
    }

    return Results.Ok(new LibraryItemMonitoringStateResponse(item.Id, request.DesiredMonitored, null, null, false, false, autoSync));
}).RequireAuthorization("AdminOnly");

app.MapPost("/api/library/items/{id:long}/monitoring/sync/{serviceKey}", async (long id, string serviceKey, MediaCloudDbContext db, IHttpClientFactory httpClientFactory) =>
{
    var item = await db.LibraryItems.FirstOrDefaultAsync(x => x.Id == id);
    if (item is null)
    {
        return Results.NotFound(new ErrorResponse("Library item not found."));
    }

    var normalizedService = (serviceKey ?? string.Empty).Trim().ToLowerInvariant();
    if (normalizedService is not ("radarr" or "overseerr"))
    {
        return Results.BadRequest(new ErrorResponse("Unsupported monitoring sync target. Use radarr or overseerr."));
    }

    var managedByMediaCloud = await GetMonitoringManagedByMediaCloudAsync(db);
    if (!managedByMediaCloud)
    {
        return Results.BadRequest(new ErrorResponse("MediaCloud-managed monitoring is disabled in Settings."));
    }

    var result = await SyncDesiredMonitoringToIntegrationAsync(item, normalizedService, db, httpClientFactory);
    return Results.Ok(result);
}).RequireAuthorization("AdminOnly");

app.MapGet("/api/library/items/{id:long}/issues", async (long id, MediaCloudDbContext db, string? status, string? issueType, int? take) =>
{
    var itemExists = await db.LibraryItems.AnyAsync(x => x.Id == id);
    if (!itemExists)
    {
        return Results.NotFound(new ErrorResponse("Library item not found."));
    }

    var limit = Math.Clamp(take ?? 100, 1, 500);
    var query = db.LibraryIssues.Where(x => x.LibraryItemId == id);

    if (!string.IsNullOrWhiteSpace(status))
    {
        query = query.Where(x => x.Status == status);
    }

    if (!string.IsNullOrWhiteSpace(issueType))
    {
        query = query.Where(x => x.IssueType == issueType);
    }

    var rows = await query
        .OrderByDescending(x => x.Id)
        .Take(limit)
        .ToListAsync();

    return Results.Ok(rows.Select(MapLibraryIssueDto).ToList());
}).RequireAuthorization();

app.MapPost("/api/library/items/{id:long}/issues", async (long id, CreateLibraryIssueRequest request, MediaCloudDbContext db) =>
{
    var item = await db.LibraryItems.FirstOrDefaultAsync(x => x.Id == id);
    if (item is null)
    {
        return Results.NotFound(new ErrorResponse("Library item not found."));
    }

    var issueType = string.IsNullOrWhiteSpace(request.IssueType)
        ? "manual_issue"
        : request.IssueType.Trim().ToLowerInvariant();
    var severity = NormalizeIssueSeverity(request.Severity);
    var status = string.Equals(request.Status, "Resolved", StringComparison.OrdinalIgnoreCase)
        ? "Resolved"
        : "Open";
    var now = DateTimeOffset.UtcNow;

    var summary = string.IsNullOrWhiteSpace(request.Summary)
        ? $"Manual issue flagged for {item.Title}."
        : request.Summary.Trim();

    var detailsJson = string.IsNullOrWhiteSpace(request.DetailsJson)
        ? "{}"
        : request.DetailsJson;

    var issue = await db.LibraryIssues
        .Where(x => x.LibraryItemId == id && x.IssueType == issueType)
        .OrderByDescending(x => x.Id)
        .FirstOrDefaultAsync();

    if (issue is null)
    {
        issue = new LibraryIssue
        {
            LibraryItemId = id,
            IssueType = issueType,
            FirstDetectedAtUtc = now
        };
        db.LibraryIssues.Add(issue);
    }

    issue.PolicyVersion = "manual-v1";
    issue.Status = status;
    issue.Severity = severity;
    issue.Summary = summary;
    issue.SuggestedAction = string.IsNullOrWhiteSpace(request.SuggestedAction)
        ? "Review media file metadata and replace or remux with preferred language track/subtitles."
        : request.SuggestedAction.Trim();
    issue.DetailsJson = detailsJson;
    issue.LastDetectedAtUtc = now;
    issue.ResolvedAtUtc = status == "Resolved" ? now : null;

    await db.SaveChangesAsync();
    return Results.Ok(MapLibraryIssueDto(issue));
}).RequireAuthorization("AdminOnly");

app.MapPost("/api/library/issues/{issueId:long}/resolve", async (long issueId, MediaCloudDbContext db) =>
{
    var issue = await db.LibraryIssues.FirstOrDefaultAsync(x => x.Id == issueId);
    if (issue is null)
    {
        return Results.NotFound(new ErrorResponse("Issue not found."));
    }

    var now = DateTimeOffset.UtcNow;
    issue.Status = "Resolved";
    issue.ResolvedAtUtc = now;
    issue.LastDetectedAtUtc = now;
    await db.SaveChangesAsync();

    return Results.Ok(MapLibraryIssueDto(issue));
}).RequireAuthorization("AdminOnly");

app.MapPost("/api/library/items/reprobe-missing-runtimes", async (BatchRuntimeReprobeRequest request, MediaCloudDbContext db) =>
{
    var runtimePolicy = await LoadRuntimePolicyValuesAsync(db,
        runtimeToleranceMinutesFloorKey,
        runtimeTolerancePercentKey,
        runtimeWarningPercentKey,
        runtimeHighMinutesKey,
        runtimeCriticalPercentKey,
        runtimeCriticalMinutesKey,
        runtimeToleranceMinutesFloorDefault,
        runtimeTolerancePercentDefault,
        runtimeWarningPercentDefault,
        runtimeHighMinutesDefault,
        runtimeCriticalPercentDefault,
        runtimeCriticalMinutesDefault);

    var result = await ExecuteBatchRuntimeReprobeAsync(
        db,
        request,
        runtimePolicy,
        runtimeMismatchIssueType,
        runtimePolicyVersion,
        progress: null);

    return Results.Ok(result);
}).RequireAuthorization("AdminOnly");

app.MapPost("/api/library/items/reprobe-runtimes/jobs", (BatchRuntimeReprobeRequest request, IServiceScopeFactory scopeFactory) =>
{
    var jobId = StartRuntimeReprobeJob(
        runtimeReprobeJobs,
        scopeFactory,
        request,
        "Queued runtime probe job...");

    return Results.Accepted($"/api/library/items/reprobe-runtimes/jobs/{jobId}", new StartRuntimeReprobeJobResponse(jobId));
}).RequireAuthorization("AdminOnly");

app.MapGet("/api/library/items/reprobe-runtimes/jobs/{jobId:guid}", (Guid jobId) =>
{
    if (!runtimeReprobeJobs.TryGetValue(jobId, out var status))
    {
        return Results.NotFound(new { Message = "Probe job not found." });
    }

    return Results.Ok(status);
}).RequireAuthorization("AdminOnly");

app.MapGet("/api/library/issues", async (MediaCloudDbContext db, string? status, string? issueType, int? take) =>
{
    var limit = Math.Clamp(take ?? 100, 1, 500);
    var query = db.LibraryIssues.AsQueryable();

    if (!string.IsNullOrWhiteSpace(status))
    {
        query = query.Where(x => x.Status == status);
    }

    if (!string.IsNullOrWhiteSpace(issueType))
    {
        query = query.Where(x => x.IssueType == issueType);
    }

    var rows = await (
        from issue in query
        join item in db.LibraryItems on issue.LibraryItemId equals item.Id into itemJoin
        from item in itemJoin.DefaultIfEmpty()
        orderby issue.Id descending
        select new LibraryIssueDto(
            issue.Id,
            issue.LibraryItemId,
            issue.IssueType,
            issue.Severity,
            issue.Status,
            issue.Summary,
            issue.SuggestedAction,
            issue.DetailsJson,
            issue.FirstDetectedAtUtc,
            issue.LastDetectedAtUtc,
            issue.ResolvedAtUtc,
            item != null ? item.Title : string.Empty,
            item != null ? item.MediaType : string.Empty))
        .Take(limit)
        .ToListAsync();

    return Results.Ok(rows);
}).RequireAuthorization();

app.Run();

static void EnsureSqliteColumn(MediaCloudDbContext db, string tableName, string columnName, string columnDefinition)
{
    var connection = db.Database.GetDbConnection();
    if (connection.State != ConnectionState.Open)
    {
        connection.Open();
    }

    using var command = connection.CreateCommand();
    command.CommandText = $"PRAGMA table_info(\"{tableName}\");";
    using var reader = command.ExecuteReader();
    while (reader.Read())
    {
        var existingColumn = reader["name"]?.ToString();
        if (string.Equals(existingColumn, columnName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
    }

    var alterSql = $"ALTER TABLE \"{tableName}\" ADD COLUMN \"{columnName}\" {columnDefinition};";
    db.Database.ExecuteSqlRaw(alterSql);
}

static async Task WriteAuditAsync(MediaCloudDbContext db, ClaimsPrincipal principal, string action, string targetUsername, string summary, Guid? targetUserId = null)
{
    var actorIdRaw = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
    Guid.TryParse(actorIdRaw, out var actorId);
    var actorName = principal.Identity?.Name ?? principal.FindFirstValue(ClaimTypes.Name) ?? "admin";

    db.UserAuditLogs.Add(new UserAuditLog
    {
        ActorUserId = actorId == Guid.Empty ? null : actorId,
        ActorUsername = actorName,
        TargetUserId = targetUserId ?? (actorId == Guid.Empty ? Guid.Empty : actorId),
        TargetUsername = targetUsername,
        Action = action,
        Summary = summary
    });

    await Task.CompletedTask;
}

static LibraryItemDto MapLibraryItemDto(
    LibraryItem item,
    IReadOnlyList<string>? sourceServices = null,
    IReadOnlyList<LibraryItemSourceTitleInfo>? sourceTitles = null)
{
    var audio = DeserializeLanguages(item.AudioLanguagesJson);
    var subtitles = DeserializeLanguages(item.SubtitleLanguagesJson);
    var playabilityDetails = DeserializePlayabilityDetails(item.PlayabilityDetailsJson);
    var displayTitle = BuildLibraryDisplayTitle(item, sourceTitles);
    var localFileExists = !string.IsNullOrWhiteSpace(item.PrimaryFilePath) && File.Exists(item.PrimaryFilePath);

    return new LibraryItemDto(
        item.Id,
        item.CanonicalKey,
        item.MediaType,
        displayTitle,
        item.Title,
        item.SortTitle,
        item.Year,
        item.TmdbId,
        item.TvdbId,
        item.ImdbId,
        item.PlexRatingKey,
        item.Description,
        item.DescriptionSourceService,
        item.RuntimeMinutes,
        item.ActualRuntimeMinutes,
        audio,
        subtitles,
        item.PlayabilityScore,
        item.PlayabilitySummary,
        item.PlayabilityCheckedAtUtc,
        playabilityDetails.Reasons,
        playabilityDetails.VideoCodec,
        playabilityDetails.AudioCodecs,
        playabilityDetails.SubtitleCodecs,
        item.IsAvailable,
        item.QualityProfile,
        item.SourceUpdatedAtUtc,
        item.UpdatedAtUtc,
        item.PrimaryFilePath,
        localFileExists,
        sourceServices ?? []);
}

static LibraryIssueDto MapLibraryIssueDto(LibraryIssue issue)
{
    return new LibraryIssueDto(
        issue.Id,
        issue.LibraryItemId,
        issue.IssueType,
        issue.Severity,
        issue.Status,
        issue.Summary,
        issue.SuggestedAction,
        issue.DetailsJson,
        issue.FirstDetectedAtUtc,
        issue.LastDetectedAtUtc,
        issue.ResolvedAtUtc,
        string.Empty,
        string.Empty);
}

static PlaybackDiagnosticDto MapPlaybackDiagnosticDto(PlaybackDiagnosticEntry row)
{
    return new PlaybackDiagnosticDto(
        row.Id,
        row.LibraryItemId,
        row.SourceService,
        IntegrationCatalog.GetName(row.SourceService),
        row.ExternalId,
        row.OccurredAtUtc,
        row.ImportedAtUtc,
        row.StartedAtUtc,
        row.StoppedAtUtc,
        row.UserName,
        row.ClientName,
        row.Player,
        row.Product,
        row.Platform,
        row.Decision,
        row.TranscodeDecision,
        row.VideoDecision,
        row.AudioDecision,
        row.SubtitleDecision,
        row.Container,
        row.VideoCodec,
        row.AudioCodec,
        row.SubtitleCodec,
        row.QualityProfile,
        row.HealthLabel,
        row.Summary,
        row.SuspectedCause,
        row.ErrorMessage,
        row.LogSnippet);
}

static LibraryRemediationJobDto MapLibraryRemediationJobDto(LibraryRemediationJob row)
{
    return new LibraryRemediationJobDto(
        row.Id,
        row.LibraryItemId,
        row.LibraryIssueId,
        row.ServiceKey,
        row.ServiceDisplayName,
        row.RequestedAction,
        row.CommandName,
        row.ExternalItemId,
        row.IssueType,
        row.Reason,
        row.Notes,
        row.ReasonCategory,
        row.Confidence,
        row.ShouldSearchNow,
        row.ShouldBlacklistCurrentRelease,
        row.NeedsManualReview,
        row.NotesRecordedOnly,
        row.LookedUpRemotely,
        row.PolicySummary,
        row.NotesHandling,
        row.ProfileDecision,
        row.ProfileSummary,
        row.Status,
        row.SearchStatus,
        row.BlacklistStatus,
        row.OutcomeSummary,
        row.ResultMessage,
        row.ReleaseSummary,
        row.RequestedBy,
        row.RequestedAtUtc,
        row.FinishedAtUtc,
        row.LastCheckedAtUtc);
}

static void ApplyPreferredDescription(LibraryItem item, string sourceService, string? candidateDescription)
{
    var cleaned = (candidateDescription ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(cleaned))
    {
        return;
    }

    var existingPriority = GetDescriptionSourcePriority(item.DescriptionSourceService);
    var candidatePriority = GetDescriptionSourcePriority(sourceService);
    if (string.IsNullOrWhiteSpace(item.Description)
        || candidatePriority < existingPriority
        || (candidatePriority == existingPriority && cleaned.Length > item.Description.Length))
    {
        item.Description = cleaned;
        item.DescriptionSourceService = sourceService;
    }
}

static int GetDescriptionSourcePriority(string? sourceService)
{
    return (sourceService ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "sonarr" => 1,
        "radarr" => 1,
        "plex" => 2,
        "overseerr" => 3,
        _ => 99
    };
}

static string NormalizeIssueSeverity(string? severity)
{
    var normalized = (severity ?? string.Empty).Trim();
    if (normalized.Equals("Critical", StringComparison.OrdinalIgnoreCase)) return "Critical";
    if (normalized.Equals("High", StringComparison.OrdinalIgnoreCase)) return "High";
    if (normalized.Equals("Info", StringComparison.OrdinalIgnoreCase)) return "Info";
    return "Warning";
}

static IReadOnlyList<string> DeserializeLanguages(string json)
{
    if (string.IsNullOrWhiteSpace(json))
    {
        return [];
    }

    try
    {
        return JsonSerializer.Deserialize<List<string>>(json) ?? [];
    }
    catch
    {
        return [];
    }
}

static MediaPlayabilityStoredDetails DeserializePlayabilityDetails(string json)
{
    if (string.IsNullOrWhiteSpace(json))
    {
        return new MediaPlayabilityStoredDetails([], string.Empty, string.Empty, string.Empty, null, null, null, [], [], []);
    }

    try
    {
        return JsonSerializer.Deserialize<MediaPlayabilityStoredDetails>(json)
            ?? new MediaPlayabilityStoredDetails([], string.Empty, string.Empty, string.Empty, null, null, null, [], [], []);
    }
    catch
    {
        return new MediaPlayabilityStoredDetails([], string.Empty, string.Empty, string.Empty, null, null, null, [], [], []);
    }
}

static IQueryable<LibraryItem> ApplyLibraryItemFilters(MediaCloudDbContext db, IQueryable<LibraryItem> query, string? mediaType, string? q, bool? available)
{
    if (!string.IsNullOrWhiteSpace(mediaType))
    {
        query = query.Where(x => x.MediaType == mediaType);
    }

    if (!string.IsNullOrWhiteSpace(q))
    {
        var needle = q.Trim().ToLowerInvariant();
        query = query.Where(x =>
            x.Title.ToLower().Contains(needle) ||
            x.SortTitle.ToLower().Contains(needle) ||
            x.CanonicalKey.ToLower().Contains(needle) ||
            db.LibraryItemSourceLinks.Any(link =>
                link.LibraryItemId == x.Id &&
                (link.SourceTitle.ToLower().Contains(needle) ||
                 link.SourceSortTitle.ToLower().Contains(needle))));
    }

    if (available.HasValue)
    {
        query = query.Where(x => x.IsAvailable == available.Value);
    }

    return query;
}

static IQueryable<LibraryItem> ApplyLibraryItemSort(IQueryable<LibraryItem> query, string? sortBy, string? sortDir)
{
    var descending = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
    var normalizedSortBy = (sortBy ?? "title").Trim().ToLowerInvariant();

    return normalizedSortBy switch
    {
        "year" => descending ? query.OrderByDescending(x => x.Year).ThenBy(x => x.SortTitle) : query.OrderBy(x => x.Year).ThenBy(x => x.SortTitle),
        "updated" => descending ? query.OrderByDescending(x => x.UpdatedAtUtc).ThenBy(x => x.SortTitle) : query.OrderBy(x => x.UpdatedAtUtc).ThenBy(x => x.SortTitle),
        "runtime" => descending ? query.OrderByDescending(x => x.RuntimeMinutes).ThenBy(x => x.SortTitle) : query.OrderBy(x => x.RuntimeMinutes).ThenBy(x => x.SortTitle),
        _ => descending ? query.OrderByDescending(x => x.SortTitle) : query.OrderBy(x => x.SortTitle)
    };
}

static bool TitleMatchesJumpToken(string? title, string token)
{
    var value = (title ?? string.Empty).Trim();
    if (string.IsNullOrEmpty(value))
    {
        return false;
    }

    var first = char.ToUpperInvariant(value[0]);
    if (token == "#")
    {
        return !char.IsLetter(first);
    }

    return first.ToString() == token;
}

static async Task<IntegrationSyncOutcome> ExecuteIntegrationSyncAsync(
    IntegrationConfig integration,
    MediaCloudDbContext db,
    IHttpClientFactory httpClientFactory,
    RuntimePolicyValues runtimePolicy,
    string runtimeMismatchIssueType,
    string runtimePolicyVersion,
    string? mediaScope = null)
{
    var service = (integration.ServiceKey ?? string.Empty).Trim().ToLowerInvariant();
    var normalizedScope = NormalizeIntegrationSyncScope(mediaScope);
    return service switch
    {
        "radarr" => await SyncRadarrMoviesAsync(integration, db, httpClientFactory, runtimePolicy, runtimeMismatchIssueType, runtimePolicyVersion),
        "sonarr" => await SyncSonarrTelevisionAsync(integration, db, httpClientFactory, runtimePolicy, runtimeMismatchIssueType, runtimePolicyVersion),
        "plex" => normalizedScope == "tv"
            ? await SyncPlexTelevisionAsync(integration, db, httpClientFactory, runtimePolicy, runtimeMismatchIssueType, runtimePolicyVersion)
            : await SyncPlexMoviesAsync(integration, db, httpClientFactory, runtimePolicy, runtimeMismatchIssueType, runtimePolicyVersion),
        "overseerr" => normalizedScope == "tv"
            ? await SyncOverseerrTelevisionRequestsAsync(integration, db, httpClientFactory)
            : await SyncOverseerrMovieRequestsAsync(integration, db, httpClientFactory),
        _ => new IntegrationSyncOutcome(false, DateTimeOffset.UtcNow, $"Sync adapter not implemented yet for service '{integration.ServiceKey}'.")
    };
}

static string NormalizeIntegrationSyncScope(string? mediaScope)
    => string.Equals(mediaScope, "tv", StringComparison.OrdinalIgnoreCase) ? "tv" : "movies";

static async Task<Dictionary<int, string>> FetchRadarrQualityProfileNamesAsync(IntegrationConfig integration, IHttpClientFactory httpClientFactory)
{
    var client = httpClientFactory.CreateClient();
    client.Timeout = TimeSpan.FromSeconds(30);

    using var request = new HttpRequestMessage(HttpMethod.Get, $"{integration.BaseUrl.TrimEnd('/')}/api/v3/qualityprofile");
    ApplyIntegrationAuthHeaders(integration, request);

    using var response = await client.SendAsync(request);
    if (!response.IsSuccessStatusCode)
    {
        return new Dictionary<int, string>();
    }

    var payload = await response.Content.ReadAsStringAsync();
    using var document = JsonDocument.Parse(payload);
    if (document.RootElement.ValueKind != JsonValueKind.Array)
    {
        return new Dictionary<int, string>();
    }

    var profiles = new Dictionary<int, string>();
    foreach (var profile in document.RootElement.EnumerateArray())
    {
        var id = GetJsonInt(profile, "id");
        var name = GetJsonString(profile, "name");
        if (!id.HasValue || string.IsNullOrWhiteSpace(name))
        {
            continue;
        }

        profiles[id.Value] = name;
    }

    return profiles;
}

static async Task<SonarrCollectionFetchResult> FetchSonarrCollectionBySeriesAsync(
    HttpClient client,
    IntegrationConfig integration,
    string collectionName,
    IReadOnlyCollection<int> seriesIds,
    string itemLabel)
{
    var items = new List<JsonElement>();
    if (seriesIds.Count == 0)
    {
        return new SonarrCollectionFetchResult(true, string.Empty, items);
    }

    foreach (var seriesId in seriesIds.Distinct())
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{integration.BaseUrl.TrimEnd('/')}/api/v3/{collectionName}?seriesId={seriesId}");
        ApplyIntegrationAuthHeaders(integration, request);

        using var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            var error = string.IsNullOrWhiteSpace(body)
                ? $"Sonarr {itemLabel} sync failed for series {seriesId} with HTTP {(int)response.StatusCode}."
                : body[..Math.Min(250, body.Length)];
            return new SonarrCollectionFetchResult(false, error, []);
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return new SonarrCollectionFetchResult(false, $"Unexpected Sonarr {itemLabel} payload for series {seriesId}.", []);
        }

        items.AddRange(document.RootElement.EnumerateArray().Select(x => x.Clone()));
    }

    return new SonarrCollectionFetchResult(true, string.Empty, items);
}

static async Task<IntegrationSyncOutcome> SyncRadarrMoviesAsync(
    IntegrationConfig integration,
    MediaCloudDbContext db,
    IHttpClientFactory httpClientFactory,
    RuntimePolicyValues runtimePolicy,
    string runtimeMismatchIssueType,
    string runtimePolicyVersion)
{
    var client = httpClientFactory.CreateClient();
    client.Timeout = TimeSpan.FromSeconds(45);

    var uri = $"{integration.BaseUrl.TrimEnd('/')}/api/v3/movie?includeMovieFile=true";
    using var httpRequest = new HttpRequestMessage(HttpMethod.Get, uri);
    ApplyIntegrationAuthHeaders(integration, httpRequest);

    using var response = await client.SendAsync(httpRequest);
    if (!response.IsSuccessStatusCode)
    {
        var body = await response.Content.ReadAsStringAsync();
        var error = string.IsNullOrWhiteSpace(body)
            ? $"Sync failed with HTTP {(int)response.StatusCode}."
            : body[..Math.Min(250, body.Length)];
        return new IntegrationSyncOutcome(false, DateTimeOffset.UtcNow, error);
    }

    var payload = await response.Content.ReadAsStringAsync();
    using var document = JsonDocument.Parse(payload);
    if (document.RootElement.ValueKind != JsonValueKind.Array)
    {
        return new IntegrationSyncOutcome(false, DateTimeOffset.UtcNow, "Unexpected Radarr response payload.");
    }

    var qualityProfileNames = await FetchRadarrQualityProfileNamesAsync(integration, httpClientFactory);
    var processed = 0;
    var syncSeenAt = DateTimeOffset.UtcNow;
    var pathMapping = await db.LibraryPathMappings.FirstOrDefaultAsync(x => x.IntegrationId == integration.Id);

    foreach (var movie in document.RootElement.EnumerateArray())
    {
        if (!movie.TryGetProperty("id", out var idElement))
        {
            continue;
        }

        var externalId = idElement.ToString();
        if (string.IsNullOrWhiteSpace(externalId))
        {
            continue;
        }

        var title = GetJsonString(movie, "title") ?? "Unknown";
        var sortTitle = GetJsonString(movie, "sortTitle") ?? title;
        var year = GetJsonInt(movie, "year");
        var tmdbId = GetJsonInt(movie, "tmdbId");
        var imdbId = GetJsonString(movie, "imdbId") ?? string.Empty;
        var runtime = GetJsonDouble(movie, "runtime");
        var overview = GetJsonString(movie, "overview") ?? string.Empty;
        var hasFile = GetJsonBool(movie, "hasFile") ?? false;
        var rawFilePath = GetNestedJsonString(movie, "movieFile", "path") ?? string.Empty;
        var resolvedFilePath = ResolveLocalPath(pathMapping, rawFilePath);
        var qualityProfileId = GetJsonInt(movie, "qualityProfileId");
        var qualityProfile = qualityProfileId.HasValue && qualityProfileNames.TryGetValue(qualityProfileId.Value, out var qualityProfileName)
            ? qualityProfileName
            : (qualityProfileId?.ToString() ?? string.Empty);
        var sourceUpdatedAt = ParseJsonDateTimeOffset(movie, "added");

        var libraryItem = await GetOrCreateLibraryItemAsync(db, BuildCanonicalMovieKey(tmdbId, imdbId, title, year), syncSeenAt, tmdbId, imdbId, title, year);
        libraryItem.MediaType = "Movie";
        libraryItem.Title = title;
        libraryItem.SortTitle = sortTitle;
        libraryItem.Year = year;
        libraryItem.TmdbId = tmdbId;
        libraryItem.ImdbId = imdbId;
        ApplyPreferredDescription(libraryItem, "radarr", overview);
        libraryItem.RuntimeMinutes = runtime;

        var previousFilePath = libraryItem.PrimaryFilePath;
        libraryItem.PrimaryFilePath = resolvedFilePath;
        if (hasFile && !string.IsNullOrWhiteSpace(resolvedFilePath) &&
            (libraryItem.ActualRuntimeMinutes is null || !string.Equals(previousFilePath, resolvedFilePath, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(libraryItem.PlayabilityScore)))
        {
            var probe = ProbeMediaFile(resolvedFilePath);
            libraryItem.ActualRuntimeMinutes = probe.RuntimeMinutes;
            ApplyPlayabilityProbe(libraryItem, probe, syncSeenAt);
        }
        else if (!hasFile || string.IsNullOrWhiteSpace(resolvedFilePath))
        {
            libraryItem.ActualRuntimeMinutes = null;
            ClearPlayability(libraryItem);
        }

        libraryItem.IsAvailable = hasFile;
        libraryItem.QualityProfile = qualityProfile;
        libraryItem.SourceUpdatedAtUtc = sourceUpdatedAt;
        libraryItem.UpdatedAtUtc = syncSeenAt;

        await UpsertLibrarySourceLinkAsync(db, libraryItem.Id, integration.Id, title, sortTitle, externalId, "movie", sourceUpdatedAt, syncSeenAt, externalId);
        await UpsertRuntimeMismatchIssueAsync(db, libraryItem, runtimePolicy, syncSeenAt, runtimeMismatchIssueType, runtimePolicyVersion);

        processed += 1;
    }

    await db.SaveChangesAsync();
    return new IntegrationSyncOutcome(true, syncSeenAt, $"Synced {processed} Radarr item(s).", processed);
}

static async Task<IntegrationSyncOutcome> SyncSonarrTelevisionAsync(
    IntegrationConfig integration,
    MediaCloudDbContext db,
    IHttpClientFactory httpClientFactory,
    RuntimePolicyValues runtimePolicy,
    string runtimeMismatchIssueType,
    string runtimePolicyVersion)
{
    var client = httpClientFactory.CreateClient();
    client.Timeout = TimeSpan.FromSeconds(60);

    var syncSeenAt = DateTimeOffset.UtcNow;
    var pathMapping = await db.LibraryPathMappings.FirstOrDefaultAsync(x => x.IntegrationId == integration.Id);
    var qualityProfileNames = await FetchRadarrQualityProfileNamesAsync(integration, httpClientFactory);

    using var seriesRequest = new HttpRequestMessage(HttpMethod.Get, $"{integration.BaseUrl.TrimEnd('/')}/api/v3/series");
    ApplyIntegrationAuthHeaders(integration, seriesRequest);
    using var seriesResponse = await client.SendAsync(seriesRequest);
    if (!seriesResponse.IsSuccessStatusCode)
    {
        var body = await seriesResponse.Content.ReadAsStringAsync();
        var error = string.IsNullOrWhiteSpace(body)
            ? $"Sonarr series sync failed with HTTP {(int)seriesResponse.StatusCode}."
            : body[..Math.Min(250, body.Length)];
        return new IntegrationSyncOutcome(false, DateTimeOffset.UtcNow, error);
    }

    using var seriesDocument = JsonDocument.Parse(await seriesResponse.Content.ReadAsStringAsync());
    if (seriesDocument.RootElement.ValueKind != JsonValueKind.Array)
    {
        return new IntegrationSyncOutcome(false, DateTimeOffset.UtcNow, "Unexpected Sonarr series payload.");
    }

    var seriesContext = new Dictionary<int, SonarrSeriesContext>();
    var seriesIds = new List<int>();
    var seriesCount = 0;

    foreach (var series in seriesDocument.RootElement.EnumerateArray())
    {
        var externalId = GetJsonInt(series, "id");
        if (!externalId.HasValue)
        {
            continue;
        }

        var title = GetJsonString(series, "title") ?? "Unknown";
        var sortTitle = GetJsonString(series, "sortTitle") ?? title;
        var year = GetJsonInt(series, "year");
        var tvdbId = GetJsonInt(series, "tvdbId");
        var tmdbId = GetJsonInt(series, "tmdbId");
        var imdbId = GetJsonString(series, "imdbId") ?? string.Empty;
        var runtime = GetJsonDouble(series, "runtime") ?? (double?)GetJsonInt(series, "runtime");
        var overview = GetJsonString(series, "overview") ?? string.Empty;
        var qualityProfileId = GetJsonInt(series, "qualityProfileId");
        var qualityProfile = qualityProfileId.HasValue && qualityProfileNames.TryGetValue(qualityProfileId.Value, out var qualityProfileName)
            ? qualityProfileName
            : (qualityProfileId?.ToString() ?? string.Empty);
        var sourceUpdatedAt = ParseJsonDateTimeOffset(series, "added");
        var rawSeriesPath = GetJsonString(series, "path") ?? string.Empty;
        var resolvedSeriesPath = ResolveLocalPath(pathMapping, rawSeriesPath);
        var episodeFileCount = GetNestedJsonInt(series, "statistics", "episodeFileCount") ?? 0;

        var item = await GetOrCreateTelevisionItemAsync(
            db,
            "Series",
            BuildSeriesCanonicalKey(tvdbId, tmdbId, imdbId, title, year),
            syncSeenAt,
            tvdbId,
            tmdbId,
            imdbId,
            title,
            year);

        item.MediaType = "Series";
        item.Title = title;
        item.SortTitle = sortTitle;
        item.Year = year;
        item.TvdbId = tvdbId;
        item.TmdbId = tmdbId;
        item.ImdbId = imdbId;
        ApplyPreferredDescription(item, "sonarr", overview);
        item.RuntimeMinutes = runtime;
        item.ActualRuntimeMinutes = null;
        item.PrimaryFilePath = resolvedSeriesPath;
        item.IsAvailable = episodeFileCount > 0;
        item.QualityProfile = qualityProfile;
        item.SourceUpdatedAtUtc = sourceUpdatedAt;
        item.UpdatedAtUtc = syncSeenAt;

        await UpsertLibrarySourceLinkAsync(db, item.Id, integration.Id, title, sortTitle, externalId.Value.ToString(), "series", sourceUpdatedAt, syncSeenAt, externalId.Value.ToString());

        seriesIds.Add(externalId.Value);
        seriesContext[externalId.Value] = new SonarrSeriesContext(title, sortTitle, year, tvdbId, tmdbId, imdbId, qualityProfile);
        seriesCount += 1;
    }

    var episodeResult = await FetchSonarrCollectionBySeriesAsync(client, integration, "episode", seriesIds, "episodes");
    if (!episodeResult.Success)
    {
        return new IntegrationSyncOutcome(false, DateTimeOffset.UtcNow, episodeResult.ErrorMessage);
    }

    var episodeFilesResult = await FetchSonarrCollectionBySeriesAsync(client, integration, "episodefile", seriesIds, "episode files");
    if (!episodeFilesResult.Success)
    {
        return new IntegrationSyncOutcome(false, DateTimeOffset.UtcNow, episodeFilesResult.ErrorMessage);
    }

    var episodeFiles = new Dictionary<int, JsonElement>();
    foreach (var episodeFile in episodeFilesResult.Items)
    {
        var id = GetJsonInt(episodeFile, "id");
        if (!id.HasValue)
        {
            continue;
        }

        episodeFiles[id.Value] = episodeFile;
    }

    var episodeCount = 0;
    foreach (var episode in episodeResult.Items)
    {
        var externalId = GetJsonInt(episode, "id");
        var parentSeriesId = GetJsonInt(episode, "seriesId");
        if (!externalId.HasValue || !parentSeriesId.HasValue || !seriesContext.TryGetValue(parentSeriesId.Value, out var parentSeries))
        {
            continue;
        }

        var episodeTitle = GetJsonString(episode, "title") ?? "Unknown Episode";
        var seasonNumber = GetJsonInt(episode, "seasonNumber") ?? 0;
        var episodeNumber = GetJsonInt(episode, "episodeNumber") ?? 0;
        var tvdbId = GetJsonInt(episode, "tvdbId");
        var airDateUtc = ParseJsonDateTimeOffset(episode, "airDateUtc") ?? ParseJsonDateTimeOffset(episode, "airDate");
        var runtime = GetJsonDouble(episode, "runtime") ?? (double?)GetJsonInt(episode, "runtime");
        var episodeFileId = GetJsonInt(episode, "episodeFileId");
        JsonElement episodeFile = default;
        var hasFile = episodeFileId.HasValue && episodeFiles.TryGetValue(episodeFileId.Value, out episodeFile);
        var rawFilePath = hasFile ? (GetJsonString(episodeFile, "path") ?? string.Empty) : string.Empty;
        var resolvedFilePath = ResolveLocalPath(pathMapping, rawFilePath);
        var displayTitle = BuildEpisodeDisplayTitle(parentSeries.Title, seasonNumber, episodeNumber, episodeTitle);
        var sortTitle = BuildEpisodeSortTitle(parentSeries.SortTitle, seasonNumber, episodeNumber, episodeTitle);

        var item = await GetOrCreateTelevisionItemAsync(
            db,
            "Episode",
            BuildEpisodeCanonicalKey(parentSeries.TvdbId, parentSeries.Title, seasonNumber, episodeNumber, tvdbId),
            syncSeenAt,
            tvdbId,
            null,
            string.Empty,
            displayTitle,
            airDateUtc?.Year);

        item.MediaType = "Episode";
        item.Title = displayTitle;
        item.SortTitle = sortTitle;
        item.Year = airDateUtc?.Year;
        item.TvdbId = tvdbId;
        item.TmdbId = null;
        item.ImdbId = string.Empty;
        item.RuntimeMinutes = runtime;

        var previousFilePath = item.PrimaryFilePath;
        item.PrimaryFilePath = resolvedFilePath;
        item.ActualRuntimeMinutes = SonarrEpisodeRuntimeSync.ResolveActualRuntimeMinutes(
            hasFile,
            resolvedFilePath,
            previousFilePath,
            item.ActualRuntimeMinutes);
        if (!hasFile || string.IsNullOrWhiteSpace(resolvedFilePath) || !string.Equals(previousFilePath, resolvedFilePath, StringComparison.Ordinal))
        {
            ClearPlayability(item);
        }

        item.IsAvailable = hasFile;
        item.QualityProfile = parentSeries.QualityProfile;
        item.SourceUpdatedAtUtc = airDateUtc;
        item.UpdatedAtUtc = syncSeenAt;

        await UpsertLibrarySourceLinkAsync(db, item.Id, integration.Id, displayTitle, sortTitle, externalId.Value.ToString(), "episode", airDateUtc, syncSeenAt, externalId.Value.ToString());
        await UpsertRuntimeMismatchIssueAsync(db, item, runtimePolicy, syncSeenAt, runtimeMismatchIssueType, runtimePolicyVersion);
        episodeCount += 1;
    }

    await db.SaveChangesAsync();
    return new IntegrationSyncOutcome(true, syncSeenAt, $"Synced {seriesCount} Sonarr series and {episodeCount} episode(s).", seriesCount + episodeCount);
}

static async Task<IntegrationSyncOutcome> SyncPlexMoviesAsync(
    IntegrationConfig integration,
    MediaCloudDbContext db,
    IHttpClientFactory httpClientFactory,
    RuntimePolicyValues runtimePolicy,
    string runtimeMismatchIssueType,
    string runtimePolicyVersion)
{
    var client = httpClientFactory.CreateClient();
    client.Timeout = TimeSpan.FromSeconds(60);

    var syncSeenAt = DateTimeOffset.UtcNow;
    var processed = 0;

    using (var sectionsRequest = new HttpRequestMessage(HttpMethod.Get, $"{integration.BaseUrl.TrimEnd('/')}/library/sections"))
    {
        ApplyIntegrationAuthHeaders(integration, sectionsRequest);
        using var sectionsResponse = await client.SendAsync(sectionsRequest);
        if (!sectionsResponse.IsSuccessStatusCode)
        {
            var body = await sectionsResponse.Content.ReadAsStringAsync();
            var error = string.IsNullOrWhiteSpace(body)
                ? $"Plex sync failed with HTTP {(int)sectionsResponse.StatusCode}."
                : body[..Math.Min(250, body.Length)];
            return new IntegrationSyncOutcome(false, syncSeenAt, error);
        }

        var sectionsXml = await sectionsResponse.Content.ReadAsStringAsync();
        var sectionsDoc = XDocument.Parse(sectionsXml);
        var movieSections = sectionsDoc.Descendants("Directory")
            .Where(x => string.Equals((string?)x.Attribute("type"), "movie", StringComparison.OrdinalIgnoreCase))
            .Select(x => (string?)x.Attribute("key"))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var sectionKey in movieSections)
        {
            using var itemsRequest = new HttpRequestMessage(HttpMethod.Get, $"{integration.BaseUrl.TrimEnd('/')}/library/sections/{sectionKey}/all?type=1");
            ApplyIntegrationAuthHeaders(integration, itemsRequest);
            using var itemsResponse = await client.SendAsync(itemsRequest);
            if (!itemsResponse.IsSuccessStatusCode)
            {
                continue;
            }

            var itemsXml = await itemsResponse.Content.ReadAsStringAsync();
            var itemsDoc = XDocument.Parse(itemsXml);
            foreach (var video in itemsDoc.Descendants("Video"))
            {
                var ratingKey = GetXmlAttr(video, "ratingKey");
                if (string.IsNullOrWhiteSpace(ratingKey)) continue;

                var title = GetXmlAttr(video, "title") ?? "Unknown";
                var sortTitle = GetXmlAttr(video, "titleSort") ?? title;
                var year = int.TryParse(GetXmlAttr(video, "year"), out var parsedYear) ? parsedYear : (int?)null;
                var runtimeMinutes = ParsePlexDurationMinutes(GetXmlAttr(video, "duration"));
                var summary = GetXmlAttr(video, "summary") ?? string.Empty;
                var updatedAt = ParseUnixSeconds(GetXmlAttr(video, "updatedAt")) ?? syncSeenAt;

                var guidIds = video.Descendants("Guid")
                    .Select(x => GetXmlAttr(x, "id") ?? string.Empty)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();
                var imdbId = ExtractProviderId(guidIds, "imdb://") ?? string.Empty;
                var tmdbRaw = ExtractProviderId(guidIds, "tmdb://");
                var tmdbId = int.TryParse(tmdbRaw, out var parsedTmdb) ? parsedTmdb : (int?)null;

                var audioLanguages = CollectPlexStreamLanguages(video, "2");
                var subtitleLanguages = CollectPlexStreamLanguages(video, "3");

                var canonical = BuildCanonicalMovieKey(tmdbId, imdbId, title, year);
                var item = await GetOrCreateLibraryItemAsync(db, canonical, syncSeenAt, tmdbId, imdbId, title, year);

                item.MediaType = "Movie";
                item.Title = title;
                item.SortTitle = sortTitle;
                item.Year = year;
                if (tmdbId.HasValue) item.TmdbId = tmdbId;
                if (!string.IsNullOrWhiteSpace(imdbId)) item.ImdbId = imdbId;
                item.PlexRatingKey = ratingKey;
                ApplyPreferredDescription(item, "plex", summary);
                item.IsAvailable = true;
                item.SourceUpdatedAtUtc = updatedAt;
                item.UpdatedAtUtc = syncSeenAt;
                if (runtimeMinutes.HasValue && runtimeMinutes.Value > 0 && (!item.RuntimeMinutes.HasValue || item.RuntimeMinutes.Value <= 0))
                {
                    item.RuntimeMinutes = runtimeMinutes;
                }

                item.AudioLanguagesJson = JsonSerializer.Serialize(audioLanguages);
                item.SubtitleLanguagesJson = JsonSerializer.Serialize(subtitleLanguages);

                await UpsertLibrarySourceLinkAsync(db, item.Id, integration.Id, title, sortTitle, ratingKey, "movie", updatedAt, syncSeenAt, ratingKey);
                await UpsertRuntimeMismatchIssueAsync(db, item, runtimePolicy, syncSeenAt, runtimeMismatchIssueType, runtimePolicyVersion);
                processed++;
            }
        }
    }

    await db.SaveChangesAsync();

    var enriched = await EnrichPlexMovieIdsByRatingKeysAsync(db, integration, httpClientFactory, syncSeenAt);
    var message = enriched > 0
        ? $"Synced {processed} Plex movie item(s); enriched IDs for {enriched}."
        : $"Synced {processed} Plex movie item(s).";

    return new IntegrationSyncOutcome(true, syncSeenAt, message, processed);
}

static async Task<IntegrationSyncOutcome> SyncPlexTelevisionAsync(
    IntegrationConfig integration,
    MediaCloudDbContext db,
    IHttpClientFactory httpClientFactory,
    RuntimePolicyValues runtimePolicy,
    string runtimeMismatchIssueType,
    string runtimePolicyVersion)
{
    var client = httpClientFactory.CreateClient();
    client.Timeout = TimeSpan.FromSeconds(60);

    var syncSeenAt = DateTimeOffset.UtcNow;
    var processed = 0;

    using (var sectionsRequest = new HttpRequestMessage(HttpMethod.Get, $"{integration.BaseUrl.TrimEnd('/')}/library/sections"))
    {
        ApplyIntegrationAuthHeaders(integration, sectionsRequest);
        using var sectionsResponse = await client.SendAsync(sectionsRequest);
        if (!sectionsResponse.IsSuccessStatusCode)
        {
            var body = await sectionsResponse.Content.ReadAsStringAsync();
            var error = string.IsNullOrWhiteSpace(body)
                ? $"Plex TV sync failed with HTTP {(int)sectionsResponse.StatusCode}."
                : body[..Math.Min(250, body.Length)];
            return new IntegrationSyncOutcome(false, syncSeenAt, error);
        }

        var sectionsXml = await sectionsResponse.Content.ReadAsStringAsync();
        var sectionsDoc = XDocument.Parse(sectionsXml);
        var showSections = sectionsDoc.Descendants("Directory")
            .Where(x => string.Equals((string?)x.Attribute("type"), "show", StringComparison.OrdinalIgnoreCase))
            .Select(x => (string?)x.Attribute("key"))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var sectionKey in showSections)
        {
            using var itemsRequest = new HttpRequestMessage(HttpMethod.Get, $"{integration.BaseUrl.TrimEnd('/')}/library/sections/{sectionKey}/all?type=2");
            ApplyIntegrationAuthHeaders(integration, itemsRequest);
            using var itemsResponse = await client.SendAsync(itemsRequest);
            if (!itemsResponse.IsSuccessStatusCode)
            {
                continue;
            }

            var itemsXml = await itemsResponse.Content.ReadAsStringAsync();
            var itemsDoc = XDocument.Parse(itemsXml);
            foreach (var show in itemsDoc.Descendants("Directory"))
            {
                var ratingKey = GetXmlAttr(show, "ratingKey");
                if (string.IsNullOrWhiteSpace(ratingKey)) continue;

                var title = GetXmlAttr(show, "title") ?? "Unknown";
                var sortTitle = GetXmlAttr(show, "titleSort") ?? title;
                var year = int.TryParse(GetXmlAttr(show, "year"), out var parsedYear) ? parsedYear : (int?)null;
                var runtimeMinutes = ParsePlexDurationMinutes(GetXmlAttr(show, "duration"));
                var summary = GetXmlAttr(show, "summary") ?? string.Empty;
                var updatedAt = ParseUnixSeconds(GetXmlAttr(show, "updatedAt")) ?? syncSeenAt;

                var guidIds = show.Descendants("Guid")
                    .Select(x => GetXmlAttr(x, "id") ?? string.Empty)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();
                var imdbId = ExtractProviderId(guidIds, "imdb://") ?? string.Empty;
                var tmdbRaw = ExtractProviderId(guidIds, "tmdb://");
                var tvdbRaw = ExtractProviderId(guidIds, "tvdb://");
                var tmdbId = int.TryParse(tmdbRaw, out var parsedTmdb) ? parsedTmdb : (int?)null;
                var tvdbId = int.TryParse(tvdbRaw, out var parsedTvdb) ? parsedTvdb : (int?)null;

                var item = await GetOrCreateTelevisionItemAsync(
                    db,
                    "Series",
                    BuildSeriesCanonicalKey(tvdbId, tmdbId, imdbId, title, year),
                    syncSeenAt,
                    tvdbId,
                    tmdbId,
                    imdbId,
                    title,
                    year);

                item.MediaType = "Series";
                item.Title = title;
                item.SortTitle = sortTitle;
                item.Year = year;
                item.TvdbId = tvdbId;
                item.TmdbId = tmdbId;
                item.ImdbId = imdbId;
                item.PlexRatingKey = ratingKey;
                ApplyPreferredDescription(item, "plex", summary);
                item.IsAvailable = true;
                item.SourceUpdatedAtUtc = updatedAt;
                item.UpdatedAtUtc = syncSeenAt;
                if (runtimeMinutes.HasValue && runtimeMinutes.Value > 0 && (!item.RuntimeMinutes.HasValue || item.RuntimeMinutes.Value <= 0))
                {
                    item.RuntimeMinutes = runtimeMinutes;
                }

                await UpsertLibrarySourceLinkAsync(db, item.Id, integration.Id, title, sortTitle, ratingKey, "series", updatedAt, syncSeenAt, ratingKey);
                await UpsertRuntimeMismatchIssueAsync(db, item, runtimePolicy, syncSeenAt, runtimeMismatchIssueType, runtimePolicyVersion);
                processed++;
            }
        }
    }

    await db.SaveChangesAsync();
    return new IntegrationSyncOutcome(true, syncSeenAt, $"Synced {processed} Plex TV series item(s).", processed);
}

static async Task<IntegrationSyncOutcome> SyncOverseerrTelevisionRequestsAsync(
    IntegrationConfig integration,
    MediaCloudDbContext db,
    IHttpClientFactory httpClientFactory)
{
    var client = httpClientFactory.CreateClient();
    client.Timeout = TimeSpan.FromSeconds(45);
    var syncSeenAt = DateTimeOffset.UtcNow;

    using var request = new HttpRequestMessage(HttpMethod.Get, $"{integration.BaseUrl.TrimEnd('/')}/api/v1/request?take=500&skip=0");
    ApplyIntegrationAuthHeaders(integration, request);
    using var response = await client.SendAsync(request);
    if (!response.IsSuccessStatusCode)
    {
        var body = await response.Content.ReadAsStringAsync();
        var error = string.IsNullOrWhiteSpace(body)
            ? $"Overseerr TV sync failed with HTTP {(int)response.StatusCode}."
            : body[..Math.Min(250, body.Length)];
        return new IntegrationSyncOutcome(false, syncSeenAt, error);
    }

    var payload = await response.Content.ReadAsStringAsync();
    using var doc = JsonDocument.Parse(payload);
    if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
    {
        return new IntegrationSyncOutcome(false, syncSeenAt, "Unexpected Overseerr response payload.");
    }

    var processed = 0;
    foreach (var row in results.EnumerateArray())
    {
        var requestId = GetJsonInt(row, "id")?.ToString();
        if (string.IsNullOrWhiteSpace(requestId)) continue;

        if (!row.TryGetProperty("media", out var media) || media.ValueKind != JsonValueKind.Object)
        {
            continue;
        }

        var mediaType = GetJsonString(media, "mediaType") ?? string.Empty;
        if (!string.Equals(mediaType, "tv", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        var tmdbId = GetJsonInt(media, "tmdbId");
        var tvdbId = GetJsonInt(media, "tvdbId");
        var imdbId = GetJsonString(media, "imdbId") ?? string.Empty;
        var title = GetJsonString(media, "title")
            ?? GetJsonString(media, "name")
            ?? GetJsonString(row, "subject")
            ?? "Unknown";
        var overview = GetJsonString(media, "overview") ?? string.Empty;
        var year = ParseYearFromDate(GetJsonString(media, "firstAirDate") ?? GetJsonString(media, "releaseDate"));
        var status = GetJsonInt(row, "status")?.ToString() ?? "unknown";
        var requestedAt = ParseJsonDateTimeOffset(row, "createdAt") ?? syncSeenAt;

        var item = await GetOrCreateTelevisionItemAsync(
            db,
            "Series",
            BuildSeriesCanonicalKey(tvdbId, tmdbId, imdbId, title, year),
            syncSeenAt,
            tvdbId,
            tmdbId,
            imdbId,
            title,
            year);

        item.MediaType = "Series";
        if (string.IsNullOrWhiteSpace(item.Title) || string.Equals(item.Title, "Unknown", StringComparison.OrdinalIgnoreCase))
        {
            item.Title = title;
            item.SortTitle = title;
            item.Year = year;
        }
        if (tvdbId.HasValue) item.TvdbId = tvdbId;
        if (tmdbId.HasValue) item.TmdbId = tmdbId;
        if (!string.IsNullOrWhiteSpace(imdbId)) item.ImdbId = imdbId;
        ApplyPreferredDescription(item, "overseerr", overview);
        item.UpdatedAtUtc = syncSeenAt;

        await UpsertLibrarySourceLinkAsync(db, item.Id, integration.Id, title, title, requestId, "overseerr_request", requestedAt, syncSeenAt, status);
        processed++;
    }

    await db.SaveChangesAsync();
    return new IntegrationSyncOutcome(true, syncSeenAt, $"Synced {processed} Overseerr TV request(s).", processed);
}

static async Task<IntegrationSyncOutcome> SyncOverseerrMovieRequestsAsync(
    IntegrationConfig integration,
    MediaCloudDbContext db,
    IHttpClientFactory httpClientFactory)
{
    var client = httpClientFactory.CreateClient();
    client.Timeout = TimeSpan.FromSeconds(45);
    var syncSeenAt = DateTimeOffset.UtcNow;

    using var request = new HttpRequestMessage(HttpMethod.Get, $"{integration.BaseUrl.TrimEnd('/')}/api/v1/request?take=500&skip=0");
    ApplyIntegrationAuthHeaders(integration, request);
    using var response = await client.SendAsync(request);
    if (!response.IsSuccessStatusCode)
    {
        var body = await response.Content.ReadAsStringAsync();
        var error = string.IsNullOrWhiteSpace(body)
            ? $"Overseerr sync failed with HTTP {(int)response.StatusCode}."
            : body[..Math.Min(250, body.Length)];
        return new IntegrationSyncOutcome(false, syncSeenAt, error);
    }

    var payload = await response.Content.ReadAsStringAsync();
    using var doc = JsonDocument.Parse(payload);
    if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
    {
        return new IntegrationSyncOutcome(false, syncSeenAt, "Unexpected Overseerr response payload.");
    }

    var processed = 0;
    foreach (var row in results.EnumerateArray())
    {
        var requestId = GetJsonInt(row, "id")?.ToString();
        if (string.IsNullOrWhiteSpace(requestId)) continue;

        if (!row.TryGetProperty("media", out var media) || media.ValueKind != JsonValueKind.Object)
        {
            continue;
        }

        var mediaType = GetJsonString(media, "mediaType") ?? string.Empty;
        if (!string.Equals(mediaType, "movie", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        var tmdbId = GetJsonInt(media, "tmdbId");
        var imdbId = GetJsonString(media, "imdbId") ?? string.Empty;
        var title = GetJsonString(media, "title") ?? GetJsonString(row, "subject") ?? "Unknown";
        var overview = GetJsonString(media, "overview") ?? string.Empty;
        var year = ParseYearFromDate(GetJsonString(media, "releaseDate"));
        var status = GetJsonInt(row, "status")?.ToString() ?? "unknown";
        var requestedAt = ParseJsonDateTimeOffset(row, "createdAt") ?? syncSeenAt;

        var canonical = BuildCanonicalMovieKey(tmdbId, imdbId, title, year);
        var item = await GetOrCreateLibraryItemAsync(db, canonical, syncSeenAt, tmdbId, imdbId, title, year);
        item.MediaType = "Movie";
        if (string.IsNullOrWhiteSpace(item.Title) || string.Equals(item.Title, "Unknown", StringComparison.OrdinalIgnoreCase))
        {
            item.Title = title;
            item.SortTitle = title;
            item.Year = year;
        }
        if (tmdbId.HasValue) item.TmdbId = tmdbId;
        if (!string.IsNullOrWhiteSpace(imdbId)) item.ImdbId = imdbId;
        ApplyPreferredDescription(item, "overseerr", overview);
        item.UpdatedAtUtc = syncSeenAt;

        await UpsertLibrarySourceLinkAsync(db, item.Id, integration.Id, title, title, requestId, "overseerr_request", requestedAt, syncSeenAt, status);
        processed++;
    }

    await db.SaveChangesAsync();
    return new IntegrationSyncOutcome(true, syncSeenAt, $"Synced {processed} Overseerr movie request(s).", processed);
}

static async Task<LibraryItem> GetOrCreateTelevisionItemAsync(
    MediaCloudDbContext db,
    string mediaType,
    string canonicalKey,
    DateTimeOffset createdAtUtc,
    int? tvdbId,
    int? tmdbId,
    string imdbId,
    string title,
    int? year)
{
    var item = await FindExistingTelevisionItemAsync(db, mediaType, canonicalKey, tvdbId, tmdbId, imdbId, title, year);
    if (item is not null)
    {
        if (!string.Equals(item.CanonicalKey, canonicalKey, StringComparison.Ordinal))
        {
            var canonicalTaken = await db.LibraryItems.AnyAsync(x => x.CanonicalKey == canonicalKey && x.Id != item.Id);
            if (!canonicalTaken)
            {
                item.CanonicalKey = canonicalKey;
            }
        }

        return item;
    }

    item = new LibraryItem
    {
        CanonicalKey = canonicalKey,
        CreatedAtUtc = createdAtUtc
    };
    db.LibraryItems.Add(item);
    await db.SaveChangesAsync();
    return item;
}

static async Task<LibraryItem> GetOrCreateLibraryItemAsync(
    MediaCloudDbContext db,
    string canonicalKey,
    DateTimeOffset createdAtUtc,
    int? tmdbId,
    string imdbId,
    string title,
    int? year)
{
    var item = await FindExistingMovieItemAsync(db, tmdbId, imdbId, title, year);
    if (item is null)
    {
        item = await db.LibraryItems.FirstOrDefaultAsync(x => x.CanonicalKey == canonicalKey);
    }

    if (item is not null)
    {
        if (!string.Equals(item.CanonicalKey, canonicalKey, StringComparison.Ordinal))
        {
            var canonicalTaken = await db.LibraryItems.AnyAsync(x => x.CanonicalKey == canonicalKey && x.Id != item.Id);
            if (!canonicalTaken)
            {
                item.CanonicalKey = canonicalKey;
            }
        }

        return item;
    }

    item = new LibraryItem
    {
        CanonicalKey = canonicalKey,
        CreatedAtUtc = createdAtUtc
    };
    db.LibraryItems.Add(item);
    await db.SaveChangesAsync();
    return item;
}

static async Task<LibraryItem?> FindExistingTelevisionItemAsync(
    MediaCloudDbContext db,
    string mediaType,
    string canonicalKey,
    int? tvdbId,
    int? tmdbId,
    string imdbId,
    string title,
    int? year)
{
    if (tvdbId.HasValue && tvdbId.Value > 0)
    {
        var byTvdb = await db.LibraryItems
            .Where(x => x.MediaType == mediaType && x.TvdbId == tvdbId.Value)
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync();
        if (byTvdb is not null) return byTvdb;
    }

    if (tmdbId.HasValue && tmdbId.Value > 0)
    {
        var byTmdb = await db.LibraryItems
            .Where(x => x.MediaType == mediaType && x.TmdbId == tmdbId.Value)
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync();
        if (byTmdb is not null) return byTmdb;
    }

    var normalizedImdb = (imdbId ?? string.Empty).Trim().ToLowerInvariant();
    if (!string.IsNullOrWhiteSpace(normalizedImdb))
    {
        var byImdb = await db.LibraryItems
            .Where(x => x.MediaType == mediaType && x.ImdbId.ToLower() == normalizedImdb)
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync();
        if (byImdb is not null) return byImdb;
    }

    var byCanonical = await db.LibraryItems
        .Where(x => x.MediaType == mediaType && x.CanonicalKey == canonicalKey)
        .OrderBy(x => x.Id)
        .FirstOrDefaultAsync();
    if (byCanonical is not null)
    {
        return byCanonical;
    }

    if (!string.Equals(mediaType, "Series", StringComparison.OrdinalIgnoreCase))
    {
        return null;
    }

    var normalizedTitle = NormalizeTitleKey(title);
    if (string.IsNullOrWhiteSpace(normalizedTitle) || !year.HasValue)
    {
        return null;
    }

    var candidates = await db.LibraryItems
        .Where(x => x.MediaType == mediaType && x.Year == year.Value)
        .OrderBy(x => x.Id)
        .ToListAsync();

    return candidates.FirstOrDefault(x =>
        string.Equals(NormalizeTitleKey(x.Title), normalizedTitle, StringComparison.Ordinal) ||
        string.Equals(NormalizeTitleKey(x.SortTitle), normalizedTitle, StringComparison.Ordinal));
}

static async Task<LibraryItem?> FindExistingMovieItemAsync(
    MediaCloudDbContext db,
    int? tmdbId,
    string imdbId,
    string title,
    int? year)
{
    if (tmdbId.HasValue && tmdbId.Value > 0)
    {
        var byTmdb = await db.LibraryItems
            .Where(x => x.MediaType == "Movie" && x.TmdbId == tmdbId.Value)
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync();
        if (byTmdb is not null) return byTmdb;
    }

    var normalizedImdb = (imdbId ?? string.Empty).Trim().ToLowerInvariant();
    if (!string.IsNullOrWhiteSpace(normalizedImdb))
    {
        var byImdb = await db.LibraryItems
            .Where(x => x.MediaType == "Movie" && x.ImdbId.ToLower() == normalizedImdb)
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync();
        if (byImdb is not null) return byImdb;
    }

    var normalizedTitle = NormalizeTitleKey(title);
    if (string.IsNullOrWhiteSpace(normalizedTitle) || !year.HasValue)
    {
        return null;
    }

    var candidates = await db.LibraryItems
        .Where(x => x.MediaType == "Movie" && x.Year == year.Value)
        .OrderBy(x => x.Id)
        .ToListAsync();

    return candidates.FirstOrDefault(x =>
        string.Equals(NormalizeTitleKey(x.Title), normalizedTitle, StringComparison.Ordinal) ||
        string.Equals(NormalizeTitleKey(x.SortTitle), normalizedTitle, StringComparison.Ordinal));
}

static async Task UpsertLibrarySourceLinkAsync(
    MediaCloudDbContext db,
    long libraryItemId,
    long integrationId,
    string sourceTitle,
    string sourceSortTitle,
    string externalId,
    string externalType,
    DateTimeOffset? externalUpdatedAtUtc,
    DateTimeOffset seenAtUtc,
    string payloadHash)
{
    var link = await db.LibraryItemSourceLinks.FirstOrDefaultAsync(x =>
        x.LibraryItemId == libraryItemId &&
        x.IntegrationId == integrationId &&
        x.ExternalId == externalId);

    if (link is null)
    {
        link = new LibraryItemSourceLink
        {
            LibraryItemId = libraryItemId,
            IntegrationId = integrationId,
            SourceTitle = string.Empty,
            SourceSortTitle = string.Empty,
            ExternalId = externalId,
            FirstSeenAtUtc = seenAtUtc
        };
        db.LibraryItemSourceLinks.Add(link);
    }

    link.SourceTitle = sourceTitle;
    link.SourceSortTitle = sourceSortTitle;
    link.ExternalType = externalType;
    link.ExternalUpdatedAtUtc = externalUpdatedAtUtc;
    link.LastSeenAtUtc = seenAtUtc;
    link.IsDeletedAtSource = false;
    link.SourcePayloadHash = payloadHash;
}

static string? GetXmlAttr(XElement element, string name)
    => element.Attribute(name)?.Value;

static double? ParsePlexDurationMinutes(string? durationMs)
{
    if (!double.TryParse(durationMs, out var ms) || ms <= 0) return null;
    return Math.Round(ms / 60000d, 2);
}

static DateTimeOffset? ParseUnixSeconds(string? raw)
{
    if (!long.TryParse(raw, out var seconds) || seconds <= 0) return null;
    return DateTimeOffset.FromUnixTimeSeconds(seconds);
}

static string? ExtractProviderId(IEnumerable<string> guidIds, string prefix)
{
    foreach (var guid in guidIds)
    {
        if (guid.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return guid[prefix.Length..];
        }
    }

    return null;
}

static IReadOnlyList<string> CollectPlexStreamLanguages(XElement video, string streamType)
{
    var values = video.Descendants("Stream")
        .Where(x => string.Equals(GetXmlAttr(x, "streamType"), streamType, StringComparison.OrdinalIgnoreCase))
        .Select(x => GetXmlAttr(x, "languageTag") ?? GetXmlAttr(x, "languageCode") ?? GetXmlAttr(x, "language"))
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(x => x!.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(x => x)
        .ToList();

    return values;
}

static int? ParseYearFromDate(string? releaseDate)
{
    if (string.IsNullOrWhiteSpace(releaseDate)) return null;
    if (DateTimeOffset.TryParse(releaseDate, out var parsed)) return parsed.Year;
    return int.TryParse(releaseDate[..Math.Min(4, releaseDate.Length)], out var year) ? year : null;
}

static string NormalizeTitleKey(string? raw)
{
    if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

    var chars = raw.Trim().ToLowerInvariant()
        .Where(char.IsLetterOrDigit)
        .ToArray();

    return new string(chars);
}

static string BuildLibraryDisplayTitle(LibraryItem item, IReadOnlyList<LibraryItemSourceTitleInfo>? sourceTitles)
{
    if (string.Equals(item.MediaType, "Movie", StringComparison.OrdinalIgnoreCase))
    {
        return BuildPreferredDisplayTitle(item.Title, sourceTitles, "radarr", "plex");
    }

    if (string.Equals(item.MediaType, "Series", StringComparison.OrdinalIgnoreCase))
    {
        return BuildPreferredDisplayTitle(item.Title, sourceTitles, "sonarr", "plex");
    }

    if (string.Equals(item.MediaType, "Episode", StringComparison.OrdinalIgnoreCase))
    {
        return BuildPreferredDisplayTitle(item.Title, sourceTitles, "sonarr", "plex");
    }

    return item.Title;
}

static string BuildPreferredDisplayTitle(string fallbackTitle, IReadOnlyList<LibraryItemSourceTitleInfo>? sourceTitles, string primaryServiceKey, string secondaryServiceKey)
{
    var titles = sourceTitles ?? [];
    var primaryTitle = GetPreferredSourceTitle(titles, primaryServiceKey);
    var secondaryTitle = GetPreferredSourceTitle(titles, secondaryServiceKey);

    if (!string.IsNullOrWhiteSpace(primaryTitle))
    {
        if (!string.IsNullOrWhiteSpace(secondaryTitle) && !TitlesEquivalentForDisplay(primaryTitle, secondaryTitle))
        {
            return $"{primaryTitle} ({secondaryTitle})";
        }

        return primaryTitle;
    }

    if (!string.IsNullOrWhiteSpace(secondaryTitle))
    {
        return secondaryTitle;
    }

    return fallbackTitle;
}

static string GetPreferredSourceTitle(IReadOnlyList<LibraryItemSourceTitleInfo> sourceTitles, string serviceKey)
{
    return sourceTitles
        .Where(x => string.Equals(x.ServiceKey, serviceKey, StringComparison.OrdinalIgnoreCase))
        .Select(x => (x.SourceTitle ?? string.Empty).Trim())
        .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
}

static bool TitlesEquivalentForDisplay(string left, string right)
{
    return string.Equals(NormalizeTitleKey(left), NormalizeTitleKey(right), StringComparison.Ordinal);
}

static async Task ConsolidateMovieDuplicatesAsync(MediaCloudDbContext db, DateTimeOffset consolidatedAtUtc)
{
    var movies = await db.LibraryItems
        .Where(x => x.MediaType == "Movie")
        .OrderBy(x => x.Id)
        .ToListAsync();

    var winnerByTmdb = new Dictionary<int, LibraryItem>();
    var winnerByImdb = new Dictionary<string, LibraryItem>(StringComparer.OrdinalIgnoreCase);
    var winnerByTitleYear = new Dictionary<string, LibraryItem>(StringComparer.Ordinal);

    foreach (var movie in movies)
    {
        var winner = ResolveExistingWinner(movie, winnerByTmdb, winnerByImdb, winnerByTitleYear);
        if (winner is null)
        {
            RegisterWinner(movie, winnerByTmdb, winnerByImdb, winnerByTitleYear);
            continue;
        }

        if (winner.Id == movie.Id)
        {
            RegisterWinner(winner, winnerByTmdb, winnerByImdb, winnerByTitleYear);
            continue;
        }

        await MergeMovieIntoWinnerAsync(db, winner, movie, consolidatedAtUtc);
        RegisterWinner(winner, winnerByTmdb, winnerByImdb, winnerByTitleYear);
    }

    await db.SaveChangesAsync();
}

static async Task ConsolidateTelevisionDuplicatesAsync(MediaCloudDbContext db, DateTimeOffset consolidatedAtUtc)
{
    await ConsolidateSeriesDuplicatesAsync(db, consolidatedAtUtc);
    await ConsolidateEpisodeDuplicatesAsync(db, consolidatedAtUtc);
}

static async Task ConsolidateSeriesDuplicatesAsync(MediaCloudDbContext db, DateTimeOffset consolidatedAtUtc)
{
    var seriesItems = await db.LibraryItems
        .Where(x => x.MediaType == "Series")
        .OrderBy(x => x.Id)
        .ToListAsync();

    var winnerByTvdb = new Dictionary<int, LibraryItem>();
    var winnerByTmdb = new Dictionary<int, LibraryItem>();
    var winnerByImdb = new Dictionary<string, LibraryItem>(StringComparer.OrdinalIgnoreCase);
    var winnerByTitleYear = new Dictionary<string, LibraryItem>(StringComparer.Ordinal);

    foreach (var series in seriesItems)
    {
        var winner = ResolveExistingTelevisionWinner(series, winnerByTvdb, winnerByTmdb, winnerByImdb, winnerByTitleYear);
        if (winner is null)
        {
            RegisterTelevisionWinner(series, winnerByTvdb, winnerByTmdb, winnerByImdb, winnerByTitleYear);
            continue;
        }

        if (winner.Id == series.Id)
        {
            RegisterTelevisionWinner(winner, winnerByTvdb, winnerByTmdb, winnerByImdb, winnerByTitleYear);
            continue;
        }

        await MergeLibraryItemIntoWinnerAsync(db, winner, series, consolidatedAtUtc, preserveTmdb: true);
        RegisterTelevisionWinner(winner, winnerByTvdb, winnerByTmdb, winnerByImdb, winnerByTitleYear);
    }

    await db.SaveChangesAsync();
}

static async Task ConsolidateEpisodeDuplicatesAsync(MediaCloudDbContext db, DateTimeOffset consolidatedAtUtc)
{
    var episodeItems = await db.LibraryItems
        .Where(x => x.MediaType == "Episode")
        .OrderBy(x => x.Id)
        .ToListAsync();

    var winnerByTvdb = new Dictionary<int, LibraryItem>();
    var winnerByCanonical = new Dictionary<string, LibraryItem>(StringComparer.OrdinalIgnoreCase);

    foreach (var episode in episodeItems)
    {
        LibraryItem? winner = null;
        if (episode.TvdbId.HasValue && episode.TvdbId.Value > 0)
        {
            winnerByTvdb.TryGetValue(episode.TvdbId.Value, out winner);
        }

        if (winner is null && !string.IsNullOrWhiteSpace(episode.CanonicalKey))
        {
            winnerByCanonical.TryGetValue(episode.CanonicalKey, out winner);
        }

        if (winner is null)
        {
            if (episode.TvdbId.HasValue && episode.TvdbId.Value > 0)
            {
                winnerByTvdb[episode.TvdbId.Value] = episode;
            }

            if (!string.IsNullOrWhiteSpace(episode.CanonicalKey))
            {
                winnerByCanonical[episode.CanonicalKey] = episode;
            }

            continue;
        }

        if (winner.Id == episode.Id)
        {
            if (episode.TvdbId.HasValue && episode.TvdbId.Value > 0)
            {
                winnerByTvdb[episode.TvdbId.Value] = winner;
            }

            if (!string.IsNullOrWhiteSpace(episode.CanonicalKey))
            {
                winnerByCanonical[episode.CanonicalKey] = winner;
            }

            continue;
        }

        await MergeLibraryItemIntoWinnerAsync(db, winner, episode, consolidatedAtUtc, preserveTmdb: false);
        if (winner.TvdbId.HasValue && winner.TvdbId.Value > 0)
        {
            winnerByTvdb[winner.TvdbId.Value] = winner;
        }

        if (!string.IsNullOrWhiteSpace(winner.CanonicalKey))
        {
            winnerByCanonical[winner.CanonicalKey] = winner;
        }
    }

    await db.SaveChangesAsync();
}

static LibraryItem? ResolveExistingWinner(
    LibraryItem movie,
    IReadOnlyDictionary<int, LibraryItem> winnerByTmdb,
    IReadOnlyDictionary<string, LibraryItem> winnerByImdb,
    IReadOnlyDictionary<string, LibraryItem> winnerByTitleYear)
{
    if (movie.TmdbId.HasValue && movie.TmdbId.Value > 0 && winnerByTmdb.TryGetValue(movie.TmdbId.Value, out var byTmdb))
    {
        return byTmdb;
    }

    var imdbKey = (movie.ImdbId ?? string.Empty).Trim().ToLowerInvariant();
    if (!string.IsNullOrWhiteSpace(imdbKey) && winnerByImdb.TryGetValue(imdbKey, out var byImdb))
    {
        return byImdb;
    }

    var titleYearKey = BuildTitleYearKey(movie);
    if (!string.IsNullOrWhiteSpace(titleYearKey) && winnerByTitleYear.TryGetValue(titleYearKey, out var byTitleYear))
    {
        return byTitleYear;
    }

    return null;
}

static LibraryItem? ResolveExistingTelevisionWinner(
    LibraryItem item,
    IReadOnlyDictionary<int, LibraryItem> winnerByTvdb,
    IReadOnlyDictionary<int, LibraryItem> winnerByTmdb,
    IReadOnlyDictionary<string, LibraryItem> winnerByImdb,
    IReadOnlyDictionary<string, LibraryItem> winnerByTitleYear)
{
    if (item.TvdbId.HasValue && item.TvdbId.Value > 0 && winnerByTvdb.TryGetValue(item.TvdbId.Value, out var byTvdb))
    {
        return byTvdb;
    }

    if (item.TmdbId.HasValue && item.TmdbId.Value > 0 && winnerByTmdb.TryGetValue(item.TmdbId.Value, out var byTmdb))
    {
        return byTmdb;
    }

    var imdbKey = (item.ImdbId ?? string.Empty).Trim().ToLowerInvariant();
    if (!string.IsNullOrWhiteSpace(imdbKey) && winnerByImdb.TryGetValue(imdbKey, out var byImdb))
    {
        return byImdb;
    }

    var titleYearKey = BuildTitleYearKey(item);
    if (!string.IsNullOrWhiteSpace(titleYearKey) && winnerByTitleYear.TryGetValue(titleYearKey, out var byTitleYear))
    {
        return byTitleYear;
    }

    return null;
}

static void RegisterTelevisionWinner(
    LibraryItem winner,
    IDictionary<int, LibraryItem> winnerByTvdb,
    IDictionary<int, LibraryItem> winnerByTmdb,
    IDictionary<string, LibraryItem> winnerByImdb,
    IDictionary<string, LibraryItem> winnerByTitleYear)
{
    if (winner.TvdbId.HasValue && winner.TvdbId.Value > 0)
    {
        winnerByTvdb[winner.TvdbId.Value] = winner;
    }

    if (winner.TmdbId.HasValue && winner.TmdbId.Value > 0)
    {
        winnerByTmdb[winner.TmdbId.Value] = winner;
    }

    var imdbKey = (winner.ImdbId ?? string.Empty).Trim().ToLowerInvariant();
    if (!string.IsNullOrWhiteSpace(imdbKey))
    {
        winnerByImdb[imdbKey] = winner;
    }

    var titleYearKey = BuildTitleYearKey(winner);
    if (!string.IsNullOrWhiteSpace(titleYearKey))
    {
        winnerByTitleYear[titleYearKey] = winner;
    }
}

static void RegisterWinner(
    LibraryItem winner,
    IDictionary<int, LibraryItem> winnerByTmdb,
    IDictionary<string, LibraryItem> winnerByImdb,
    IDictionary<string, LibraryItem> winnerByTitleYear)
{
    if (winner.TmdbId.HasValue && winner.TmdbId.Value > 0)
    {
        winnerByTmdb[winner.TmdbId.Value] = winner;
    }

    var imdbKey = (winner.ImdbId ?? string.Empty).Trim().ToLowerInvariant();
    if (!string.IsNullOrWhiteSpace(imdbKey))
    {
        winnerByImdb[imdbKey] = winner;
    }

    var titleYearKey = BuildTitleYearKey(winner);
    if (!string.IsNullOrWhiteSpace(titleYearKey))
    {
        winnerByTitleYear[titleYearKey] = winner;
    }
}

static async Task MergeMovieIntoWinnerAsync(MediaCloudDbContext db, LibraryItem winner, LibraryItem loser, DateTimeOffset consolidatedAtUtc)
{
    await MergeLibraryItemIntoWinnerAsync(db, winner, loser, consolidatedAtUtc, preserveTmdb: true);
}

static async Task MergeLibraryItemIntoWinnerAsync(MediaCloudDbContext db, LibraryItem winner, LibraryItem loser, DateTimeOffset consolidatedAtUtc, bool preserveTmdb)
{
    if (preserveTmdb)
    {
        winner.TmdbId ??= loser.TmdbId;
    }

    winner.TvdbId ??= loser.TvdbId;
    if (string.IsNullOrWhiteSpace(winner.ImdbId)) winner.ImdbId = loser.ImdbId;
    if (string.IsNullOrWhiteSpace(winner.PlexRatingKey)) winner.PlexRatingKey = loser.PlexRatingKey;
    if (string.IsNullOrWhiteSpace(winner.Title) || string.Equals(winner.Title, "Unknown", StringComparison.OrdinalIgnoreCase)) winner.Title = loser.Title;
    if (string.IsNullOrWhiteSpace(winner.SortTitle)) winner.SortTitle = loser.SortTitle;
    winner.Year ??= loser.Year;
    winner.RuntimeMinutes ??= loser.RuntimeMinutes;
    winner.ActualRuntimeMinutes ??= loser.ActualRuntimeMinutes;
    if (string.IsNullOrWhiteSpace(winner.PrimaryFilePath)) winner.PrimaryFilePath = loser.PrimaryFilePath;
    if ((winner.AudioLanguagesJson ?? "[]") == "[]" && !string.IsNullOrWhiteSpace(loser.AudioLanguagesJson)) winner.AudioLanguagesJson = loser.AudioLanguagesJson;
    if ((winner.SubtitleLanguagesJson ?? "[]") == "[]" && !string.IsNullOrWhiteSpace(loser.SubtitleLanguagesJson)) winner.SubtitleLanguagesJson = loser.SubtitleLanguagesJson;
    if (string.IsNullOrWhiteSpace(winner.PlayabilityScore) && !string.IsNullOrWhiteSpace(loser.PlayabilityScore)) winner.PlayabilityScore = loser.PlayabilityScore;
    if (string.IsNullOrWhiteSpace(winner.PlayabilitySummary) && !string.IsNullOrWhiteSpace(loser.PlayabilitySummary)) winner.PlayabilitySummary = loser.PlayabilitySummary;
    if (string.IsNullOrWhiteSpace(winner.PlayabilityDetailsJson) && !string.IsNullOrWhiteSpace(loser.PlayabilityDetailsJson)) winner.PlayabilityDetailsJson = loser.PlayabilityDetailsJson;
    winner.PlayabilityCheckedAtUtc ??= loser.PlayabilityCheckedAtUtc;
    winner.IsAvailable = winner.IsAvailable || loser.IsAvailable;
    if (string.IsNullOrWhiteSpace(winner.QualityProfile)) winner.QualityProfile = loser.QualityProfile;
    if ((winner.SourceUpdatedAtUtc ?? DateTimeOffset.MinValue) < (loser.SourceUpdatedAtUtc ?? DateTimeOffset.MinValue)) winner.SourceUpdatedAtUtc = loser.SourceUpdatedAtUtc;
    winner.UpdatedAtUtc = consolidatedAtUtc;

    var loserLinks = await db.LibraryItemSourceLinks.Where(x => x.LibraryItemId == loser.Id).ToListAsync();
    foreach (var link in loserLinks)
    {
        var existing = await db.LibraryItemSourceLinks.FirstOrDefaultAsync(x =>
            x.LibraryItemId == winner.Id &&
            x.IntegrationId == link.IntegrationId &&
            x.ExternalId == link.ExternalId);

        if (existing is null)
        {
            link.LibraryItemId = winner.Id;
        }
        else
        {
            if (existing.FirstSeenAtUtc > link.FirstSeenAtUtc) existing.FirstSeenAtUtc = link.FirstSeenAtUtc;
            if (existing.LastSeenAtUtc < link.LastSeenAtUtc) existing.LastSeenAtUtc = link.LastSeenAtUtc;
            existing.IsDeletedAtSource = existing.IsDeletedAtSource && link.IsDeletedAtSource;
            if (string.IsNullOrWhiteSpace(existing.SourceTitle) && !string.IsNullOrWhiteSpace(link.SourceTitle)) existing.SourceTitle = link.SourceTitle;
            if (string.IsNullOrWhiteSpace(existing.SourceSortTitle) && !string.IsNullOrWhiteSpace(link.SourceSortTitle)) existing.SourceSortTitle = link.SourceSortTitle;
            if (!string.IsNullOrWhiteSpace(link.SourcePayloadHash)) existing.SourcePayloadHash = link.SourcePayloadHash;
            db.LibraryItemSourceLinks.Remove(link);
        }
    }

    var loserIssues = await db.LibraryIssues.Where(x => x.LibraryItemId == loser.Id).ToListAsync();
    foreach (var issue in loserIssues)
    {
        issue.LibraryItemId = winner.Id;
    }

    db.LibraryItems.Remove(loser);
}

static string BuildTitleYearKey(LibraryItem item)
{
    var title = NormalizeTitleKey(string.IsNullOrWhiteSpace(item.SortTitle) ? item.Title : item.SortTitle);
    if (string.IsNullOrWhiteSpace(title) || !item.Year.HasValue)
    {
        return string.Empty;
    }

    return $"{title}:{item.Year.Value}";
}

static async Task<bool> HasSourceLinkForServiceAsync(MediaCloudDbContext db, long libraryItemId, string serviceKey)
{
    var normalized = (serviceKey ?? string.Empty).Trim().ToLowerInvariant();
    if (string.IsNullOrWhiteSpace(normalized)) return false;

    return await (
        from link in db.LibraryItemSourceLinks
        join integration in db.IntegrationConfigs on link.IntegrationId equals integration.Id
        where link.LibraryItemId == libraryItemId
            && integration.ServiceKey.ToLower() == normalized
            && !link.IsDeletedAtSource
        select link.Id)
        .AnyAsync();
}

static string GetMovieDesiredMonitoringKey(long libraryItemId)
    => $"monitoring:movie:{libraryItemId}:desired";

static bool ParseBoolOrDefault(string? raw, bool fallback)
{
    if (string.IsNullOrWhiteSpace(raw)) return fallback;
    return raw.Equals("true", StringComparison.OrdinalIgnoreCase)
           || raw.Equals("1", StringComparison.OrdinalIgnoreCase)
           || raw.Equals("yes", StringComparison.OrdinalIgnoreCase)
           || raw.Equals("on", StringComparison.OrdinalIgnoreCase);
}

static async Task<bool?> GetMovieDesiredMonitoringAsync(MediaCloudDbContext db, long libraryItemId)
{
    var key = GetMovieDesiredMonitoringKey(libraryItemId);
    var raw = await db.AppConfigEntries.Where(x => x.Key == key).Select(x => x.Value).FirstOrDefaultAsync();
    if (string.IsNullOrWhiteSpace(raw)) return null;
    return ParseBoolOrDefault(raw, false);
}

static async Task SetMovieDesiredMonitoringAsync(MediaCloudDbContext db, long libraryItemId, bool desiredMonitored, DateTimeOffset now)
{
    var key = GetMovieDesiredMonitoringKey(libraryItemId);
    var setting = await db.AppConfigEntries.FirstOrDefaultAsync(x => x.Key == key);
    if (setting is null)
    {
        setting = new AppConfigEntry { Key = key };
        db.AppConfigEntries.Add(setting);
    }

    setting.Value = desiredMonitored ? "true" : "false";
    setting.UpdatedAtUtc = now;
}

static async Task<bool> GetMonitoringManagedByMediaCloudAsync(MediaCloudDbContext db)
{
    var raw = await db.AppConfigEntries.Where(x => x.Key == monitoringManagedByMediaCloudKey).Select(x => x.Value).FirstOrDefaultAsync();
    return ParseBoolOrDefault(raw, true);
}

static async Task SetMonitoringManagedByMediaCloudAsync(MediaCloudDbContext db, bool enabled, DateTimeOffset now)
{
    var setting = await db.AppConfigEntries.FirstOrDefaultAsync(x => x.Key == monitoringManagedByMediaCloudKey);
    if (setting is null)
    {
        setting = new AppConfigEntry { Key = monitoringManagedByMediaCloudKey };
        db.AppConfigEntries.Add(setting);
    }

    setting.Value = enabled ? "true" : "false";
    setting.UpdatedAtUtc = now;
}

static async Task<bool> GetMonitoringAutoSyncEnabledAsync(MediaCloudDbContext db)
{
    var raw = await db.AppConfigEntries.Where(x => x.Key == monitoringAutoSyncEnabledKey).Select(x => x.Value).FirstOrDefaultAsync();
    return ParseBoolOrDefault(raw, false);
}

static async Task SetMonitoringAutoSyncEnabledAsync(MediaCloudDbContext db, bool enabled, DateTimeOffset now)
{
    var setting = await db.AppConfigEntries.FirstOrDefaultAsync(x => x.Key == monitoringAutoSyncEnabledKey);
    if (setting is null)
    {
        setting = new AppConfigEntry { Key = monitoringAutoSyncEnabledKey };
        db.AppConfigEntries.Add(setting);
    }

    setting.Value = enabled ? "true" : "false";
    setting.UpdatedAtUtc = now;
}

static async Task<bool> OverseerrHasMovieRequestAsync(IntegrationConfig? overseerr, int? tmdbId, IHttpClientFactory httpClientFactory)
{
    if (overseerr is null || !tmdbId.HasValue || tmdbId.Value <= 0) return false;

    try
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(20);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{overseerr.BaseUrl.TrimEnd('/')}/api/v1/request?take=500&skip=0");
        ApplyIntegrationAuthHeaders(overseerr, request);
        using var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode) return false;

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) return false;

        foreach (var row in results.EnumerateArray())
        {
            if (!row.TryGetProperty("media", out var media) || media.ValueKind != JsonValueKind.Object) continue;
            if (!media.TryGetProperty("tmdbId", out var tmdbElement)) continue;
            if (tmdbElement.GetInt32() == tmdbId.Value) return true;
        }

        return false;
    }
    catch
    {
        return false;
    }
}

static async Task<int> RemoveOverseerrRequestsForMovieAsync(IntegrationConfig? overseerr, int? tmdbId, IHttpClientFactory httpClientFactory)
{
    if (overseerr is null || !tmdbId.HasValue || tmdbId.Value <= 0) return 0;

    try
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(20);
        using var listRequest = new HttpRequestMessage(HttpMethod.Get, $"{overseerr.BaseUrl.TrimEnd('/')}/api/v1/request?take=500&skip=0");
        ApplyIntegrationAuthHeaders(overseerr, listRequest);
        using var listResponse = await client.SendAsync(listRequest);
        if (!listResponse.IsSuccessStatusCode) return 0;

        var body = await listResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) return 0;

        var removed = 0;
        foreach (var row in results.EnumerateArray())
        {
            if (!row.TryGetProperty("media", out var media) || media.ValueKind != JsonValueKind.Object) continue;
            if (!media.TryGetProperty("tmdbId", out var tmdbElement) || tmdbElement.GetInt32() != tmdbId.Value) continue;
            if (!row.TryGetProperty("id", out var idElement)) continue;
            var requestId = idElement.GetInt64();

            using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"{overseerr.BaseUrl.TrimEnd('/')}/api/v1/request/{requestId}");
            ApplyIntegrationAuthHeaders(overseerr, deleteRequest);
            using var deleteResponse = await client.SendAsync(deleteRequest);
            if (deleteResponse.IsSuccessStatusCode) removed++;
        }

        return removed;
    }
    catch
    {
        return 0;
    }
}

static async Task<LibraryItemMonitoringApplyResponse> SyncDesiredMonitoringToIntegrationAsync(LibraryItem item, string serviceKey, MediaCloudDbContext db, IHttpClientFactory httpClientFactory)
{
    var desiredMonitored = await GetMovieDesiredMonitoringAsync(db, item.Id);
    if (!desiredMonitored.HasValue)
    {
        return new LibraryItemMonitoringApplyResponse(item.Id, false, null, null, null, null, false, "Desired monitor is not set in MediaCloud yet.");
    }

    var normalized = (serviceKey ?? string.Empty).Trim().ToLowerInvariant();
    if (normalized == "radarr")
    {
        var radarr = await GetEnabledIntegrationByServiceAsync(db, "radarr");
        if (radarr is null)
        {
            return new LibraryItemMonitoringApplyResponse(item.Id, false, desiredMonitored, null, null, null, false, "No enabled Radarr integration configured.");
        }

        var radarrState = await FetchRadarrMovieStateAsync(item.TmdbId, radarr, httpClientFactory);
        if (!radarrState.Exists)
        {
            return new LibraryItemMonitoringApplyResponse(item.Id, false, desiredMonitored, null, null, null, false, "Movie not found in Radarr.");
        }

        var before = radarrState.Monitored;
        if (before == desiredMonitored)
        {
            return new LibraryItemMonitoringApplyResponse(item.Id, true, desiredMonitored, before, before, false, false, $"Radarr monitored already {(before == true ? "enabled" : "disabled")} (matches MediaCloud desired).");
        }

        var updateResult = await SetRadarrMonitoredAsync(radarr, radarrState.RadarrMovieId, desiredMonitored.Value, httpClientFactory);
        if (!updateResult)
        {
            return new LibraryItemMonitoringApplyResponse(item.Id, false, desiredMonitored, before, before, before != desiredMonitored, true, "Failed to update Radarr monitored flag.");
        }

        var refreshed = await FetchRadarrMovieStateAsync(item.TmdbId, radarr, httpClientFactory);
        var driftAfter = desiredMonitored.HasValue && refreshed.Monitored.HasValue
            ? desiredMonitored.Value != refreshed.Monitored.Value
            : (bool?)null;
        return new LibraryItemMonitoringApplyResponse(item.Id, refreshed.Exists && refreshed.Monitored == desiredMonitored, desiredMonitored, before, refreshed.Monitored, driftAfter, true, refreshed.Exists ? $"Applied MediaCloud desired monitor to Radarr: {(refreshed.Monitored == true ? "enabled" : "disabled")}." : "Radarr movie disappeared after update.");
    }

    if (normalized == "overseerr")
    {
        var overseerr = await GetEnabledIntegrationByServiceAsync(db, "overseerr");
        if (overseerr is null)
        {
            return new LibraryItemMonitoringApplyResponse(item.Id, false, desiredMonitored, null, null, null, false, "No enabled Overseerr integration configured.");
        }

        if (desiredMonitored.Value)
        {
            var candidate = new PlexBackfillCandidateDto(item.Id, item.Title, item.Year, item.TmdbId, item.ImdbId, false, true, false, item.TmdbId.HasValue && item.TmdbId.Value > 0, string.Empty);
            var result = await EnsureMovieRequestedInOverseerrAsync(candidate, overseerr, httpClientFactory);
            var hasRequest = await OverseerrHasMovieRequestAsync(overseerr, item.TmdbId, httpClientFactory);
            var hasMedia = (await FetchOverseerrMovieStateAsync(item.TmdbId, overseerr, httpClientFactory)).Exists;
            var synced = hasRequest || hasMedia;
            return new LibraryItemMonitoringApplyResponse(item.Id, synced && result.Success, desiredMonitored, null, null, synced ? false : true, result.PerformedAction, synced ? "Overseerr now reflects desired monitored ON." : $"Overseerr sync failed: {result.Message}");
        }
        else
        {
            var removed = await RemoveOverseerrRequestsForMovieAsync(overseerr, item.TmdbId, httpClientFactory);
            var hasRequest = await OverseerrHasMovieRequestAsync(overseerr, item.TmdbId, httpClientFactory);
            var synced = !hasRequest;
            return new LibraryItemMonitoringApplyResponse(item.Id, synced, desiredMonitored, null, null, synced ? false : true, removed > 0, synced ? "Overseerr now reflects desired monitored OFF (no pending request)." : "Overseerr still has request(s) for this movie.");
        }
    }

    return new LibraryItemMonitoringApplyResponse(item.Id, false, desiredMonitored, null, null, null, false, "Unsupported monitoring sync target.");
}

static async Task<IReadOnlyList<LibraryItemSourceStatusDto>> GetTelevisionSourceStatusesAsync(LibraryItem item, MediaCloudDbContext db)
{
    var integrations = await db.IntegrationConfigs
        .Where(x => x.Enabled)
        .ToListAsync();
    var links = await db.LibraryItemSourceLinks
        .Where(x => x.LibraryItemId == item.Id)
        .ToListAsync();

    return TelevisionSourceCoverage.BuildRows(item, integrations, links)
        .Select(row => new LibraryItemSourceStatusDto(
            row.ServiceKey,
            row.DisplayName,
            row.IntegrationId,
            row.InstanceName,
            row.HasSourceLink,
            false,
            row.Note,
            null,
            null,
            null,
            null,
            false,
            null,
            false))
        .ToList();
}

static async Task<IReadOnlyList<LibraryItemSourceStatusDto>> GetMovieSourceStatusesAsync(LibraryItem item, MediaCloudDbContext db, IHttpClientFactory httpClientFactory)
{
    var services = new[] { "plex", "radarr", "overseerr" };
    var rows = new List<LibraryItemSourceStatusDto>();

    var radarrIntegration = await GetEnabledIntegrationByServiceAsync(db, "radarr");
    var overseerrIntegration = await GetEnabledIntegrationByServiceAsync(db, "overseerr");
    var radarrState = await FetchRadarrMovieStateAsync(item.TmdbId, radarrIntegration, httpClientFactory);
    var overseerrState = await FetchOverseerrMovieStateAsync(item.TmdbId, overseerrIntegration, httpClientFactory);
    var overseerrHasRequest = await OverseerrHasMovieRequestAsync(overseerrIntegration, item.TmdbId, httpClientFactory);

    var desiredMonitored = await GetMovieDesiredMonitoringAsync(db, item.Id);
    if (!desiredMonitored.HasValue && radarrState.Monitored.HasValue)
    {
        desiredMonitored = radarrState.Monitored.Value;
        await SetMovieDesiredMonitoringAsync(db, item.Id, desiredMonitored.Value, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
    }

    var managedByMediaCloud = await GetMonitoringManagedByMediaCloudAsync(db);
    var autoSync = await GetMonitoringAutoSyncEnabledAsync(db);

    var monitoringDrift = desiredMonitored.HasValue && radarrState.Monitored.HasValue
        ? desiredMonitored.Value != radarrState.Monitored.Value
        : (bool?)null;

    foreach (var service in services)
    {
        var integration = service switch
        {
            "radarr" => radarrIntegration,
            "overseerr" => overseerrIntegration,
            _ => await GetEnabledIntegrationByServiceAsync(db, service)
        };
        var hasLink = await HasSourceLinkForServiceAsync(db, item.Id, service);

        string note;
        if (integration is null)
        {
            note = "No enabled integration configured.";
        }
        else if (hasLink)
        {
            note = "Source link is present.";
        }
        else if (service is "radarr" or "overseerr")
        {
            note = item.TmdbId.HasValue && item.TmdbId.Value > 0
                ? "Missing link. Sync can attempt to create and then verify."
                : "Missing link and TMDB ID is required before sync can create this source entry.";
        }
        else
        {
            note = "Missing link. Sync will refresh from this integration and re-check links.";
        }

        if (!managedByMediaCloud && (service is "radarr" or "overseerr"))
        {
            note = $"{note} Monitoring sync is disabled globally in Settings.";
        }

        if (service == "radarr")
        {
            var observed = radarrState.Monitored.HasValue ? (radarrState.Monitored.Value ? "Yes" : "No") : "Unknown";
            var desired = desiredMonitored.HasValue ? (desiredMonitored.Value ? "Yes" : "No") : "Unset";
            note = $"{note} Desired monitor (MediaCloud): {desired}. Radarr monitored: {observed}.";
        }

        var supportsMonitoringSync = managedByMediaCloud && (service is "radarr" or "overseerr");
        bool? monitoringSynced = null;
        if (service == "radarr")
        {
            monitoringSynced = desiredMonitored.HasValue && radarrState.Monitored.HasValue
                ? desiredMonitored.Value == radarrState.Monitored.Value
                : null;
        }
        else if (service == "overseerr")
        {
            monitoringSynced = desiredMonitored.HasValue
                ? (desiredMonitored.Value ? (overseerrHasRequest || overseerrState.Exists) : !overseerrHasRequest)
                : null;
        }

        var canSync = integration is not null && (service == "plex" || (item.TmdbId.HasValue && item.TmdbId.Value > 0));
        rows.Add(new LibraryItemSourceStatusDto(
            service,
            IntegrationCatalog.GetName(service),
            integration?.Id,
            integration?.InstanceName ?? string.Empty,
            hasLink,
            canSync,
            note,
            service == "radarr" ? radarrState.Monitored : null,
            service == "radarr" ? desiredMonitored : null,
            service == "radarr" ? monitoringDrift : null,
            service == "overseerr" ? overseerrState.Exists : null,
            supportsMonitoringSync,
            monitoringSynced,
            autoSync));
    }

    return rows;
}

static async Task<IntegrationSyncOutcome> SyncSingleIntegrationNowAsync(
    IntegrationConfig integration,
    MediaCloudDbContext db,
    IHttpClientFactory httpClientFactory,
    string runtimeToleranceMinutesFloorKey,
    string runtimeTolerancePercentKey,
    string runtimeWarningPercentKey,
    string runtimeHighMinutesKey,
    string runtimeCriticalPercentKey,
    string runtimeCriticalMinutesKey,
    double runtimeToleranceMinutesFloorDefault,
    double runtimeTolerancePercentDefault,
    double runtimeWarningPercentDefault,
    double runtimeHighMinutesDefault,
    double runtimeCriticalPercentDefault,
    double runtimeCriticalMinutesDefault,
    string runtimeMismatchIssueType,
    string runtimePolicyVersion)
{
    var runtimePolicy = await LoadRuntimePolicyValuesAsync(db,
        runtimeToleranceMinutesFloorKey,
        runtimeTolerancePercentKey,
        runtimeWarningPercentKey,
        runtimeHighMinutesKey,
        runtimeCriticalPercentKey,
        runtimeCriticalMinutesKey,
        runtimeToleranceMinutesFloorDefault,
        runtimeTolerancePercentDefault,
        runtimeWarningPercentDefault,
        runtimeHighMinutesDefault,
        runtimeCriticalPercentDefault,
        runtimeCriticalMinutesDefault);

    var outcome = await ExecuteIntegrationSyncAsync(
        integration,
        db,
        httpClientFactory,
        runtimePolicy,
        runtimeMismatchIssueType,
        runtimePolicyVersion);

    if (!outcome.Success)
    {
        return outcome;
    }

    await ConsolidateMovieDuplicatesAsync(db, outcome.SyncSeenAtUtc);
    await db.SaveChangesAsync();
    return outcome;
}

static string GetServiceRoleSummary(string serviceKey)
{
    var key = (serviceKey ?? string.Empty).Trim().ToLowerInvariant();
    return key switch
    {
        "radarr" => "Catalog ingestion + runtime baseline",
        "plex" => "File playback truth + audio/subtitle stream truth",
        "overseerr" => "Request intent and request workflow truth",
        "sonarr" => "Series catalog ingestion",
        "lidarr" => "Music catalog ingestion",
        _ => "Auxiliary integration source"
    };
}

static async Task<PlexBackfillPreviewResponse> BuildPlexBackfillPlanAsync(MediaCloudDbContext db, IHttpClientFactory httpClientFactory, int take, bool includeItemsAlreadyRequested)
{
    var items = await db.LibraryItems
        .Where(x => x.MediaType == "Movie")
        .OrderBy(x => x.SortTitle)
        .ThenBy(x => x.Title)
        .ToListAsync();

    var itemIds = items.Select(x => x.Id).ToList();
    var sourceRows = new List<(long LibraryItemId, string ServiceKey)>();
    if (itemIds.Count > 0)
    {
        var rawSourceRows = await (
            from link in db.LibraryItemSourceLinks
            join integration in db.IntegrationConfigs on link.IntegrationId equals integration.Id
            where itemIds.Contains(link.LibraryItemId)
            select new { link.LibraryItemId, integration.ServiceKey })
            .ToListAsync();

        sourceRows = rawSourceRows
            .Select(x => (x.LibraryItemId, x.ServiceKey))
            .ToList();
    }

    var serviceMap = sourceRows
        .GroupBy(x => x.LibraryItemId)
        .ToDictionary(
            g => g.Key,
            g => g
                .Select(x => (x.ServiceKey ?? string.Empty).Trim().ToLowerInvariant())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList());

    var snapshots = items.Select(item =>
    {
        var services = serviceMap.TryGetValue(item.Id, out var rowServices) ? rowServices : [];
        var inPlex = services.Any(x => string.Equals(x, "plex", StringComparison.OrdinalIgnoreCase));
        var inRadarr = services.Any(x => string.Equals(x, "radarr", StringComparison.OrdinalIgnoreCase));
        var hasOverseerrRequest = services.Any(x => string.Equals(x, "overseerr", StringComparison.OrdinalIgnoreCase));
        return new { Item = item, Services = services, InPlex = inPlex, InRadarr = inRadarr, HasOverseerrRequest = hasOverseerrRequest };
    }).ToList();

    var plexRows = snapshots.Where(x => x.InPlex).ToList();
    var missingRadarrTotal = plexRows.Count(x => !x.InRadarr);
    var missingOverseerrRequestTotal = plexRows.Count(x => !x.HasOverseerrRequest);
    var missingBothTotal = plexRows.Count(x => !x.InRadarr && !x.HasOverseerrRequest);

    var overseerrIntegration = await GetEnabledIntegrationByServiceAsync(db, "overseerr");

    var candidateSeed = plexRows
        .Where(x => !x.InRadarr || !x.HasOverseerrRequest)
        .Take(Math.Max(take * 5, take))
        .ToList();

    var tmdbIds = candidateSeed
        .Where(x => x.Item.TmdbId.HasValue && x.Item.TmdbId.Value > 0)
        .Select(x => x.Item.TmdbId!.Value)
        .Distinct()
        .ToList();

    var overseerrMediaByTmdb = await FetchOverseerrMediaPresenceByTmdbAsync(overseerrIntegration, httpClientFactory, tmdbIds);

    var candidates = candidateSeed
        .Select(x =>
        {
            var hasTmdb = x.Item.TmdbId.HasValue && x.Item.TmdbId.Value > 0;
            var inOverseerrMedia = hasTmdb
                && overseerrMediaByTmdb.TryGetValue(x.Item.TmdbId!.Value, out var present)
                && present;

            return new PlexBackfillCandidateDto(
                x.Item.Id,
                x.Item.Title,
                x.Item.Year,
                x.Item.TmdbId,
                x.Item.ImdbId,
                !x.InRadarr,
                !x.HasOverseerrRequest,
                inOverseerrMedia,
                hasTmdb,
                string.Join(",", x.Services.OrderBy(s => s)));
        })
        .Where(x => includeItemsAlreadyRequested
            ? x.MissingRadarr
            : (x.MissingRadarr || (x.MissingOverseerrRequest && !x.InOverseerrMedia)))
        .Take(take)
        .ToList();

    return new PlexBackfillPreviewResponse(
        plexRows.Count,
        missingRadarrTotal,
        missingOverseerrRequestTotal,
        missingBothTotal,
        candidates.Count,
        candidates);
}

static async Task<IntegrationConfig?> GetEnabledIntegrationByServiceAsync(MediaCloudDbContext db, string serviceKey)
{
    return await db.IntegrationConfigs
        .Where(x => x.Enabled && x.ServiceKey.ToLower() == serviceKey.ToLower())
        .OrderBy(x => x.Id)
        .FirstOrDefaultAsync();
}

static async Task<List<LibraryRemediationSourceLink>> GetLibraryRemediationSourceLinksAsync(MediaCloudDbContext db, long libraryItemId)
{
    return await (
        from link in db.LibraryItemSourceLinks
        join integration in db.IntegrationConfigs on link.IntegrationId equals integration.Id
        where link.LibraryItemId == libraryItemId
        select new LibraryRemediationSourceLink(
            integration.ServiceKey,
            integration.Id,
            link.ExternalId,
            link.ExternalType,
            link.IsDeletedAtSource))
        .ToListAsync();
}

static LibraryRemediationIntentDto ToIntentDto(LibraryRemediationIntent intent)
    => new(intent.IssueType, intent.RequestedAction, intent.ReasonCategory, intent.Confidence, intent.ShouldSearchNow, intent.ShouldBlacklistCurrentRelease, intent.NeedsManualReview, intent.NotesRecordedOnly, intent.PolicySummary, intent.NotesHandling, intent.ProfileDecision, intent.ProfileSummary);

static async Task<LibraryRemediationReleaseContext> BuildRemediationReleaseContextAsync(
    LibraryItem item,
    LibraryRemediationPlan plan,
    MediaCloudDbContext db,
    IHttpClientFactory httpClientFactory)
{
    var sourceTitle = await db.LibraryItemSourceLinks
        .Where(x => x.LibraryItemId == item.Id && x.IntegrationId == (plan.IntegrationId ?? x.IntegrationId))
        .OrderByDescending(x => x.LastSeenAtUtc)
        .Select(x => x.SourceTitle)
        .FirstOrDefaultAsync();

    var integration = plan.IntegrationId.HasValue
        ? await db.IntegrationConfigs.FirstOrDefaultAsync(x => x.Id == plan.IntegrationId.Value && x.Enabled)
        : await GetEnabledIntegrationByServiceAsync(db, plan.ServiceKey);

    var historyCandidates = integration is null || !plan.ExternalItemId.HasValue
        ? []
        : await FetchRemediationHistoryCandidatesAsync(plan, integration, httpClientFactory);

    return LibraryRemediationReleaseAwareness.BuildContext(
        plan.ServiceKey,
        plan.ExternalItemId,
        item.PrimaryFilePath,
        item.QualityProfile,
        sourceTitle ?? item.Title,
        historyCandidates);
}

static async Task<List<LibraryRemediationHistoryCandidate>> FetchRemediationHistoryCandidatesAsync(
    LibraryRemediationPlan plan,
    IntegrationConfig integration,
    IHttpClientFactory httpClientFactory)
{
    string? endpoint = plan.ServiceKey switch
    {
        "radarr" when plan.ExternalItemId.HasValue => $"/api/v3/history/movie?movieId={plan.ExternalItemId.Value}&page=1&pageSize=20&sortDirection=descending",
        "sonarr" when plan.ExternalItemId.HasValue && string.Equals(plan.CommandName, "EpisodeSearch", StringComparison.OrdinalIgnoreCase) => $"/api/v3/history/episode?episodeId={plan.ExternalItemId.Value}&page=1&pageSize=20&sortDirection=descending",
        _ => null
    };

    if (string.IsNullOrWhiteSpace(endpoint))
    {
        return [];
    }

    try
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(20);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{integration.BaseUrl.TrimEnd('/')}{endpoint}");
        ApplyIntegrationAuthHeaders(integration, request);
        using var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var rows = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("records", out var recordsElement)
            ? recordsElement
            : root;
        if (rows.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return rows.EnumerateArray()
            .Select(x => new LibraryRemediationHistoryCandidate(
                (long?)GetJsonInt(x, "id"),
                GetJsonString(x, "eventType") ?? string.Empty,
                GetJsonString(x, "sourceTitle") ?? string.Empty,
                GetJsonString(x, "downloadId") ?? string.Empty,
                GetJsonString(x, "data") ?? x.ToString(),
                ParseJsonDateTimeOffset(x, "date") ?? ParseJsonDateTimeOffset(x, "eventTime") ?? ParseJsonDateTimeOffset(x, "importedDate")))
            .Where(x => x.HistoryRecordId.HasValue || !string.IsNullOrWhiteSpace(x.EventType) || !string.IsNullOrWhiteSpace(x.SourceTitle) || !string.IsNullOrWhiteSpace(x.DownloadId))
            .ToList();
    }
    catch
    {
        return [];
    }
}

static async Task<(bool Success, string Message)> ExecuteBlacklistPlanAsync(
    IntegrationConfig integration,
    LibraryRemediationBlacklistPlan blacklistPlan,
    IHttpClientFactory httpClientFactory)
{
    if (!blacklistPlan.ShouldAttempt || string.IsNullOrWhiteSpace(blacklistPlan.EndpointPath))
    {
        return (false, blacklistPlan.Reason);
    }

    var client = httpClientFactory.CreateClient();
    client.Timeout = TimeSpan.FromSeconds(20);
    using var request = new HttpRequestMessage(HttpMethod.Post, $"{integration.BaseUrl.TrimEnd('/')}{blacklistPlan.EndpointPath}");
    ApplyIntegrationAuthHeaders(integration, request);
    using var response = await client.SendAsync(request);
    if (response.IsSuccessStatusCode)
    {
        return (true, blacklistPlan.Reason);
    }

    var body = await response.Content.ReadAsStringAsync();
    var message = string.IsNullOrWhiteSpace(body)
        ? $"Blacklist request failed with HTTP {(int)response.StatusCode}."
        : body[..Math.Min(250, body.Length)];
    return (false, message);
}

static async Task<LibraryItemRemediationResponse> ExecuteSearchReplacementAsync(
    LibraryItem item,
    LibraryRemediationPlan plan,
    MediaCloudDbContext db,
    IHttpClientFactory httpClientFactory)
{
    var integration = plan.IntegrationId.HasValue
        ? await db.IntegrationConfigs.FirstOrDefaultAsync(x => x.Id == plan.IntegrationId.Value && x.Enabled)
        : null;
    integration ??= await GetEnabledIntegrationByServiceAsync(db, plan.ServiceKey);
    if (integration is null)
    {
        return new LibraryItemRemediationResponse(item.Id, false, plan.ServiceKey, plan.DisplayName, plan.CommandName, plan.ExternalItemId, false, string.Empty, string.Empty, $"No enabled {plan.DisplayName} integration is configured.");
    }

    try
    {
        return plan.ServiceKey switch
        {
            "radarr" => await ExecuteRadarrSearchReplacementAsync(item, plan, integration, httpClientFactory),
            "sonarr" => await ExecuteSonarrSearchReplacementAsync(item, plan, integration, httpClientFactory),
            "lidarr" => await ExecuteLidarrSearchReplacementAsync(item, plan, integration, httpClientFactory),
            _ => new LibraryItemRemediationResponse(item.Id, false, plan.ServiceKey, plan.DisplayName, plan.CommandName, plan.ExternalItemId, false, string.Empty, string.Empty, $"Search replacement is not supported for service '{plan.ServiceKey}'.")
        };
    }
    catch (Exception ex)
    {
        return new LibraryItemRemediationResponse(item.Id, false, plan.ServiceKey, plan.DisplayName, plan.CommandName, plan.ExternalItemId, false, string.Empty, string.Empty, ex.Message);
    }
}

static async Task<LibraryItemRemediationResponse> ExecuteRadarrSearchReplacementAsync(
    LibraryItem item,
    LibraryRemediationPlan plan,
    IntegrationConfig integration,
    IHttpClientFactory httpClientFactory)
{
    var lookedUpRemotely = false;
    var radarrMovieId = plan.ExternalItemId;
    if (!radarrMovieId.HasValue)
    {
        radarrMovieId = await TryLookupRadarrMovieIdAsync(item, integration, httpClientFactory);
        lookedUpRemotely = radarrMovieId.HasValue;
    }

    if (!radarrMovieId.HasValue)
    {
        return new LibraryItemRemediationResponse(item.Id, false, plan.ServiceKey, plan.DisplayName, plan.CommandName, null, false, string.Empty, string.Empty, "MediaCloud could not resolve the Radarr movie ID for this item.");
    }

    var (success, responseMessage) = await QueueArrCommandAsync(
        integration,
        "/api/v3/command",
        new { name = "MoviesSearch", movieIds = new[] { radarrMovieId.Value } },
        httpClientFactory,
        $"Queued Radarr replacement search for movie ID {radarrMovieId.Value}.");

    return new LibraryItemRemediationResponse(item.Id, success, plan.ServiceKey, plan.DisplayName, plan.CommandName, radarrMovieId, lookedUpRemotely, string.Empty, string.Empty, responseMessage);
}

static async Task<LibraryItemRemediationResponse> ExecuteSonarrSearchReplacementAsync(
    LibraryItem item,
    LibraryRemediationPlan plan,
    IntegrationConfig integration,
    IHttpClientFactory httpClientFactory)
{
    if (!plan.ExternalItemId.HasValue)
    {
        return new LibraryItemRemediationResponse(item.Id, false, plan.ServiceKey, plan.DisplayName, plan.CommandName, null, false, string.Empty, string.Empty, plan.Message);
    }

    object payload = string.Equals(plan.CommandName, "SeriesSearch", StringComparison.OrdinalIgnoreCase)
        ? new { name = "SeriesSearch", seriesId = plan.ExternalItemId.Value }
        : new { name = "EpisodeSearch", episodeIds = new[] { plan.ExternalItemId.Value } };

    var (success, responseMessage) = await QueueArrCommandAsync(
        integration,
        "/api/v3/command",
        payload,
        httpClientFactory,
        $"Queued Sonarr replacement search for {plan.CommandName} target {plan.ExternalItemId.Value}.");

    return new LibraryItemRemediationResponse(item.Id, success, plan.ServiceKey, plan.DisplayName, plan.CommandName, plan.ExternalItemId, false, string.Empty, string.Empty, responseMessage);
}

static async Task<LibraryItemRemediationResponse> ExecuteLidarrSearchReplacementAsync(
    LibraryItem item,
    LibraryRemediationPlan plan,
    IntegrationConfig integration,
    IHttpClientFactory httpClientFactory)
{
    if (!plan.ExternalItemId.HasValue)
    {
        return new LibraryItemRemediationResponse(item.Id, false, plan.ServiceKey, plan.DisplayName, plan.CommandName, null, false, string.Empty, string.Empty, plan.Message);
    }

    var (success, responseMessage) = await QueueArrCommandAsync(
        integration,
        "/api/v1/command",
        new { name = "AlbumSearch", albumIds = new[] { plan.ExternalItemId.Value } },
        httpClientFactory,
        $"Queued Lidarr replacement search for album ID {plan.ExternalItemId.Value}.");

    return new LibraryItemRemediationResponse(item.Id, success, plan.ServiceKey, plan.DisplayName, plan.CommandName, plan.ExternalItemId, false, string.Empty, string.Empty, responseMessage);
}

static async Task<int?> TryLookupRadarrMovieIdAsync(LibraryItem item, IntegrationConfig integration, IHttpClientFactory httpClientFactory)
{
    if (!item.TmdbId.HasValue || item.TmdbId.Value <= 0)
    {
        return null;
    }

    try
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(20);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{integration.BaseUrl.TrimEnd('/')}/api/v3/movie?tmdbId={item.TmdbId.Value}");
        ApplyIntegrationAuthHeaders(integration, request);
        using var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
        {
            return null;
        }

        var match = document.RootElement.EnumerateArray().FirstOrDefault();
        return GetJsonInt(match, "id");
    }
    catch
    {
        return null;
    }
}

static async Task<(bool Success, string Message)> QueueArrCommandAsync(
    IntegrationConfig integration,
    string endpointPath,
    object payload,
    IHttpClientFactory httpClientFactory,
    string successMessage)
{
    var client = httpClientFactory.CreateClient();
    client.Timeout = TimeSpan.FromSeconds(20);
    using var request = new HttpRequestMessage(HttpMethod.Post, $"{integration.BaseUrl.TrimEnd('/')}{endpointPath}")
    {
        Content = JsonContent.Create(payload)
    };
    ApplyIntegrationAuthHeaders(integration, request);
    using var response = await client.SendAsync(request);
    if (response.IsSuccessStatusCode)
    {
        return (true, successMessage);
    }

    var body = await response.Content.ReadAsStringAsync();
    var message = string.IsNullOrWhiteSpace(body)
        ? $"{integration.ServiceKey} remediation command failed with HTTP {(int)response.StatusCode}."
        : body[..Math.Min(250, body.Length)];
    return (false, message);
}

static async Task<RadarrMovieState> FetchRadarrMovieStateAsync(int? tmdbId, IntegrationConfig? radarr, IHttpClientFactory httpClientFactory)
{
    if (radarr is null || !tmdbId.HasValue || tmdbId.Value <= 0)
    {
        return new RadarrMovieState(false, null, null);
    }

    try
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(20);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{radarr.BaseUrl.TrimEnd('/')}/api/v3/movie?tmdbId={tmdbId.Value}");
        ApplyIntegrationAuthHeaders(radarr, request);
        using var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return new RadarrMovieState(false, null, null);
        }

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
        {
            return new RadarrMovieState(false, null, null);
        }

        var row = doc.RootElement[0];
        var movieId = row.TryGetProperty("id", out var idElement) ? idElement.GetInt64() : (long?)null;
        var monitored = row.TryGetProperty("monitored", out var monitoredElement) ? monitoredElement.GetBoolean() : (bool?)null;
        return new RadarrMovieState(true, movieId, monitored);
    }
    catch
    {
        return new RadarrMovieState(false, null, null);
    }
}

static async Task<bool> SetRadarrMonitoredAsync(IntegrationConfig radarr, long? radarrMovieId, bool monitored, IHttpClientFactory httpClientFactory)
{
    if (!radarrMovieId.HasValue || radarrMovieId.Value <= 0) return false;

    try
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(20);
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{radarr.BaseUrl.TrimEnd('/')}/api/v3/movie/editor")
        {
            Content = JsonContent.Create(new { movieIds = new[] { radarrMovieId.Value }, monitored })
        };
        ApplyIntegrationAuthHeaders(radarr, request);
        using var response = await client.SendAsync(request);
        return response.IsSuccessStatusCode;
    }
    catch
    {
        return false;
    }
}

static async Task<OverseerrMovieState> FetchOverseerrMovieStateAsync(int? tmdbId, IntegrationConfig? overseerr, IHttpClientFactory httpClientFactory)
{
    if (overseerr is null || !tmdbId.HasValue || tmdbId.Value <= 0)
    {
        return new OverseerrMovieState(false);
    }

    try
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(20);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{overseerr.BaseUrl.TrimEnd('/')}/api/v1/movie/{tmdbId.Value}");
        ApplyIntegrationAuthHeaders(overseerr, request);
        using var response = await client.SendAsync(request);
        return new OverseerrMovieState(response.IsSuccessStatusCode);
    }
    catch
    {
        return new OverseerrMovieState(false);
    }
}

static async Task<Dictionary<int, bool>> FetchOverseerrMediaPresenceByTmdbAsync(IntegrationConfig? overseerr, IHttpClientFactory httpClientFactory, IReadOnlyList<int> tmdbIds)
{
    var result = new Dictionary<int, bool>();
    if (overseerr is null || tmdbIds.Count == 0)
    {
        return result;
    }

    var client = httpClientFactory.CreateClient();
    client.Timeout = TimeSpan.FromSeconds(20);

    foreach (var tmdbId in tmdbIds.Distinct())
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{overseerr.BaseUrl.TrimEnd('/')}/api/v1/movie/{tmdbId}");
            ApplyIntegrationAuthHeaders(overseerr, request);
            using var response = await client.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                result[tmdbId] = true;
            }
            else if ((int)response.StatusCode == 404)
            {
                result[tmdbId] = false;
            }
            else
            {
                result[tmdbId] = false;
            }
        }
        catch
        {
            result[tmdbId] = false;
        }
    }

    return result;
}

static async Task<PlexBackfillEnrichResponse> EnrichPlexBackfillIdsAsync(MediaCloudDbContext db, IHttpClientFactory httpClientFactory, int take)
{
    var plex = await GetEnabledIntegrationByServiceAsync(db, "plex");
    if (plex is null)
    {
        return new PlexBackfillEnrichResponse(0, 0, 0, 0, ["No enabled Plex integration configured."]);
    }

    var candidates = await db.LibraryItems
        .Where(x => x.MediaType == "Movie"
            && !string.IsNullOrWhiteSpace(x.PlexRatingKey)
            && ((x.TmdbId == null || x.TmdbId <= 0) || string.IsNullOrWhiteSpace(x.ImdbId)))
        .OrderBy(x => x.SortTitle)
        .ThenBy(x => x.Title)
        .Take(take)
        .ToListAsync();

    var (scanned, updated, failed, errors) = await EnrichPlexMovieIdsCoreAsync(db, plex, httpClientFactory, candidates, DateTimeOffset.UtcNow);

    await db.SaveChangesAsync();
    return new PlexBackfillEnrichResponse(candidates.Count, scanned, updated, failed, errors.Take(50).ToList());
}

static async Task<int> EnrichPlexMovieIdsByRatingKeysAsync(
    MediaCloudDbContext db,
    IntegrationConfig plex,
    IHttpClientFactory httpClientFactory,
    DateTimeOffset seenAtUtc)
{
    var candidates = await db.LibraryItems
        .Where(x => x.MediaType == "Movie"
            && !string.IsNullOrWhiteSpace(x.PlexRatingKey)
            && ((x.TmdbId == null || x.TmdbId <= 0) || string.IsNullOrWhiteSpace(x.ImdbId)))
        .ToListAsync();

    if (candidates.Count == 0)
    {
        return 0;
    }

    var (_, updated, _, _) = await EnrichPlexMovieIdsCoreAsync(db, plex, httpClientFactory, candidates, seenAtUtc);
    await db.SaveChangesAsync();
    return updated;
}

static async Task<(int Scanned, int Updated, int Failed, List<string> Errors)> EnrichPlexMovieIdsCoreAsync(
    MediaCloudDbContext db,
    IntegrationConfig plex,
    IHttpClientFactory httpClientFactory,
    IReadOnlyList<LibraryItem> candidates,
    DateTimeOffset updatedAtUtc)
{
    var client = httpClientFactory.CreateClient();
    client.Timeout = TimeSpan.FromSeconds(35);

    var scanned = 0;
    var updated = 0;
    var failed = 0;
    var errors = new List<string>();

    foreach (var item in candidates)
    {
        scanned++;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{plex.BaseUrl.TrimEnd('/')}/library/metadata/{item.PlexRatingKey}");
            ApplyIntegrationAuthHeaders(plex, request);
            using var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                failed++;
                errors.Add($"{item.Title}: HTTP {(int)response.StatusCode}");
                continue;
            }

            var xml = await response.Content.ReadAsStringAsync();
            var doc = XDocument.Parse(xml);
            var guidIds = doc.Descendants("Guid")
                .Select(x => GetXmlAttr(x, "id") ?? string.Empty)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            var tmdbRaw = ExtractProviderId(guidIds, "tmdb://");
            var imdbRaw = ExtractProviderId(guidIds, "imdb://");

            var changed = false;
            if ((item.TmdbId is null || item.TmdbId <= 0) && int.TryParse(tmdbRaw, out var tmdbId) && tmdbId > 0)
            {
                item.TmdbId = tmdbId;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(item.ImdbId) && !string.IsNullOrWhiteSpace(imdbRaw))
            {
                item.ImdbId = imdbRaw.Trim();
                changed = true;
            }

            if (changed)
            {
                item.UpdatedAtUtc = updatedAtUtc;
                updated++;
            }
        }
        catch (Exception ex)
        {
            failed++;
            errors.Add($"{item.Title}: {ex.Message[..Math.Min(ex.Message.Length, 140)]}");
        }
    }

    return (scanned, updated, failed, errors);
}

static async Task<ExternalBackfillActionResult> EnsureMovieInRadarrAsync(PlexBackfillCandidateDto candidate, IntegrationConfig radarr, IHttpClientFactory httpClientFactory)
{
    if (!candidate.TmdbId.HasValue || candidate.TmdbId.Value <= 0)
    {
        return new ExternalBackfillActionResult(false, false, "Missing TMDB id.");
    }

    var client = httpClientFactory.CreateClient();
    client.Timeout = TimeSpan.FromSeconds(30);

    using (var existsRequest = new HttpRequestMessage(HttpMethod.Get, $"{radarr.BaseUrl.TrimEnd('/')}/api/v3/movie?tmdbId={candidate.TmdbId.Value}"))
    {
        ApplyIntegrationAuthHeaders(radarr, existsRequest);
        using var existsResponse = await client.SendAsync(existsRequest);
        if (existsResponse.IsSuccessStatusCode)
        {
            var body = await existsResponse.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
            {
                return new ExternalBackfillActionResult(true, false, "Already exists in Radarr.");
            }
        }
    }

    var qualityProfileId = 1;
    using (var qpRequest = new HttpRequestMessage(HttpMethod.Get, $"{radarr.BaseUrl.TrimEnd('/')}/api/v3/qualityprofile"))
    {
        ApplyIntegrationAuthHeaders(radarr, qpRequest);
        using var qpResponse = await client.SendAsync(qpRequest);
        if (qpResponse.IsSuccessStatusCode)
        {
            var body = await qpResponse.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
            {
                qualityProfileId = doc.RootElement[0].GetProperty("id").GetInt32();
            }
        }
    }

    string? rootFolderPath = null;
    using (var rfRequest = new HttpRequestMessage(HttpMethod.Get, $"{radarr.BaseUrl.TrimEnd('/')}/api/v3/rootfolder"))
    {
        ApplyIntegrationAuthHeaders(radarr, rfRequest);
        using var rfResponse = await client.SendAsync(rfRequest);
        if (rfResponse.IsSuccessStatusCode)
        {
            var body = await rfResponse.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
            {
                rootFolderPath = doc.RootElement[0].GetProperty("path").GetString();
            }
        }
    }

    if (string.IsNullOrWhiteSpace(rootFolderPath))
    {
        return new ExternalBackfillActionResult(false, false, "No Radarr root folder configured.");
    }

    var payload = new
    {
        title = candidate.Title,
        qualityProfileId,
        titleSlug = candidate.Title?.ToLowerInvariant().Replace(' ', '-'),
        images = Array.Empty<object>(),
        tmdbId = candidate.TmdbId.Value,
        year = candidate.Year,
        rootFolderPath,
        monitored = true,
        minimumAvailability = "released",
        addOptions = new { searchForMovie = false }
    };

    using var addRequest = new HttpRequestMessage(HttpMethod.Post, $"{radarr.BaseUrl.TrimEnd('/')}/api/v3/movie")
    {
        Content = JsonContent.Create(payload)
    };
    ApplyIntegrationAuthHeaders(radarr, addRequest);
    using var addResponse = await client.SendAsync(addRequest);
    if (!addResponse.IsSuccessStatusCode)
    {
        var err = await addResponse.Content.ReadAsStringAsync();
        return new ExternalBackfillActionResult(false, false, string.IsNullOrWhiteSpace(err) ? $"HTTP {(int)addResponse.StatusCode}" : err[..Math.Min(180, err.Length)]);
    }

    return new ExternalBackfillActionResult(true, true, "Added to Radarr.");
}

static async Task<ExternalBackfillActionResult> EnsureMovieRequestedInOverseerrAsync(PlexBackfillCandidateDto candidate, IntegrationConfig overseerr, IHttpClientFactory httpClientFactory)
{
    if (!candidate.TmdbId.HasValue || candidate.TmdbId.Value <= 0)
    {
        return new ExternalBackfillActionResult(false, false, "Missing TMDB id.");
    }

    var client = httpClientFactory.CreateClient();
    client.Timeout = TimeSpan.FromSeconds(30);

    var payload = new
    {
        mediaType = "movie",
        mediaId = candidate.TmdbId.Value,
        is4k = false
    };

    using var req = new HttpRequestMessage(HttpMethod.Post, $"{overseerr.BaseUrl.TrimEnd('/')}/api/v1/request")
    {
        Content = JsonContent.Create(payload)
    };
    ApplyIntegrationAuthHeaders(overseerr, req);
    using var response = await client.SendAsync(req);

    if (response.IsSuccessStatusCode)
    {
        return new ExternalBackfillActionResult(true, true, "Created Overseerr request.");
    }

    var body = await response.Content.ReadAsStringAsync();
    if ((int)response.StatusCode == 409 || body.Contains("already", StringComparison.OrdinalIgnoreCase))
    {
        return new ExternalBackfillActionResult(true, false, "Already requested in Overseerr.");
    }

    return new ExternalBackfillActionResult(false, false, string.IsNullOrWhiteSpace(body) ? $"HTTP {(int)response.StatusCode}" : body[..Math.Min(180, body.Length)]);
}

static void ApplyIntegrationAuthHeaders(IntegrationConfig integration, HttpRequestMessage request)
{
    var authType = IntegrationCatalog.NormalizeAuthType(integration.AuthType);
    if (authType == "ApiKey")
    {
        if (string.Equals(integration.ServiceKey, "tautulli", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.Equals(integration.ServiceKey, "plex", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Add("X-Plex-Token", integration.ApiKey);
        }
        else
        {
            request.Headers.Add("X-Api-Key", integration.ApiKey);
        }
        return;
    }

    if (authType == "Basic")
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{integration.Username}:{integration.Password}"));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", encoded);
    }
}

async Task<PullPlaybackDiagnosticsResponse> PullPlaybackDiagnosticsAsync(LibraryItem item, MediaCloudDbContext db, IHttpClientFactory httpClientFactory, PullPlaybackDiagnosticsRequest? request)
{
    var hoursBack = Math.Clamp(request?.HoursBack ?? 48, 1, 24 * 14);
    var maxItems = Math.Clamp(request?.MaxItems ?? 10, 1, 50);
    var includeServerLogs = request?.IncludeServerLogs ?? true;
    var imported = 0;
    var updated = 0;
    var usedTautulli = false;
    var usedPlex = false;

    var tautulli = await db.IntegrationConfigs
        .Where(x => x.Enabled && x.ServiceKey == "tautulli")
        .OrderBy(x => x.Id)
        .FirstOrDefaultAsync();

    if (tautulli is not null && !string.IsNullOrWhiteSpace(item.PlexRatingKey))
    {
        usedTautulli = true;
        var logs = includeServerLogs ? await TryFetchTautulliPlexLogLinesAsync(tautulli, httpClientFactory) : [];
        var history = await FetchTautulliHistoryAsync(tautulli, httpClientFactory, item.PlexRatingKey, item.Title, item.Year, item.MediaType, hoursBack, maxItems);

        foreach (var session in history)
        {
            var stream = await FetchTautulliStreamDetailsAsync(tautulli, httpClientFactory, session.ExternalId);
            var logSnippet = MatchRelevantPlexLogLines(logs, item.Title, session.Player, session.OccurredAtUtc);
            var probe = new PlaybackDiagnosticProbe(
                session.Decision,
                stream.TranscodeDecision,
                stream.VideoDecision,
                stream.AudioDecision,
                stream.SubtitleDecision,
                stream.Container,
                stream.VideoCodec,
                stream.AudioCodec,
                stream.SubtitleCodec,
                stream.QualityProfile,
                PbxFirstNonEmpty(session.ErrorMessage, stream.ErrorMessage),
                logSnippet,
                session.Player,
                session.Product,
                session.Platform);
            var assessment = PlaybackDiagnosticsAnalyzer.Analyze(probe);
            var wasUpdate = await UpsertPlaybackDiagnosticEntryAsync(db, new PlaybackDiagnosticEntry
            {
                LibraryItemId = item.Id,
                IntegrationId = tautulli.Id,
                SourceService = "tautulli",
                ExternalId = session.ExternalId,
                OccurredAtUtc = session.OccurredAtUtc,
                ImportedAtUtc = DateTimeOffset.UtcNow,
                StartedAtUtc = session.StartedAtUtc,
                StoppedAtUtc = session.StoppedAtUtc,
                UserName = session.UserName,
                ClientName = session.ClientName,
                Player = session.Player,
                Product = session.Product,
                Platform = session.Platform,
                Decision = session.Decision,
                TranscodeDecision = stream.TranscodeDecision,
                VideoDecision = stream.VideoDecision,
                AudioDecision = stream.AudioDecision,
                SubtitleDecision = stream.SubtitleDecision,
                Container = stream.Container,
                VideoCodec = stream.VideoCodec,
                AudioCodec = stream.AudioCodec,
                SubtitleCodec = stream.SubtitleCodec,
                QualityProfile = stream.QualityProfile,
                HealthLabel = assessment.HealthLabel,
                Summary = assessment.Summary,
                SuspectedCause = assessment.SuspectedCause,
                ErrorMessage = PbxFirstNonEmpty(session.ErrorMessage, stream.ErrorMessage),
                LogSnippet = logSnippet,
                RawPayloadJson = stream.RawJson
            });
            if (wasUpdate) updated++; else imported++;
        }
    }

    if (imported == 0 && updated == 0)
    {
        var plex = await db.IntegrationConfigs
            .Where(x => x.Enabled && x.ServiceKey == "plex")
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync();

        if (plex is not null && !string.IsNullOrWhiteSpace(item.PlexRatingKey))
        {
            usedPlex = true;
            var sessions = await FetchPlexLiveSessionDetailsAsync(plex, httpClientFactory, item.PlexRatingKey);
            foreach (var session in sessions.Take(maxItems))
            {
                var probe = new PlaybackDiagnosticProbe(
                    session.Decision,
                    session.TranscodeDecision,
                    session.VideoDecision,
                    session.AudioDecision,
                    session.SubtitleDecision,
                    session.Container,
                    session.VideoCodec,
                    session.AudioCodec,
                    session.SubtitleCodec,
                    session.QualityProfile,
                    string.Empty,
                    string.Empty,
                    session.Player,
                    session.Product,
                    session.Platform);
                var assessment = PlaybackDiagnosticsAnalyzer.Analyze(probe);
                var wasUpdate = await UpsertPlaybackDiagnosticEntryAsync(db, new PlaybackDiagnosticEntry
                {
                    LibraryItemId = item.Id,
                    IntegrationId = plex.Id,
                    SourceService = "plex",
                    ExternalId = session.ExternalId,
                    OccurredAtUtc = session.OccurredAtUtc,
                    ImportedAtUtc = DateTimeOffset.UtcNow,
                    UserName = session.UserName,
                    ClientName = session.ClientName,
                    Player = session.Player,
                    Product = session.Product,
                    Platform = session.Platform,
                    Decision = session.Decision,
                    TranscodeDecision = session.TranscodeDecision,
                    VideoDecision = session.VideoDecision,
                    AudioDecision = session.AudioDecision,
                    SubtitleDecision = session.SubtitleDecision,
                    Container = session.Container,
                    VideoCodec = session.VideoCodec,
                    AudioCodec = session.AudioCodec,
                    SubtitleCodec = session.SubtitleCodec,
                    QualityProfile = session.QualityProfile,
                    HealthLabel = assessment.HealthLabel,
                    Summary = assessment.Summary,
                    SuspectedCause = assessment.SuspectedCause,
                    ErrorMessage = string.Empty,
                    LogSnippet = string.Empty,
                    RawPayloadJson = session.RawPayload
                });
                if (wasUpdate) updated++; else imported++;
            }
        }
    }

    await db.SaveChangesAsync();

    var totalCount = await db.PlaybackDiagnosticEntries.CountAsync(x => x.LibraryItemId == item.Id);
    var sourceMessage = usedTautulli
        ? "Pulled historical playback diagnostics from Tautulli."
        : usedPlex
            ? "No Tautulli history found; checked active Plex sessions only."
            : "No Plex or Tautulli playback integration is configured for diagnostics.";

    return new PullPlaybackDiagnosticsResponse(item.Id, imported, updated, totalCount, usedTautulli, usedPlex, sourceMessage);
}

async Task<List<TautulliHistoryItem>> FetchTautulliHistoryAsync(IntegrationConfig integration, IHttpClientFactory httpClientFactory, string ratingKey, string expectedTitle, int? expectedYear, string mediaType, int hoursBack, int maxItems)
{
    var startDate = DateTimeOffset.UtcNow.AddHours(-hoursBack).ToString("yyyy-MM-dd");
    var normalizedMediaType = NormalizePlaybackMediaType(mediaType);

    var directRows = await FetchTautulliHistoryRowsAsync(integration, httpClientFactory, new Dictionary<string, string?>
    {
        ["rating_key"] = ratingKey,
        ["media_type"] = normalizedMediaType,
        ["length"] = maxItems.ToString(CultureInfo.InvariantCulture),
        ["order_column"] = "date",
        ["order_dir"] = "desc",
        ["start_date"] = startDate
    });

    if (directRows.Count > 0)
    {
        return directRows;
    }

    var fallbackRows = await FetchTautulliHistoryRowsAsync(integration, httpClientFactory, new Dictionary<string, string?>
    {
        ["search"] = expectedTitle,
        ["media_type"] = normalizedMediaType,
        ["length"] = (maxItems * 3).ToString(CultureInfo.InvariantCulture),
        ["order_column"] = "date",
        ["order_dir"] = "desc",
        ["start_date"] = startDate
    });

    return fallbackRows
        .Where(x => PlaybackDiagnosticsHistoryMatching.IsLikelyMatch(x.DisplayTitle, expectedTitle, expectedYear))
        .Take(maxItems)
        .ToList();
}

async Task<List<TautulliHistoryItem>> FetchTautulliHistoryRowsAsync(IntegrationConfig integration, IHttpClientFactory httpClientFactory, Dictionary<string, string?> query)
{
    var doc = await CallTautulliAsync(integration, httpClientFactory, "get_history", query);

    var rows = new List<TautulliHistoryItem>();
    if (doc is null) return rows;

    if (!PbxTryGetNestedProperty(doc.RootElement, out var dataArray, "response", "data", "data") || dataArray.ValueKind != JsonValueKind.Array)
    {
        return rows;
    }

    foreach (var row in dataArray.EnumerateArray())
    {
        var externalId = GetJsonString(row, "row_id") ?? GetJsonString(row, "reference_id") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(externalId)) continue;
        var rowRatingKey = GetJsonString(row, "rating_key") ?? string.Empty;
        var occurredAt = ParseUnixTime(PbxGetJsonLong(row, "date")) ?? DateTimeOffset.UtcNow;
        rows.Add(new TautulliHistoryItem(
            externalId,
            rowRatingKey,
            occurredAt,
            ParseUnixTime(PbxGetJsonLong(row, "started")),
            ParseUnixTime(PbxGetJsonLong(row, "stopped")),
            GetJsonString(row, "user") ?? string.Empty,
            GetJsonString(row, "friendly_name") ?? string.Empty,
            GetJsonString(row, "player") ?? string.Empty,
            GetJsonString(row, "product") ?? string.Empty,
            GetJsonString(row, "platform") ?? string.Empty,
            GetJsonString(row, "transcode_decision") ?? string.Empty,
            string.Empty,
            GetJsonString(row, "full_title") ?? GetJsonString(row, "title") ?? string.Empty));
    }

    return rows;
}

async Task<TautulliStreamDetails> FetchTautulliStreamDetailsAsync(IntegrationConfig integration, IHttpClientFactory httpClientFactory, string rowId)
{
    var doc = await CallTautulliAsync(integration, httpClientFactory, "get_stream_data", new Dictionary<string, string?>
    {
        ["row_id"] = rowId
    });

    if (doc is null || !PbxTryGetNestedProperty(doc.RootElement, out var data, "response", "data"))
    {
        return new TautulliStreamDetails(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
    }

    return new TautulliStreamDetails(
        GetJsonString(data, "transcode_decision") ?? string.Empty,
        GetJsonString(data, "transcode_decision") ?? string.Empty,
        GetJsonString(data, "stream_video_decision") ?? GetJsonString(data, "video_decision") ?? string.Empty,
        GetJsonString(data, "stream_audio_decision") ?? GetJsonString(data, "audio_decision") ?? string.Empty,
        GetJsonString(data, "stream_subtitle_decision") ?? GetJsonString(data, "subtitle_decision") ?? string.Empty,
        GetJsonString(data, "stream_container") ?? GetJsonString(data, "container") ?? string.Empty,
        GetJsonString(data, "stream_video_codec") ?? GetJsonString(data, "video_codec") ?? string.Empty,
        GetJsonString(data, "stream_audio_codec") ?? GetJsonString(data, "audio_codec") ?? string.Empty,
        GetJsonString(data, "stream_subtitle_codec") ?? GetJsonString(data, "subtitle_codec") ?? string.Empty,
        GetJsonString(data, "quality_profile") ?? string.Empty,
        string.Empty,
        data.GetRawText());
}

async Task<List<string>> TryFetchTautulliPlexLogLinesAsync(IntegrationConfig integration, IHttpClientFactory httpClientFactory)
{
    var doc = await CallTautulliAsync(integration, httpClientFactory, "get_plex_log", new Dictionary<string, string?>
    {
        ["window"] = "400",
        ["log_type"] = "server"
    });

    var lines = new List<string>();
    if (doc is null || !PbxTryGetNestedProperty(doc.RootElement, out var data, "response", "data") || data.ValueKind != JsonValueKind.Array)
    {
        return lines;
    }

    foreach (var entry in data.EnumerateArray())
    {
        if (entry.ValueKind != JsonValueKind.Array) continue;
        var parts = entry.EnumerateArray().Select(x => x.ToString()).ToArray();
        if (parts.Length >= 3)
        {
            lines.Add(string.Join(" | ", parts));
        }
    }

    return lines;
}

string MatchRelevantPlexLogLines(IReadOnlyList<string> lines, string title, string player, DateTimeOffset occurredAtUtc)
{
    if (lines.Count == 0) return string.Empty;

    var interesting = lines
        .Where(line => line.Contains("error", StringComparison.OrdinalIgnoreCase)
            || line.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || line.Contains("transcod", StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(player) && line.Contains(player, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(title) && line.Contains(title, StringComparison.OrdinalIgnoreCase)))
        .TakeLast(6)
        .ToList();

    return interesting.Count == 0 ? string.Empty : string.Join("\n", interesting);
}

async Task<List<PlexLiveSessionDetails>> FetchPlexLiveSessionDetailsAsync(IntegrationConfig integration, IHttpClientFactory httpClientFactory, string ratingKey)
{
    var client = httpClientFactory.CreateClient();
    client.Timeout = TimeSpan.FromSeconds(20);
    using var request = new HttpRequestMessage(HttpMethod.Get, $"{integration.BaseUrl.TrimEnd('/')}/status/sessions");
    ApplyIntegrationAuthHeaders(integration, request);
    using var response = await client.SendAsync(request);
    if (!response.IsSuccessStatusCode)
    {
        return [];
    }

    var xml = await response.Content.ReadAsStringAsync();
    var doc = XDocument.Parse(xml);
    var rows = new List<PlexLiveSessionDetails>();
    foreach (var video in doc.Descendants().Where(x => x.Name.LocalName is "Video" or "Track" or "Episode"))
    {
        var sessionRatingKey = GetXmlAttr(video, "ratingKey") ?? string.Empty;
        if (!string.Equals(sessionRatingKey, ratingKey, StringComparison.OrdinalIgnoreCase)) continue;

        var playerNode = video.Elements().FirstOrDefault(x => x.Name.LocalName == "Player");
        var userNode = video.Elements().FirstOrDefault(x => x.Name.LocalName == "User");
        var sessionNode = video.Elements().FirstOrDefault(x => x.Name.LocalName == "Session");
        var mediaNode = video.Elements().FirstOrDefault(x => x.Name.LocalName == "Media");
        var partNode = mediaNode?.Elements().FirstOrDefault(x => x.Name.LocalName == "Part");
        var streamNodes = partNode?.Elements().Where(x => x.Name.LocalName == "Stream").ToList() ?? [];
        var videoStream = streamNodes.FirstOrDefault(x => string.Equals(GetXmlAttr(x, "streamType"), "1", StringComparison.OrdinalIgnoreCase));
        var audioStream = streamNodes.FirstOrDefault(x => string.Equals(GetXmlAttr(x, "streamType"), "2", StringComparison.OrdinalIgnoreCase));
        var subtitleStream = streamNodes.FirstOrDefault(x => string.Equals(GetXmlAttr(x, "streamType"), "3", StringComparison.OrdinalIgnoreCase));
        var transcodeNode = video.Elements().FirstOrDefault(x => x.Name.LocalName == "TranscodeSession");
        var sessionKey = GetXmlAttr(video, "sessionKey") ?? GetXmlAttr(sessionNode, "id") ?? Guid.NewGuid().ToString("N");
        var transcodeDecision = GetXmlAttr(transcodeNode, "transcodeDecision") ?? GetXmlAttr(video, "transcodeDecision") ?? GetXmlAttr(partNode, "decision") ?? string.Empty;
        rows.Add(new PlexLiveSessionDetails(
            $"session:{sessionKey}",
            DateTimeOffset.UtcNow,
            GetXmlAttr(userNode, "title") ?? string.Empty,
            GetXmlAttr(userNode, "title") ?? string.Empty,
            GetXmlAttr(playerNode, "title") ?? string.Empty,
            GetXmlAttr(playerNode, "product") ?? string.Empty,
            GetXmlAttr(playerNode, "platform") ?? string.Empty,
            transcodeDecision,
            transcodeDecision,
            GetXmlAttr(videoStream, "decision") ?? transcodeDecision,
            GetXmlAttr(audioStream, "decision") ?? transcodeDecision,
            GetXmlAttr(subtitleStream, "decision") ?? string.Empty,
            GetXmlAttr(mediaNode, "container") ?? string.Empty,
            GetXmlAttr(videoStream, "codec") ?? string.Empty,
            GetXmlAttr(audioStream, "codec") ?? string.Empty,
            GetXmlAttr(subtitleStream, "codec") ?? string.Empty,
            GetXmlAttr(transcodeNode, "qualityProfile") ?? "Original",
            video.ToString(SaveOptions.DisableFormatting)));
    }

    return rows;
}

async Task<bool> UpsertPlaybackDiagnosticEntryAsync(MediaCloudDbContext db, PlaybackDiagnosticEntry candidate)
{
    var existing = await db.PlaybackDiagnosticEntries.FirstOrDefaultAsync(x => x.LibraryItemId == candidate.LibraryItemId && x.SourceService == candidate.SourceService && x.ExternalId == candidate.ExternalId);
    if (existing is null)
    {
        db.PlaybackDiagnosticEntries.Add(candidate);
        return false;
    }

    existing.IntegrationId = candidate.IntegrationId;
    existing.OccurredAtUtc = candidate.OccurredAtUtc;
    existing.ImportedAtUtc = candidate.ImportedAtUtc;
    existing.StartedAtUtc = candidate.StartedAtUtc;
    existing.StoppedAtUtc = candidate.StoppedAtUtc;
    existing.UserName = candidate.UserName;
    existing.ClientName = candidate.ClientName;
    existing.Player = candidate.Player;
    existing.Product = candidate.Product;
    existing.Platform = candidate.Platform;
    existing.Decision = candidate.Decision;
    existing.TranscodeDecision = candidate.TranscodeDecision;
    existing.VideoDecision = candidate.VideoDecision;
    existing.AudioDecision = candidate.AudioDecision;
    existing.SubtitleDecision = candidate.SubtitleDecision;
    existing.Container = candidate.Container;
    existing.VideoCodec = candidate.VideoCodec;
    existing.AudioCodec = candidate.AudioCodec;
    existing.SubtitleCodec = candidate.SubtitleCodec;
    existing.QualityProfile = candidate.QualityProfile;
    existing.HealthLabel = candidate.HealthLabel;
    existing.Summary = candidate.Summary;
    existing.SuspectedCause = candidate.SuspectedCause;
    existing.ErrorMessage = candidate.ErrorMessage;
    existing.LogSnippet = candidate.LogSnippet;
    existing.RawPayloadJson = candidate.RawPayloadJson;
    return true;
}

async Task<JsonDocument?> CallTautulliAsync(IntegrationConfig integration, IHttpClientFactory httpClientFactory, string cmd, IDictionary<string, string?> query)
{
    var client = httpClientFactory.CreateClient();
    client.Timeout = TimeSpan.FromSeconds(20);
    var parameters = new List<string>
    {
        $"apikey={Uri.EscapeDataString(integration.ApiKey ?? string.Empty)}",
        $"cmd={Uri.EscapeDataString(cmd)}"
    };

    foreach (var pair in query)
    {
        if (string.IsNullOrWhiteSpace(pair.Value)) continue;
        parameters.Add($"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}");
    }

    var url = $"{integration.BaseUrl.TrimEnd('/')}/api/v2?{string.Join("&", parameters)}";
    using var response = await client.GetAsync(url);
    if (!response.IsSuccessStatusCode)
    {
        return null;
    }

    var body = await response.Content.ReadAsStringAsync();
    return JsonDocument.Parse(body);
}

string NormalizePlaybackMediaType(string mediaType)
    => (mediaType ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "movie" => "movie",
        "track" => "track",
        _ => "episode"
    };

DateTimeOffset? ParseUnixTime(long? seconds)
    => seconds.HasValue && seconds.Value > 0 ? DateTimeOffset.FromUnixTimeSeconds(seconds.Value) : null;

long? PbxGetJsonLong(JsonElement element, string propertyName)
{
    if (!element.TryGetProperty(propertyName, out var value)) return null;
    if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return number;
    if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) return parsed;
    return null;
}

bool PbxTryGetNestedProperty(JsonElement root, out JsonElement value, params string[] names)
{
    value = root;
    foreach (var name in names)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out value)) return false;
    }

    return true;
}

string PbxFirstNonEmpty(params string[] values)
    => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;

static string BuildCanonicalMovieKey(int? tmdbId, string imdbId, string title, int? year)
{
    if (tmdbId.HasValue && tmdbId.Value > 0)
    {
        return $"movie:tmdb:{tmdbId.Value}";
    }

    if (!string.IsNullOrWhiteSpace(imdbId))
    {
        return $"movie:imdb:{imdbId.Trim().ToLowerInvariant()}";
    }

    var safeTitle = (title ?? string.Empty).Trim().ToLowerInvariant();
    var safeYear = year?.ToString() ?? "na";
    return $"movie:titleyear:{safeTitle}:{safeYear}";
}

static string BuildSeriesCanonicalKey(int? tvdbId, int? tmdbId, string imdbId, string title, int? year)
{
    if (tvdbId.HasValue && tvdbId.Value > 0)
    {
        return $"series:tvdb:{tvdbId.Value}";
    }

    if (tmdbId.HasValue && tmdbId.Value > 0)
    {
        return $"series:tmdb:{tmdbId.Value}";
    }

    if (!string.IsNullOrWhiteSpace(imdbId))
    {
        return $"series:imdb:{imdbId.Trim().ToLowerInvariant()}";
    }

    var safeTitle = NormalizeTitleKey(title);
    var safeYear = year?.ToString() ?? "na";
    return $"series:titleyear:{safeTitle}:{safeYear}";
}

static string BuildEpisodeCanonicalKey(int? seriesTvdbId, string seriesTitle, int seasonNumber, int episodeNumber, int? episodeTvdbId)
{
    if (episodeTvdbId.HasValue && episodeTvdbId.Value > 0)
    {
        return $"episode:tvdb:{episodeTvdbId.Value}";
    }

    var seriesScope = seriesTvdbId.HasValue && seriesTvdbId.Value > 0
        ? $"tvdb:{seriesTvdbId.Value}"
        : $"title:{NormalizeTitleKey(seriesTitle)}";

    return $"episode:{seriesScope}:s{seasonNumber:00}:e{episodeNumber:00}";
}

static string BuildEpisodeDisplayTitle(string seriesTitle, int seasonNumber, int episodeNumber, string episodeTitle)
    => $"{seriesTitle} — S{seasonNumber:00}E{episodeNumber:00} — {episodeTitle}";

static string BuildEpisodeSortTitle(string seriesSortTitle, int seasonNumber, int episodeNumber, string episodeTitle)
    => $"{seriesSortTitle} s{seasonNumber:00}e{episodeNumber:00} {episodeTitle}";

static string? GetJsonString(JsonElement element, string property)
{
    if (!element.TryGetProperty(property, out var value)) return null;
    if (value.ValueKind == JsonValueKind.Null) return null;
    return value.GetString();
}

static string? GetNestedJsonString(JsonElement element, string parentProperty, string childProperty)
{
    if (!element.TryGetProperty(parentProperty, out var parent)) return null;
    if (parent.ValueKind != JsonValueKind.Object) return null;
    if (!parent.TryGetProperty(childProperty, out var child)) return null;
    if (child.ValueKind == JsonValueKind.Null) return null;
    return child.GetString();
}

static int? GetNestedJsonInt(JsonElement element, string parentProperty, string childProperty)
{
    if (!element.TryGetProperty(parentProperty, out var parent)) return null;
    if (parent.ValueKind != JsonValueKind.Object) return null;
    if (!parent.TryGetProperty(childProperty, out var child)) return null;
    return child.ValueKind == JsonValueKind.Number && child.TryGetInt32(out var v) ? v : null;
}

static int? GetJsonInt(JsonElement element, string property)
{
    if (!element.TryGetProperty(property, out var value)) return null;
    return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var v) ? v : null;
}

static double? GetJsonDouble(JsonElement element, string property)
{
    if (!element.TryGetProperty(property, out var value)) return null;
    if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var v)) return v;
    if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)) return parsed;
    return null;
}

static long? TryParseLong(string? value)
    => long.TryParse(value, out var parsed) ? parsed : null;

static bool? GetJsonBool(JsonElement element, string property)
{
    if (!element.TryGetProperty(property, out var value)) return null;
    return value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False ? value.GetBoolean() : null;
}

static DateTimeOffset? ParseJsonDateTimeOffset(JsonElement element, string property)
{
    var raw = GetJsonString(element, property);
    if (string.IsNullOrWhiteSpace(raw)) return null;
    return DateTimeOffset.TryParse(raw, out var parsed) ? parsed : null;
}

static async Task<RuntimePolicyValues> LoadRuntimePolicyValuesAsync(
    MediaCloudDbContext db,
    string toleranceMinutesFloorKey,
    string tolerancePercentKey,
    string warningPercentKey,
    string highMinutesKey,
    string criticalPercentKey,
    string criticalMinutesKey,
    double toleranceMinutesFloorDefault,
    double tolerancePercentDefault,
    double warningPercentDefault,
    double highMinutesDefault,
    double criticalPercentDefault,
    double criticalMinutesDefault)
{
    var settings = await RuntimePolicySettings.LoadAsync(
        db,
        toleranceMinutesFloorKey,
        tolerancePercentKey,
        warningPercentKey,
        highMinutesKey,
        criticalPercentKey,
        criticalMinutesKey,
        toleranceMinutesFloorDefault,
        tolerancePercentDefault,
        warningPercentDefault,
        highMinutesDefault,
        criticalPercentDefault,
        criticalMinutesDefault);

    return new RuntimePolicyValues(
        settings.ToleranceMinutesFloor,
        settings.TolerancePercent,
        settings.WarningPercent,
        settings.HighMinutes,
        settings.CriticalPercent,
        settings.CriticalMinutes);
}

static async Task UpsertRuntimeProbeFailureIssueAsync(
    MediaCloudDbContext db,
    LibraryItem item,
    string filePath,
    string probeError,
    int? probeExitCode,
    DateTimeOffset detectedAtUtc,
    string issueType)
{
    var issue = await db.LibraryIssues
        .Where(x => x.LibraryItemId == item.Id && x.IssueType == issueType)
        .OrderByDescending(x => x.Id)
        .FirstOrDefaultAsync();

    var hasFailure = item.ActualRuntimeMinutes is null;
    if (!hasFailure)
    {
        if (issue is not null && !string.Equals(issue.Status, "Resolved", StringComparison.OrdinalIgnoreCase))
        {
            issue.Status = "Resolved";
            issue.ResolvedAtUtc = detectedAtUtc;
            issue.LastDetectedAtUtc = detectedAtUtc;
        }

        return;
    }

    if (issue is null)
    {
        issue = new LibraryIssue
        {
            LibraryItemId = item.Id,
            IssueType = issueType,
            FirstDetectedAtUtc = detectedAtUtc
        };
        db.LibraryIssues.Add(issue);
    }

    var normalizedPath = (filePath ?? string.Empty).Trim();
    var normalizedError = (probeError ?? string.Empty).Trim();
    var isMissingFile = string.IsNullOrWhiteSpace(normalizedPath) || !File.Exists(normalizedPath);

    issue.PolicyVersion = "runtime-probe-v1";
    issue.Status = "Open";
    issue.ResolvedAtUtc = null;
    issue.LastDetectedAtUtc = detectedAtUtc;
    issue.Severity = isMissingFile ? "Warning" : "High";
    issue.Summary = isMissingFile
        ? "Runtime probe could not find the local media file."
        : "Runtime probe failed to read media duration from the local file.";
    issue.SuggestedAction = isMissingFile
        ? "Verify path mapping and local file availability, then resync or reprobe."
        : "Verify the media file is readable/healthy in Plex/Sonarr and replace or redownload if needed.";
    issue.DetailsJson = RuntimeProbeFailurePolicy.BuildIssueDetailsJson(normalizedPath, normalizedError, probeExitCode);
}

static async Task UpsertRuntimeMismatchIssueAsync(
    MediaCloudDbContext db,
    LibraryItem item,
    RuntimePolicyValues policy,
    DateTimeOffset detectedAtUtc,
    string issueType,
    string policyVersion)
{
    var issue = await db.LibraryIssues
        .Where(x => x.LibraryItemId == item.Id && x.IssueType == issueType)
        .OrderByDescending(x => x.Id)
        .FirstOrDefaultAsync();

    var evaluation = EvaluateRuntimeMismatch(item.RuntimeMinutes, item.ActualRuntimeMinutes, policy);
    if (!evaluation.IsMismatch)
    {
        if (issue is not null && !string.Equals(issue.Status, "Resolved", StringComparison.OrdinalIgnoreCase))
        {
            issue.Status = "Resolved";
            issue.ResolvedAtUtc = detectedAtUtc;
            issue.LastDetectedAtUtc = detectedAtUtc;
        }
        return;
    }

    if (issue is null)
    {
        issue = new LibraryIssue
        {
            LibraryItemId = item.Id,
            IssueType = issueType,
            FirstDetectedAtUtc = detectedAtUtc
        };
        db.LibraryIssues.Add(issue);
    }

    issue.PolicyVersion = policyVersion;
    issue.Status = "Open";
    issue.ResolvedAtUtc = null;
    issue.LastDetectedAtUtc = detectedAtUtc;
    issue.Severity = evaluation.Severity;
    issue.Summary = $"Runtime mismatch: reported {item.RuntimeMinutes:0.##}m vs actual {item.ActualRuntimeMinutes:0.##}m (Δ {evaluation.DiffMinutes:0.##}m / {evaluation.DiffPercent:0.##}%).";
    issue.SuggestedAction = "Reprobe runtime, verify source metadata, and check if file cut/version differs from source runtime.";
    issue.DetailsJson = JsonSerializer.Serialize(new
    {
        reportedRuntimeMinutes = item.RuntimeMinutes,
        actualRuntimeMinutes = item.ActualRuntimeMinutes,
        diffMinutes = evaluation.DiffMinutes,
        diffPercent = evaluation.DiffPercent,
        thresholdMinutes = evaluation.ThresholdMinutes,
        policy = new
        {
            toleranceMinutesFloor = policy.ToleranceMinutesFloor,
            tolerancePercent = policy.TolerancePercent,
            warningPercent = policy.WarningPercent,
            highMinutes = policy.HighMinutes,
            criticalPercent = policy.CriticalPercent,
            criticalMinutes = policy.CriticalMinutes
        }
    });
}

static RuntimeMismatchEvaluation EvaluateRuntimeMismatch(double? reportedRuntimeMinutes, double? actualRuntimeMinutes, RuntimePolicyValues policy)
{
    if (!reportedRuntimeMinutes.HasValue || !actualRuntimeMinutes.HasValue || reportedRuntimeMinutes.Value <= 0 || actualRuntimeMinutes.Value <= 0)
    {
        return new RuntimeMismatchEvaluation(false, 0, 0, 0, string.Empty);
    }

    var diffMinutes = Math.Abs(actualRuntimeMinutes.Value - reportedRuntimeMinutes.Value);
    var diffPercent = (diffMinutes / reportedRuntimeMinutes.Value) * 100d;
    var thresholdMinutes = Math.Max(policy.ToleranceMinutesFloor, reportedRuntimeMinutes.Value * (policy.TolerancePercent / 100d));

    if (diffMinutes <= thresholdMinutes)
    {
        return new RuntimeMismatchEvaluation(false, diffMinutes, diffPercent, thresholdMinutes, string.Empty);
    }

    var severity = diffPercent >= policy.CriticalPercent || diffMinutes >= policy.CriticalMinutes
        ? "Critical"
        : (diffPercent >= policy.WarningPercent || diffMinutes >= policy.HighMinutes)
            ? "High"
            : "Warning";

    return new RuntimeMismatchEvaluation(true, diffMinutes, diffPercent, thresholdMinutes, severity);
}

static bool ValidatePathMapping(string remoteRootPath, string localRootPath, out string error)
{
    var hasRemote = !string.IsNullOrWhiteSpace(remoteRootPath);
    var hasLocal = !string.IsNullOrWhiteSpace(localRootPath);

    if (hasRemote != hasLocal)
    {
        error = "Remote root and local root must both be set (or both empty).";
        return false;
    }

    if (!hasRemote)
    {
        error = string.Empty;
        return true;
    }

    if (!Path.IsPathRooted(localRootPath))
    {
        error = "Local root path must be an absolute path.";
        return false;
    }

    if (!remoteRootPath.StartsWith('/') && !remoteRootPath.StartsWith('\\'))
    {
        error = "Remote root path must start with '/' (or '\\').";
        return false;
    }

    error = string.Empty;
    return true;
}

static string ResolveLocalPath(LibraryPathMapping? mapping, string sourcePath)
{
    var source = (sourcePath ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(source))
    {
        return string.Empty;
    }

    if (mapping is null)
    {
        return source;
    }

    var remote = (mapping.RemoteRootPath ?? string.Empty).Trim().TrimEnd('/', '\\');
    var local = (mapping.LocalRootPath ?? string.Empty).Trim().TrimEnd('/', '\\');

    if (string.IsNullOrWhiteSpace(remote) || string.IsNullOrWhiteSpace(local))
    {
        return source;
    }

    if (!source.StartsWith(remote, StringComparison.OrdinalIgnoreCase))
    {
        return source;
    }

    var tail = source[remote.Length..].TrimStart('/', '\\');
    if (string.IsNullOrWhiteSpace(tail))
    {
        return local;
    }

    var normalizedTail = tail.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    return Path.Combine(local, normalizedTail);
}

static async Task<bool> TryEnrichSeriesIdentifiersFromSourceAsync(LibraryItem item, MediaCloudDbContext db, IHttpClientFactory httpClientFactory, bool persistChanges = true)
{
    if (!string.Equals(item.MediaType, "Series", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    var needsIdentifiers = !item.TvdbId.HasValue
        || !item.TmdbId.HasValue
        || string.IsNullOrWhiteSpace(item.ImdbId)
        || string.IsNullOrWhiteSpace(item.PrimaryFilePath);
    if (!needsIdentifiers)
    {
        return false;
    }

    var sourceLink = await db.LibraryItemSourceLinks
        .Where(x => x.LibraryItemId == item.Id
            && !x.IsDeletedAtSource
            && (x.ExternalType ?? string.Empty).ToLower() == "series")
        .Join(
            db.IntegrationConfigs.Where(x => x.Enabled && x.ServiceKey == "sonarr"),
            link => link.IntegrationId,
            integration => integration.Id,
            (link, integration) => new { Link = link, Integration = integration })
        .OrderBy(x => x.Link.Id)
        .FirstOrDefaultAsync();

    if (sourceLink is null || !int.TryParse((sourceLink.Link.ExternalId ?? string.Empty).Trim(), out var seriesId) || seriesId <= 0)
    {
        return false;
    }

    var client = httpClientFactory.CreateClient();
    client.Timeout = TimeSpan.FromSeconds(20);
    using var request = new HttpRequestMessage(HttpMethod.Get, $"{sourceLink.Integration.BaseUrl.TrimEnd('/')}/api/v3/series/{seriesId}");
    ApplyIntegrationAuthHeaders(sourceLink.Integration, request);
    using var response = await client.SendAsync(request);
    if (!response.IsSuccessStatusCode)
    {
        return false;
    }

    var payload = await response.Content.ReadAsStringAsync();
    using var document = JsonDocument.Parse(payload);
    if (document.RootElement.ValueKind != JsonValueKind.Object)
    {
        return false;
    }

    var series = document.RootElement;
    var updated = false;

    var tvdbId = GetJsonInt(series, "tvdbId");
    if (tvdbId.HasValue && tvdbId.Value > 0 && item.TvdbId != tvdbId)
    {
        item.TvdbId = tvdbId;
        updated = true;
    }

    var tmdbId = GetJsonInt(series, "tmdbId");
    if (tmdbId.HasValue && tmdbId.Value > 0 && item.TmdbId != tmdbId)
    {
        item.TmdbId = tmdbId;
        updated = true;
    }

    var imdbId = GetJsonString(series, "imdbId") ?? string.Empty;
    if (!string.IsNullOrWhiteSpace(imdbId) && !string.Equals(item.ImdbId, imdbId, StringComparison.OrdinalIgnoreCase))
    {
        item.ImdbId = imdbId;
        updated = true;
    }

    var rawSeriesPath = GetJsonString(series, "path") ?? string.Empty;
    if (!string.IsNullOrWhiteSpace(rawSeriesPath))
    {
        var pathMappings = await db.LibraryPathMappings
            .Where(x => x.IntegrationId == sourceLink.Integration.Id)
            .ToListAsync();
        var pathMapping = pathMappings
            .OrderByDescending(x => rawSeriesPath.StartsWith((x.RemoteRootPath ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(x => (x.RemoteRootPath ?? string.Empty).Length)
            .FirstOrDefault();
        var resolvedSeriesPath = ResolveLocalPath(pathMapping, rawSeriesPath);
        if (!string.Equals(item.PrimaryFilePath, resolvedSeriesPath, StringComparison.Ordinal))
        {
            item.PrimaryFilePath = resolvedSeriesPath;
            updated = true;
        }
    }

    var title = GetJsonString(series, "title") ?? string.Empty;
    if (!string.IsNullOrWhiteSpace(title) && (string.IsNullOrWhiteSpace(item.Title) || string.Equals(item.Title, "Unknown", StringComparison.OrdinalIgnoreCase)))
    {
        item.Title = title;
        updated = true;
    }

    var sortTitle = GetJsonString(series, "sortTitle") ?? string.Empty;
    if (!string.IsNullOrWhiteSpace(sortTitle) && (string.IsNullOrWhiteSpace(item.SortTitle) || string.Equals(item.SortTitle, item.Title, StringComparison.OrdinalIgnoreCase)))
    {
        item.SortTitle = sortTitle;
        updated = true;
    }

    if (updated)
    {
        item.UpdatedAtUtc = DateTimeOffset.UtcNow;
        if (persistChanges)
        {
            await db.SaveChangesAsync();
        }
    }

    return updated;
}

static async Task<string> TryRefreshPrimaryFilePathFromSourceAsync(LibraryItem item, MediaCloudDbContext db, IHttpClientFactory httpClientFactory)
{
    var links = await db.LibraryItemSourceLinks
        .Where(x => x.LibraryItemId == item.Id)
        .OrderBy(x => x.Id)
        .ToListAsync();

    if (links.Count == 0)
    {
        return string.Empty;
    }

    var client = httpClientFactory.CreateClient();
    client.Timeout = TimeSpan.FromSeconds(20);

    foreach (var link in links)
    {
        var integration = await db.IntegrationConfigs.FirstOrDefaultAsync(x => x.Id == link.IntegrationId && x.Enabled);
        if (integration is null)
        {
            continue;
        }

        var service = (integration.ServiceKey ?? string.Empty).Trim().ToLowerInvariant();
        var externalType = (link.ExternalType ?? string.Empty).Trim().ToLowerInvariant();
        if (service != "radarr" || externalType != "movie")
        {
            continue;
        }

        if (!int.TryParse(link.ExternalId, out var externalId) || externalId <= 0)
        {
            continue;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{integration.BaseUrl.TrimEnd('/')}/api/v3/movie/{externalId}");
            ApplyIntegrationAuthHeaders(integration, request);
            using var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                continue;
            }

            var payload = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var sourcePath = GetNestedJsonString(doc.RootElement, "movieFile", "path") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                continue;
            }

            var mapping = await db.LibraryPathMappings.FirstOrDefaultAsync(x => x.IntegrationId == integration.Id);
            var resolved = ResolveLocalPath(mapping, sourcePath);
            if (string.IsNullOrWhiteSpace(resolved))
            {
                continue;
            }

            item.PrimaryFilePath = resolved;
            if (File.Exists(resolved))
            {
                return resolved;
            }
        }
        catch
        {
            // best-effort fallback only
        }
    }

    return string.Empty;
}

static Guid StartRuntimeReprobeJob(
    ConcurrentDictionary<Guid, RuntimeReprobeJobStatusResponse> runtimeReprobeJobs,
    IServiceScopeFactory scopeFactory,
    BatchRuntimeReprobeRequest request,
    string queuedMessage)
{
    var now = DateTimeOffset.UtcNow;
    var jobId = Guid.NewGuid();
    runtimeReprobeJobs[jobId] = new RuntimeReprobeJobStatusResponse(
        jobId,
        "running",
        queuedMessage,
        0,
        0,
        0,
        0,
        0,
        0,
        now,
        null);

    _ = Task.Run(async () =>
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MediaCloudDbContext>();

            var runtimePolicy = await LoadRuntimePolicyValuesAsync(db,
                runtimeToleranceMinutesFloorKey,
                runtimeTolerancePercentKey,
                runtimeWarningPercentKey,
                runtimeHighMinutesKey,
                runtimeCriticalPercentKey,
                runtimeCriticalMinutesKey,
                runtimeToleranceMinutesFloorDefault,
                runtimeTolerancePercentDefault,
                runtimeWarningPercentDefault,
                runtimeHighMinutesDefault,
                runtimeCriticalPercentDefault,
                runtimeCriticalMinutesDefault);

            var result = await ExecuteBatchRuntimeReprobeAsync(
                db,
                request,
                runtimePolicy,
                runtimeMismatchIssueType,
                runtimePolicyVersion,
                progress =>
                {
                    runtimeReprobeJobs[jobId] = new RuntimeReprobeJobStatusResponse(
                        jobId,
                        "running",
                        progress.TotalCandidates > 0
                            ? $"Probing runtimes... {progress.Inspected}/{progress.TotalCandidates}"
                            : "Scanning candidates...",
                        progress.TotalCandidates,
                        progress.Inspected,
                        progress.Attempted,
                        progress.Updated,
                        progress.MissingFiles,
                        progress.Failed,
                        now,
                        null);
                });

            var finishedAt = DateTimeOffset.UtcNow;
            runtimeReprobeJobs[jobId] = new RuntimeReprobeJobStatusResponse(
                jobId,
                "completed",
                $"Probe complete: updated {result.Updated}/{result.Inspected}, missing {result.MissingFiles}, failures {result.Failed}.",
                result.Inspected,
                result.Inspected,
                result.Attempted,
                result.Updated,
                result.MissingFiles,
                result.Failed,
                now,
                finishedAt);
        }
        catch (Exception ex)
        {
            var finishedAt = DateTimeOffset.UtcNow;
            var message = ex.Message;
            if (message.Length > 250) message = message[..250];
            runtimeReprobeJobs[jobId] = new RuntimeReprobeJobStatusResponse(
                jobId,
                "failed",
                $"Probe job failed: {message}",
                0,
                0,
                0,
                0,
                0,
                0,
                now,
                finishedAt);
        }
    });

    return jobId;
}

static async Task<BatchRuntimeReprobeResponse> ExecuteBatchRuntimeReprobeAsync(
    MediaCloudDbContext db,
    BatchRuntimeReprobeRequest request,
    RuntimePolicyValues runtimePolicy,
    string runtimeMismatchIssueType,
    string runtimePolicyVersion,
    Action<RuntimeReprobeJobProgress>? progress = null)
{
    var take = Math.Clamp(request.Take <= 0 ? 200 : request.Take, 1, 10000);
    var query = db.LibraryItems.AsQueryable();

    if (!request.ForceAll)
    {
        query = query.Where(x => x.ActualRuntimeMinutes == null);
    }

    query = query.Where(x => x.PrimaryFilePath != "");

    if (!string.IsNullOrWhiteSpace(request.MediaType))
    {
        var media = request.MediaType.Trim();
        query = query.Where(x => x.MediaType == media);
    }

    var candidates = await query
        .OrderBy(x => x.Id)
        .Take(take)
        .ToListAsync();

    var probeFailureIssues = await db.LibraryIssues
        .Where(x => x.IssueType == runtimeProbeFailureIssueType && x.Status == "Open")
        .ToListAsync();

    candidates = candidates
        .Where(item => request.ForceAll || !RuntimeProbeFailurePolicy.ShouldSkipAutomaticReprobe(
            probeFailureIssues
                .Where(issue => issue.LibraryItemId == item.Id)
                .OrderByDescending(issue => issue.Id)
                .Select(issue => issue.Status)
                .FirstOrDefault(),
            probeFailureIssues
                .Where(issue => issue.LibraryItemId == item.Id)
                .OrderByDescending(issue => issue.Id)
                .Select(issue => issue.DetailsJson)
                .FirstOrDefault(),
            item.PrimaryFilePath))
        .ToList();

    var totalCandidates = candidates.Count;
    var inspected = 0;
    var attempted = 0;
    var updated = 0;
    var missingFiles = 0;
    var failed = 0;

    progress?.Invoke(new RuntimeReprobeJobProgress(totalCandidates, inspected, attempted, updated, missingFiles, failed));

    foreach (var item in candidates)
    {
        inspected++;
        var filePath = (item.PrimaryFilePath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            missingFiles++;
            item.ActualRuntimeMinutes = null;
            ClearPlayability(item);
            item.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await UpsertRuntimeProbeFailureIssueAsync(db, item, filePath, string.Empty, null, item.UpdatedAtUtc, runtimeProbeFailureIssueType);
            await UpsertRuntimeMismatchIssueAsync(db, item, runtimePolicy, item.UpdatedAtUtc, runtimeMismatchIssueType, runtimePolicyVersion);
            progress?.Invoke(new RuntimeReprobeJobProgress(totalCandidates, inspected, attempted, updated, missingFiles, failed));
            continue;
        }

        attempted++;
        var probe = ProbeMediaFile(filePath);
        if (probe.RuntimeMinutes.HasValue && probe.RuntimeMinutes.Value > 0)
        {
            item.ActualRuntimeMinutes = probe.RuntimeMinutes;
            ApplyPlayabilityProbe(item, probe, DateTimeOffset.UtcNow);
            item.UpdatedAtUtc = DateTimeOffset.UtcNow;
            updated++;
        }
        else
        {
            item.ActualRuntimeMinutes = null;
            ClearPlayability(item);
            item.UpdatedAtUtc = DateTimeOffset.UtcNow;
            failed++;
        }

        await UpsertRuntimeProbeFailureIssueAsync(db, item, filePath, probe.Error, probe.ExitCode, item.UpdatedAtUtc, runtimeProbeFailureIssueType);
        await UpsertRuntimeMismatchIssueAsync(db, item, runtimePolicy, item.UpdatedAtUtc, runtimeMismatchIssueType, runtimePolicyVersion);
        progress?.Invoke(new RuntimeReprobeJobProgress(totalCandidates, inspected, attempted, updated, missingFiles, failed));
    }

    await db.SaveChangesAsync();
    return new BatchRuntimeReprobeResponse(inspected, attempted, updated, missingFiles, failed);
}

static MediaProbeResult ProbeMediaFile(string filePath)
{
    try
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return new MediaProbeResult(null, null, "File not found.", null);
        }

        var psi = new ProcessStartInfo
        {
            FileName = "ffprobe",
            Arguments = $"-v error -show_entries format=duration,format_name,bit_rate:stream=codec_type,codec_name,profile,width,height,pix_fmt -of json \"{filePath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        if (proc is null)
        {
            return new MediaProbeResult(null, null, "Failed to start ffprobe process.", null);
        }

        if (!proc.WaitForExit(5000))
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            return new MediaProbeResult(null, null, "ffprobe timed out after 5s.", null);
        }

        var stdout = proc.StandardOutput.ReadToEnd().Trim();
        var stderr = proc.StandardError.ReadToEnd().Trim();
        var exitCode = proc.ExitCode;
        if (string.IsNullOrWhiteSpace(stdout))
        {
            var emptyError = !string.IsNullOrWhiteSpace(stderr)
                ? stderr[..Math.Min(300, stderr.Length)]
                : (exitCode != 0 ? $"ffprobe exited with code {exitCode}." : "ffprobe returned no output.");
            return new MediaProbeResult(null, exitCode, emptyError, null);
        }

        using var document = JsonDocument.Parse(stdout);
        var root = document.RootElement;
        var format = root.TryGetProperty("format", out var formatElement) ? formatElement : default;
        var formatNames = GetJsonString(format, "format_name") ?? string.Empty;
        var durationSeconds = GetJsonDouble(format, "duration");
        var bitrateBitsPerSecond = TryParseLong(GetJsonString(format, "bit_rate"));
        var streams = root.TryGetProperty("streams", out var streamsElement) && streamsElement.ValueKind == JsonValueKind.Array
            ? streamsElement.EnumerateArray().ToArray()
            : [];

        var videoStream = streams.FirstOrDefault(stream => string.Equals(GetJsonString(stream, "codec_type"), "video", StringComparison.OrdinalIgnoreCase));
        var videoCodec = GetJsonString(videoStream, "codec_name") ?? string.Empty;
        var videoProfile = GetJsonString(videoStream, "profile") ?? string.Empty;
        var pixelFormat = GetJsonString(videoStream, "pix_fmt") ?? string.Empty;
        var width = GetJsonInt(videoStream, "width");
        var height = GetJsonInt(videoStream, "height");
        var audioCodecs = streams
            .Where(stream => string.Equals(GetJsonString(stream, "codec_type"), "audio", StringComparison.OrdinalIgnoreCase))
            .Select(stream => GetJsonString(stream, "codec_name") ?? string.Empty)
            .Where(codec => !string.IsNullOrWhiteSpace(codec))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var subtitleCodecs = streams
            .Where(stream => string.Equals(GetJsonString(stream, "codec_type"), "subtitle", StringComparison.OrdinalIgnoreCase))
            .Select(stream => GetJsonString(stream, "codec_name") ?? string.Empty)
            .Where(codec => !string.IsNullOrWhiteSpace(codec))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var runtimeMinutes = durationSeconds.HasValue && durationSeconds.Value > 0
            ? Math.Round(durationSeconds.Value / 60d, 2)
            : (double?)null;
        var probeInfo = new MediaPlayabilityProbeInfo(
            string.IsNullOrWhiteSpace(formatNames) ? [] : new[] { formatNames },
            videoCodec,
            videoProfile,
            pixelFormat,
            width,
            height,
            bitrateBitsPerSecond,
            audioCodecs,
            subtitleCodecs);
        var assessment = MediaPlayabilityScoring.Evaluate(probeInfo);
        var details = new MediaPlayabilityStoredDetails(
            probeInfo.ContainerNames,
            probeInfo.VideoCodec,
            probeInfo.VideoProfile,
            probeInfo.PixelFormat,
            probeInfo.Width,
            probeInfo.Height,
            probeInfo.BitrateBitsPerSecond,
            probeInfo.AudioCodecs,
            probeInfo.SubtitleCodecs,
            assessment.Reasons);

        return new MediaProbeResult(runtimeMinutes, exitCode, string.Empty, new MediaPlayabilitySnapshot(assessment.Label, assessment.Summary, details));
    }
    catch (Exception ex)
    {
        var error = ex.Message;
        if (error.Contains("ffprobe", StringComparison.OrdinalIgnoreCase) &&
            (error.Contains("No such file or directory", StringComparison.OrdinalIgnoreCase) ||
             error.Contains("cannot find the file", StringComparison.OrdinalIgnoreCase)))
        {
            error = "ffprobe is not available in this runtime container. Install ffmpeg/ffprobe in the image and redeploy.";
        }

        if (error.Length > 300) error = error[..300];
        return new MediaProbeResult(null, null, error, null);
    }
}

static void ApplyPlayabilityProbe(LibraryItem item, MediaProbeResult probe, DateTimeOffset checkedAtUtc)
{
    if (probe.Playability is null)
    {
        ClearPlayability(item);
        return;
    }

    item.PlayabilityScore = probe.Playability.Label;
    item.PlayabilitySummary = probe.Playability.Summary;
    item.PlayabilityDetailsJson = JsonSerializer.Serialize(probe.Playability.Details);
    item.PlayabilityCheckedAtUtc = checkedAtUtc;
}

static void ClearPlayability(LibraryItem item)
{
    item.PlayabilityScore = string.Empty;
    item.PlayabilitySummary = string.Empty;
    item.PlayabilityDetailsJson = string.Empty;
    item.PlayabilityCheckedAtUtc = null;
}

file sealed record RuntimePolicyValues(double ToleranceMinutesFloor, double TolerancePercent, double WarningPercent, double HighMinutes, double CriticalPercent, double CriticalMinutes);
file sealed record RuntimeMismatchEvaluation(bool IsMismatch, double DiffMinutes, double DiffPercent, double ThresholdMinutes, string Severity);
file sealed record MediaPlayabilitySnapshot(string Label, string Summary, MediaPlayabilityStoredDetails Details);
file sealed record MediaProbeResult(double? RuntimeMinutes, int? ExitCode, string Error, MediaPlayabilitySnapshot? Playability);
file sealed record IntegrationSyncOutcome(bool Success, DateTimeOffset SyncSeenAtUtc, string Message, int Processed = 0);
file sealed record ExternalBackfillActionResult(bool Success, bool PerformedAction, string Message);
file sealed record RadarrMovieState(bool Exists, long? RadarrMovieId, bool? Monitored);
file sealed record OverseerrMovieState(bool Exists);

public record LoginRequest(string Username, string Password);
public record LoginResponse(Guid UserId, string Username, string Role, string Token);
public record MeResponse(Guid UserId, string Username, string Role);
public record RegisterRequest(string Username, string Password);
public record CreateUserRequest(string Username, string Password, string Role);
public record UpdateUserRequest(string Username, string Role, string? Password);
public record UpdateAuthSettingsRequest(bool AllowSelfRegistration);
public record AuthSettingsResponse(bool AllowSelfRegistration);
public record UpdateRuntimePolicySettingsRequest(double ToleranceMinutesFloor, double TolerancePercent, double WarningPercent, double HighMinutes, double CriticalPercent, double CriticalMinutes);
public record RuntimePolicySettingsResponse(double ToleranceMinutesFloor, double TolerancePercent, double WarningPercent, double HighMinutes, double CriticalPercent, double CriticalMinutes);
public record IntegrationServiceResponse(string ServiceKey, string DisplayName, bool RequiresAuth, IReadOnlyList<string> AllowedAuthTypes);
public record CreateIntegrationInstanceRequest(string ServiceKey, string InstanceName, string BaseUrl, string AuthType, string ApiKey, string Username, string Password, bool Enabled);
public record UpdateIntegrationInstanceRequest(string InstanceName, string BaseUrl, string AuthType, string ApiKey, string Username, string Password, bool Enabled);
public record IntegrationInstanceResponse(long Id, string ServiceKey, string DisplayName, string InstanceName, string BaseUrl, string AuthType, string ApiKey, string Username, string Password, bool Enabled, DateTimeOffset? UpdatedAtUtc);
public record CreateLibraryPathMappingRequest(long IntegrationId, string RemoteRootPath, string LocalRootPath);
public record UpdateLibraryPathMappingRequest(string RemoteRootPath, string LocalRootPath);
public record LibraryPathMappingResponse(long Id, long IntegrationId, string ServiceKey, string InstanceName, string DisplayName, string RemoteRootPath, string LocalRootPath, DateTimeOffset UpdatedAtUtc);
public record LibraryPathMappingTestResponse(long MappingId, long IntegrationId, string ServiceKey, string RemoteRootPath, string LocalRootPath, bool LocalPathExists, bool RemotePathMatchesIntegration, bool DeepTestAttempted, string SourceFilePath, string ResolvedLocalFilePath, bool ResolvedLocalFileExists, IReadOnlyList<string> DiscoveredRemoteRoots, bool Success, string Message);
public record IntegrationRemoteRootsResponse(long IntegrationId, string ServiceKey, IReadOnlyList<string> Paths, string Message);
public record LocalDirectoryBrowseResponse(string Path, string ParentPath, IReadOnlyList<string> Directories);
public record IntegrationTestResponse(long IntegrationId, string ServiceKey, string InstanceName, bool Success, int StatusCode, string Message);
public record TriggerIntegrationSyncRequest(bool ForceFullResync = false, string? MediaScope = null);
public record TriggerIntegrationSyncResponse(long IntegrationId, bool Accepted, string Message);
public record PlexBackfillPreviewRequest(int Take = 100, bool IncludeItemsAlreadyRequested = false);
public record PlexBackfillEnrichRequest(int Take = 200);
public record PlexBackfillApplyRequest(int Take = 200, bool CreateOverseerrRequests = false, bool IncludeItemsAlreadyRequested = false, IReadOnlyList<long>? SelectedLibraryItemIds = null);
public record PlexBackfillCandidateDto(long LibraryItemId, string Title, int? Year, int? TmdbId, string ImdbId, bool MissingRadarr, bool MissingOverseerrRequest, bool InOverseerrMedia, bool CanBackfill, string SourceServicesCsv);
public record PlexBackfillPreviewResponse(int TotalPlexMovies, int MissingRadarrCount, int MissingOverseerrRequestCount, int MissingBothCount, int CandidateCount, IReadOnlyList<PlexBackfillCandidateDto> Items);
public record PlexBackfillEnrichResponse(int CandidateCount, int Scanned, int Updated, int Failed, IReadOnlyList<string> Errors);
public record PlexBackfillApplyResponse(int TotalPlexMovies, int MissingRadarrCount, int MissingOverseerrRequestCount, int MissingBothCount, int CandidateCount, int AddedToRadarr, int AddedToOverseerr, int Skipped, IReadOnlyList<string> Errors);
public record IntegrationSyncStateDto(long IntegrationId, DateTimeOffset? LastAttemptedAtUtc, DateTimeOffset? LastSuccessfulAtUtc, string LastCursor, string LastEtag, string LastError, int ConsecutiveFailureCount, DateTimeOffset UpdatedAtUtc);
public record DashboardSourceTruthIntegrationDto(long IntegrationId, string ServiceKey, string DisplayName, string InstanceName, bool Enabled, string RoleSummary, int SourceLinkCount, DateTimeOffset? LastAttemptedAtUtc, DateTimeOffset? LastSuccessfulAtUtc, string LastError, int ConsecutiveFailureCount);
public record DashboardSourceTruthResponse(IReadOnlyList<DashboardSourceTruthIntegrationDto> Integrations);
public record LibraryItemDto(long Id, string CanonicalKey, string MediaType, string DisplayTitle, string Title, string SortTitle, int? Year, int? TmdbId, int? TvdbId, string ImdbId, string PlexRatingKey, string Description, string DescriptionSourceService, double? RuntimeMinutes, double? ActualRuntimeMinutes, IReadOnlyList<string> AudioLanguages, IReadOnlyList<string> SubtitleLanguages, string PlayabilityScore, string PlayabilitySummary, DateTimeOffset? PlayabilityCheckedAtUtc, IReadOnlyList<string> PlayabilityReasons, string PlayabilityVideoCodec, IReadOnlyList<string> PlayabilityAudioCodecs, IReadOnlyList<string> PlayabilitySubtitleCodecs, bool IsAvailable, string QualityProfile, DateTimeOffset? SourceUpdatedAtUtc, DateTimeOffset UpdatedAtUtc, string PrimaryFilePath, bool LocalFileExists, IReadOnlyList<string> SourceServices);
public record LibraryItemRuntimeProbeResponse(long Id, string MediaType, string Title, string PrimaryFilePath, bool FileExists, bool Success, double? ActualRuntimeMinutes, string Message, int? ProbeExitCode, string ProbeError, string PlayabilityScore, string PlayabilitySummary, DateTimeOffset? PlayabilityCheckedAtUtc);
public record PlaybackDiagnosticDto(long Id, long LibraryItemId, string SourceService, string SourceDisplayName, string ExternalId, DateTimeOffset OccurredAtUtc, DateTimeOffset ImportedAtUtc, DateTimeOffset? StartedAtUtc, DateTimeOffset? StoppedAtUtc, string UserName, string ClientName, string Player, string Product, string Platform, string Decision, string TranscodeDecision, string VideoDecision, string AudioDecision, string SubtitleDecision, string Container, string VideoCodec, string AudioCodec, string SubtitleCodec, string QualityProfile, string HealthLabel, string Summary, string SuspectedCause, string ErrorMessage, string LogSnippet);
public record PullPlaybackDiagnosticsRequest(int HoursBack = 48, int MaxItems = 10, bool IncludeServerLogs = true);
public record PullPlaybackDiagnosticsResponse(long LibraryItemId, int ImportedCount, int UpdatedCount, int TotalCount, bool UsedTautulli, bool UsedPlex, string Message);
public record TautulliHistoryItem(string ExternalId, string RatingKey, DateTimeOffset OccurredAtUtc, DateTimeOffset? StartedAtUtc, DateTimeOffset? StoppedAtUtc, string UserName, string ClientName, string Player, string Product, string Platform, string Decision, string ErrorMessage, string DisplayTitle);
public record TautulliStreamDetails(string Decision, string TranscodeDecision, string VideoDecision, string AudioDecision, string SubtitleDecision, string Container, string VideoCodec, string AudioCodec, string SubtitleCodec, string QualityProfile, string ErrorMessage, string RawJson);
public record PlexLiveSessionDetails(string ExternalId, DateTimeOffset OccurredAtUtc, string UserName, string ClientName, string Player, string Product, string Platform, string Decision, string TranscodeDecision, string VideoDecision, string AudioDecision, string SubtitleDecision, string Container, string VideoCodec, string AudioCodec, string SubtitleCodec, string QualityProfile, string RawPayload);
public record BatchRuntimeReprobeRequest(string? MediaType, int Take = 200, bool ForceAll = false);
public record BatchRuntimeReprobeResponse(int Inspected, int Attempted, int Updated, int MissingFiles, int Failed);
public record CreateLibraryIssueRequest(string IssueType, string Severity, string Summary, string SuggestedAction, string DetailsJson, string Status = "Open");
public record StartRuntimeReprobeJobResponse(Guid JobId);
public record RuntimeReprobeJobStatusResponse(Guid JobId, string Status, string Message, int TotalCandidates, int Inspected, int Attempted, int Updated, int MissingFiles, int Failed, DateTimeOffset StartedAtUtc, DateTimeOffset? FinishedAtUtc);
public record RuntimeReprobeJobProgress(int TotalCandidates, int Inspected, int Attempted, int Updated, int MissingFiles, int Failed);
public record SonarrCollectionFetchResult(bool Success, string ErrorMessage, List<JsonElement> Items);
public record SonarrSeriesContext(string Title, string SortTitle, int? Year, int? TvdbId, int? TmdbId, string ImdbId, string QualityProfile);
public record LibraryItemSourceTitleInfo(long LibraryItemId, string ServiceKey, string InstanceName, string SourceTitle, string SourceSortTitle);
public record LibraryItemSourceLinkDto(long Id, long LibraryItemId, long IntegrationId, string ServiceKey, string InstanceName, string SourceTitle, string SourceSortTitle, string ExternalId, string ExternalType, DateTimeOffset? ExternalUpdatedAtUtc, DateTimeOffset LastSeenAtUtc, bool IsDeletedAtSource);
public record LibraryItemSourceStatusDto(string ServiceKey, string DisplayName, long? IntegrationId, string InstanceName, bool HasSourceLink, bool CanSync, string Note, bool? RadarrMonitored, bool? DesiredMonitored, bool? MonitoringDrift, bool? OverseerrInMedia, bool SupportsMonitoringSync, bool? MonitoringSynced, bool AutoSyncEnabled);
public record LibraryItemSourceSyncResponse(long LibraryItemId, string ServiceKey, bool Success, bool HadSourceLinkBefore, bool HasSourceLinkAfter, bool AttemptedCreateAction, string Message);
public record SearchReplacementRequest(string Reason, string Notes, string? IssueType = null);
public record LibraryRemediationIntentDto(string IssueType, string RequestedAction, string ReasonCategory, string Confidence, bool ShouldSearchNow, bool ShouldBlacklistCurrentRelease, bool NeedsManualReview, bool NotesRecordedOnly, string PolicySummary, string NotesHandling, string ProfileDecision, string ProfileSummary);
public record LibraryItemRemediationResponse(long LibraryItemId, bool Success, string ServiceKey, string ServiceDisplayName, string CommandName, int? ExternalItemId, bool LookedUpRemotely, string Reason, string Notes, string Message, LibraryRemediationIntentDto? Intent = null);
public record LibraryRemediationJobDto(long Id, long LibraryItemId, long? LibraryIssueId, string ServiceKey, string ServiceDisplayName, string RequestedAction, string CommandName, int? ExternalItemId, string IssueType, string Reason, string Notes, string ReasonCategory, string Confidence, bool ShouldSearchNow, bool ShouldBlacklistCurrentRelease, bool NeedsManualReview, bool NotesRecordedOnly, bool LookedUpRemotely, string PolicySummary, string NotesHandling, string ProfileDecision, string ProfileSummary, string Status, string SearchStatus, string BlacklistStatus, string OutcomeSummary, string ResultMessage, string ReleaseSummary, string RequestedBy, DateTimeOffset RequestedAtUtc, DateTimeOffset? FinishedAtUtc, DateTimeOffset? LastCheckedAtUtc);
public record SetDesiredMonitoringRequest(bool DesiredMonitored);
public record UpdateMonitoringSettingsRequest(bool ManagedByMediaCloud, bool AutoSyncEnabled);
public record MonitoringSettingsResponse(bool ManagedByMediaCloud, bool AutoSyncEnabled);
public record UpdateTvDisplaySettingsRequest(bool HideSpecialsByDefault);
public record TvDisplaySettingsResponse(bool HideSpecialsByDefault);
public record LibraryItemMonitoringStateResponse(long LibraryItemId, bool? DesiredMonitored, bool? RadarrMonitored, bool? MonitoringDrift, bool OverseerrSignalPresent, bool RadarrExists, bool AutoSyncEnabled);
public record LibraryItemMonitoringApplyResponse(long LibraryItemId, bool Success, bool? DesiredMonitored, bool? RadarrMonitoredBefore, bool? RadarrMonitoredAfter, bool? MonitoringDriftAfter, bool ActionAttempted, string Message);
public record LibraryItemCountResponse(int Total);
public record PurgeLibraryRequest(string? MediaType = null, bool ResetSyncState = true, bool ClearMonitoringState = true);
public record PurgeLibraryResponse(string MediaType, int ItemsDeleted, int SourceLinksDeleted, int IssuesDeleted, int MonitoringStateDeleted);
public record LibraryJumpResponse(bool Found, string Token, int PageIndex, long? TargetId);
public record LibraryIssueDto(long Id, long LibraryItemId, string IssueType, string Severity, string Status, string Summary, string SuggestedAction, string DetailsJson, DateTimeOffset FirstDetectedAtUtc, DateTimeOffset LastDetectedAtUtc, DateTimeOffset? ResolvedAtUtc, string LibraryItemTitle, string MediaType);
public record UserSummaryResponse(Guid Id, string Username, string Role, DateTimeOffset CreatedAtUtc);
public record UserAuditLogResponse(long Id, DateTimeOffset OccurredAtUtc, Guid? ActorUserId, string ActorUsername, Guid TargetUserId, string TargetUsername, string Action, string Summary);
public record ErrorResponse(string Error);
public record SuccessResponse(bool Success);

public static class RuntimePolicySettings
{
    public static async Task<RuntimePolicySettingsResponse> LoadAsync(
        MediaCloudDbContext db,
        string toleranceMinutesFloorKey,
        string tolerancePercentKey,
        string warningPercentKey,
        string highMinutesKey,
        string criticalPercentKey,
        string criticalMinutesKey,
        double toleranceMinutesFloorDefault,
        double tolerancePercentDefault,
        double warningPercentDefault,
        double highMinutesDefault,
        double criticalPercentDefault,
        double criticalMinutesDefault)
    {
        var map = await db.AppConfigEntries
            .Where(x => x.Key == toleranceMinutesFloorKey
                        || x.Key == tolerancePercentKey
                        || x.Key == warningPercentKey
                        || x.Key == highMinutesKey
                        || x.Key == criticalPercentKey
                        || x.Key == criticalMinutesKey)
            .ToDictionaryAsync(x => x.Key, x => x.Value);

        return new RuntimePolicySettingsResponse(
            ParseDoubleOrDefault(map, toleranceMinutesFloorKey, toleranceMinutesFloorDefault),
            ParseDoubleOrDefault(map, tolerancePercentKey, tolerancePercentDefault),
            ParseDoubleOrDefault(map, warningPercentKey, warningPercentDefault),
            ParseDoubleOrDefault(map, highMinutesKey, highMinutesDefault),
            ParseDoubleOrDefault(map, criticalPercentKey, criticalPercentDefault),
            ParseDoubleOrDefault(map, criticalMinutesKey, criticalMinutesDefault));
    }

    public static async Task UpsertAsync(MediaCloudDbContext db, string key, double value, DateTimeOffset now)
    {
        var raw = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var setting = await db.AppConfigEntries.FirstOrDefaultAsync(x => x.Key == key);
        if (setting is null)
        {
            setting = new AppConfigEntry { Key = key };
            db.AppConfigEntries.Add(setting);
        }

        setting.Value = raw;
        setting.UpdatedAtUtc = now;
    }

    private static double ParseDoubleOrDefault(Dictionary<string, string> map, string key, double fallback)
    {
        if (!map.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw)) return fallback;
        return double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }
}

public static class AppAuthSettings
{
    public static async Task<bool> IsSelfRegistrationAllowedAsync(MediaCloudDbContext db, bool fallbackValue, string key)
    {
        var raw = await db.AppConfigEntries.Where(x => x.Key == key).Select(x => x.Value).FirstOrDefaultAsync();
        if (string.IsNullOrWhiteSpace(raw)) return fallbackValue;
        return raw.Equals("true", StringComparison.OrdinalIgnoreCase)
               || raw.Equals("1", StringComparison.OrdinalIgnoreCase)
               || raw.Equals("yes", StringComparison.OrdinalIgnoreCase)
               || raw.Equals("on", StringComparison.OrdinalIgnoreCase);
    }
}

public static class TvDisplaySettings
{
    public static async Task<TvDisplaySettingsResponse> LoadAsync(MediaCloudDbContext db, string key, bool fallbackHideSpecials)
    {
        var raw = await db.AppConfigEntries.Where(x => x.Key == key).Select(x => x.Value).FirstOrDefaultAsync();
        return new TvDisplaySettingsResponse(ParseBool(raw, fallbackHideSpecials));
    }

    public static async Task UpsertAsync(MediaCloudDbContext db, string key, bool hideSpecialsByDefault, DateTimeOffset now)
    {
        var setting = await db.AppConfigEntries.FirstOrDefaultAsync(x => x.Key == key);
        if (setting is null)
        {
            setting = new AppConfigEntry { Key = key };
            db.AppConfigEntries.Add(setting);
        }

        setting.Value = hideSpecialsByDefault ? "true" : "false";
        setting.UpdatedAtUtc = now;
    }

    private static bool ParseBool(string? raw, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        return raw.Equals("true", StringComparison.OrdinalIgnoreCase)
               || raw.Equals("1", StringComparison.OrdinalIgnoreCase)
               || raw.Equals("yes", StringComparison.OrdinalIgnoreCase)
               || raw.Equals("on", StringComparison.OrdinalIgnoreCase);
    }
}

public static class IntegrationCatalog
{
    public static readonly (string Key, string Name)[] SupportedServices =
    [
        ("overseerr", "Overseerr"),
        ("radarr", "Radarr"),
        ("sonarr", "Sonarr"),
        ("lidarr", "Lidarr"),
        ("prowlarr", "Prowlarr"),
        ("plex", "Plex"),
        ("tautulli", "Tautulli")
    ];

    private static readonly string[] SupportedAuthTypes = ["None", "ApiKey", "Basic"];

    public static bool IsSupported(string key) => SupportedServices.Any(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
    public static string GetName(string key) => SupportedServices.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Name ?? key;

    public static bool ServiceRequiresCredentials(string key) => IsSupported(key);
    public static IReadOnlyList<string> GetAllowedAuthTypesForService(string key)
        => key.Equals("tautulli", StringComparison.OrdinalIgnoreCase)
            ? ["ApiKey"]
            : ServiceRequiresCredentials(key) ? ["ApiKey", "Basic"] : ["None", "ApiKey", "Basic"];

    public static bool IsSupportedAuthType(string? value)
        => SupportedAuthTypes.Any(x => x.Equals(value ?? string.Empty, StringComparison.OrdinalIgnoreCase));

    public static string NormalizeAuthType(string? value)
        => SupportedAuthTypes.FirstOrDefault(x => x.Equals(value ?? string.Empty, StringComparison.OrdinalIgnoreCase)) ?? "ApiKey";

    public static bool IsAuthTypeAllowedForService(string key, string? authType)
    {
        var normalized = NormalizeAuthType(authType);
        return GetAllowedAuthTypesForService(key).Any(x => x.Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<(bool Success, int StatusCode, string Message)> TestConnectionAsync(string serviceKey, IntegrationConfig config, IHttpClientFactory httpClientFactory)
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);

        var baseUrl = config.BaseUrl.TrimEnd('/');
        var requestUri = serviceKey switch
        {
            "overseerr" => $"{baseUrl}/api/v1/status",
            "radarr" => $"{baseUrl}/api/v3/system/status",
            "sonarr" => $"{baseUrl}/api/v3/system/status",
            "lidarr" => $"{baseUrl}/api/v1/system/status",
            "prowlarr" => $"{baseUrl}/api/v1/system/status",
            "plex" => $"{baseUrl}/identity",
            "tautulli" => $"{baseUrl}/api/v2?apikey={Uri.EscapeDataString(config.ApiKey ?? string.Empty)}&cmd=get_activity",
            _ => baseUrl
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            var authType = NormalizeAuthType(config.AuthType);

            if (authType == "ApiKey")
            {
                if (serviceKey == "plex") request.Headers.Add("X-Plex-Token", config.ApiKey);
                else request.Headers.Add("X-Api-Key", config.ApiKey);
            }
            else if (authType == "Basic")
            {
                var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.Username}:{config.Password}"));
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", encoded);
            }

            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                var message = string.IsNullOrWhiteSpace(body) ? $"HTTP {(int)response.StatusCode}" : body[..Math.Min(160, body.Length)];
                return (false, (int)response.StatusCode, message);
            }

            return (true, (int)response.StatusCode, "Connection successful");
        }
        catch (Exception ex)
        {
            return (false, 0, ex.Message);
        }
    }
}
