using System.Data;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
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
const string runtimeMismatchIssueType = "runtime_mismatch";
const string runtimePolicyVersion = "runtime-v1";
const double runtimeToleranceMinutesFloorDefault = 3d;
const double runtimeTolerancePercentDefault = 5d;
const double runtimeWarningPercentDefault = 10d;
const double runtimeHighMinutesDefault = 12d;
const double runtimeCriticalPercentDefault = 20d;
const double runtimeCriticalMinutesDefault = 25d;

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
");

    EnsureSqliteColumn(db, "IntegrationConfigs", "InstanceName", "TEXT NOT NULL DEFAULT 'Default'");
    EnsureSqliteColumn(db, "IntegrationConfigs", "AuthType", "TEXT NOT NULL DEFAULT 'ApiKey'");
    EnsureSqliteColumn(db, "IntegrationConfigs", "Username", "TEXT NOT NULL DEFAULT ''");
    EnsureSqliteColumn(db, "IntegrationConfigs", "Password", "TEXT NOT NULL DEFAULT ''");
    EnsureSqliteColumn(db, "IntegrationConfigs", "RemoteRootPath", "TEXT NOT NULL DEFAULT ''");
    EnsureSqliteColumn(db, "IntegrationConfigs", "LocalRootPath", "TEXT NOT NULL DEFAULT ''");
    EnsureSqliteColumn(db, "LibraryItems", "ActualRuntimeMinutes", "REAL NULL");
    EnsureSqliteColumn(db, "LibraryItems", "PrimaryFilePath", "TEXT NOT NULL DEFAULT ''");

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
                using var episodeFileRequest = new HttpRequestMessage(HttpMethod.Get, $"{integration.BaseUrl.TrimEnd('/')}/api/v3/episodefile");
                ApplyIntegrationAuthHeaders(integration, episodeFileRequest);
                using var episodeFileResponse = await client.SendAsync(episodeFileRequest);
                if (episodeFileResponse.IsSuccessStatusCode)
                {
                    var episodePayload = await episodeFileResponse.Content.ReadAsStringAsync();
                    using var episodeDoc = JsonDocument.Parse(episodePayload);
                    if (episodeDoc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        var sampleFilePath = episodeDoc.RootElement
                            .EnumerateArray()
                            .Select(x => GetJsonString(x, "path"))
                            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x) && x.Trim().StartsWith(mapping.RemoteRootPath, StringComparison.OrdinalIgnoreCase))
                            ?? episodeDoc.RootElement
                                .EnumerateArray()
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
                    else
                    {
                        messages.Add("Deep test skipped: unexpected Sonarr episode file payload.");
                    }
                }
                else
                {
                    messages.Add($"Deep test skipped: could not read Sonarr episode files (HTTP {(int)episodeFileResponse.StatusCode}).");
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

app.MapPost("/api/integrations/{id:long}/sync", async (long id, TriggerIntegrationSyncRequest request, MediaCloudDbContext db, IHttpClientFactory httpClientFactory) =>
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

    if (!string.Equals(integration.ServiceKey, "radarr", StringComparison.OrdinalIgnoreCase))
    {
        state.LastError = $"Sync adapter not implemented yet for service '{integration.ServiceKey}'.";
        state.ConsecutiveFailureCount += 1;
        await db.SaveChangesAsync();
        return Results.Ok(new TriggerIntegrationSyncResponse(id, false, state.LastError));
    }

    try
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
            state.LastError = string.IsNullOrWhiteSpace(body)
                ? $"Sync failed with HTTP {(int)response.StatusCode}."
                : body[..Math.Min(250, body.Length)];
            state.ConsecutiveFailureCount += 1;
            state.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new TriggerIntegrationSyncResponse(id, false, state.LastError));
        }

        var payload = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            state.LastError = "Unexpected Radarr response payload.";
            state.ConsecutiveFailureCount += 1;
            state.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new TriggerIntegrationSyncResponse(id, false, state.LastError));
        }

        var processed = 0;
        var syncSeenAt = DateTimeOffset.UtcNow;
        var pathMapping = await db.LibraryPathMappings.FirstOrDefaultAsync(x => x.IntegrationId == integration.Id);
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
            var hasFile = GetJsonBool(movie, "hasFile") ?? false;
            var rawFilePath = GetNestedJsonString(movie, "movieFile", "path") ?? string.Empty;
            var resolvedFilePath = ResolveLocalPath(pathMapping, rawFilePath);
            var qualityProfile = GetJsonInt(movie, "qualityProfileId")?.ToString() ?? string.Empty;
            var sourceUpdatedAt = ParseJsonDateTimeOffset(movie, "added");

            var canonicalKey = BuildCanonicalMovieKey(tmdbId, imdbId, title, year);
            var libraryItem = await db.LibraryItems.FirstOrDefaultAsync(x => x.CanonicalKey == canonicalKey);
            if (libraryItem is null)
            {
                libraryItem = new LibraryItem
                {
                    CanonicalKey = canonicalKey,
                    CreatedAtUtc = syncSeenAt
                };
                db.LibraryItems.Add(libraryItem);
                await db.SaveChangesAsync();
            }

            libraryItem.MediaType = "Movie";
            libraryItem.Title = title;
            libraryItem.SortTitle = sortTitle;
            libraryItem.Year = year;
            libraryItem.TmdbId = tmdbId;
            libraryItem.ImdbId = imdbId;
            libraryItem.RuntimeMinutes = runtime;
            var previousFilePath = libraryItem.PrimaryFilePath;
            libraryItem.PrimaryFilePath = resolvedFilePath;
            if (hasFile && !string.IsNullOrWhiteSpace(resolvedFilePath) &&
                (libraryItem.ActualRuntimeMinutes is null || !string.Equals(previousFilePath, resolvedFilePath, StringComparison.Ordinal)))
            {
                libraryItem.ActualRuntimeMinutes = ProbeRuntimeMinutes(resolvedFilePath);
            }

            libraryItem.IsAvailable = hasFile;
            libraryItem.QualityProfile = qualityProfile;
            libraryItem.SourceUpdatedAtUtc = sourceUpdatedAt;
            libraryItem.UpdatedAtUtc = syncSeenAt;

            var link = await db.LibraryItemSourceLinks.FirstOrDefaultAsync(x =>
                x.LibraryItemId == libraryItem.Id &&
                x.IntegrationId == integration.Id &&
                x.ExternalId == externalId);

            if (link is null)
            {
                link = new LibraryItemSourceLink
                {
                    LibraryItemId = libraryItem.Id,
                    IntegrationId = integration.Id,
                    ExternalId = externalId,
                    FirstSeenAtUtc = syncSeenAt
                };
                db.LibraryItemSourceLinks.Add(link);
            }

            link.ExternalType = "movie";
            link.ExternalUpdatedAtUtc = sourceUpdatedAt;
            link.LastSeenAtUtc = syncSeenAt;
            link.IsDeletedAtSource = false;
            link.SourcePayloadHash = externalId;

            await UpsertRuntimeMismatchIssueAsync(db, libraryItem, runtimePolicy, syncSeenAt, runtimeMismatchIssueType, runtimePolicyVersion);

            processed += 1;
        }

        state.LastSuccessfulAtUtc = syncSeenAt;
        state.LastError = string.Empty;
        state.ConsecutiveFailureCount = 0;
        state.UpdatedAtUtc = syncSeenAt;
        await db.SaveChangesAsync();

        return Results.Ok(new TriggerIntegrationSyncResponse(id, true, $"Synced {processed} Radarr item(s)."));
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

    var query = ApplyLibraryItemFilters(db.LibraryItems.AsQueryable(), mediaType, q, available);
    query = ApplyLibraryItemSort(query, sortBy, sortDir);

    var rows = await query
        .Skip(skip)
        .Take(limit)
        .ToListAsync();

    var items = rows.Select(MapLibraryItemDto).ToList();
    return Results.Ok(items);
}).RequireAuthorization();

app.MapGet("/api/library/items/count", async (MediaCloudDbContext db, string? mediaType, string? q, bool? available) =>
{
    var query = ApplyLibraryItemFilters(db.LibraryItems.AsQueryable(), mediaType, q, available);
    var total = await query.CountAsync();
    return Results.Ok(new LibraryItemCountResponse(total));
}).RequireAuthorization();

app.MapGet("/api/library/items/jump", async (MediaCloudDbContext db, string token, string? mediaType, string? q, bool? available, string? sortBy, string? sortDir, int? pageSize) =>
{
    if (string.IsNullOrWhiteSpace(token))
    {
        return Results.BadRequest(new ErrorResponse("Token is required."));
    }

    var normalizedToken = token.Trim().ToUpperInvariant();
    var size = Math.Clamp(pageSize ?? 100, 1, 500);

    var query = ApplyLibraryItemFilters(db.LibraryItems.AsQueryable(), mediaType, q, available);
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

app.MapGet("/api/library/items/{id:long}", async (long id, MediaCloudDbContext db) =>
{
    var row = await db.LibraryItems.FirstOrDefaultAsync(x => x.Id == id);
    if (row is null)
    {
        return Results.NotFound(new ErrorResponse("Library item not found."));
    }

    return Results.Ok(MapLibraryItemDto(row));
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
        return Results.Ok(new LibraryItemRuntimeProbeResponse(row.Id, row.MediaType, row.Title, filePath, false, false, null, "No primary file path available to probe.", null, string.Empty));
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
        row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await UpsertRuntimeMismatchIssueAsync(db, row, runtimePolicy, row.UpdatedAtUtc, runtimeMismatchIssueType, runtimePolicyVersion);
        await db.SaveChangesAsync();
        return Results.Ok(new LibraryItemRuntimeProbeResponse(row.Id, row.MediaType, row.Title, filePath, false, false, null, "Resolved file path does not exist on this host. Try integration sync if mappings changed.", null, string.Empty));
    }

    var probe = ProbeRuntime(filePath);
    var runtimeMinutes = probe.RuntimeMinutes;
    row.PrimaryFilePath = filePath;
    row.ActualRuntimeMinutes = runtimeMinutes;
    row.UpdatedAtUtc = DateTimeOffset.UtcNow;
    await UpsertRuntimeMismatchIssueAsync(db, row, runtimePolicy, row.UpdatedAtUtc, runtimeMismatchIssueType, runtimePolicyVersion);
    await db.SaveChangesAsync();

    var success = runtimeMinutes.HasValue && runtimeMinutes.Value > 0;
    var message = success
        ? $"Runtime reprobe complete: {runtimeMinutes:0.##} min."
        : string.IsNullOrWhiteSpace(probe.Error)
            ? "Runtime reprobe completed but ffprobe did not return a usable duration."
            : $"Runtime reprobe failed: {probe.Error}";

    return Results.Ok(new LibraryItemRuntimeProbeResponse(row.Id, row.MediaType, row.Title, filePath, true, success, runtimeMinutes, message, probe.ExitCode, probe.Error));
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
            link.ExternalId,
            link.ExternalType,
            link.ExternalUpdatedAtUtc,
            link.LastSeenAtUtc,
            link.IsDeletedAtSource))
        .ToListAsync();

    return Results.Ok(rows);
}).RequireAuthorization();

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

    var take = Math.Clamp(request.Take <= 0 ? 200 : request.Take, 1, 2000);
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

    var inspected = 0;
    var attempted = 0;
    var updated = 0;
    var missingFiles = 0;
    var failed = 0;

    foreach (var item in candidates)
    {
        inspected++;
        var filePath = (item.PrimaryFilePath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            missingFiles++;
            item.ActualRuntimeMinutes = null;
            item.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await UpsertRuntimeMismatchIssueAsync(db, item, runtimePolicy, item.UpdatedAtUtc, runtimeMismatchIssueType, runtimePolicyVersion);
            continue;
        }

        attempted++;
        var runtime = ProbeRuntimeMinutes(filePath);
        if (runtime.HasValue && runtime.Value > 0)
        {
            item.ActualRuntimeMinutes = runtime;
            item.UpdatedAtUtc = DateTimeOffset.UtcNow;
            updated++;
        }
        else
        {
            item.ActualRuntimeMinutes = null;
            item.UpdatedAtUtc = DateTimeOffset.UtcNow;
            failed++;
        }

        await UpsertRuntimeMismatchIssueAsync(db, item, runtimePolicy, item.UpdatedAtUtc, runtimeMismatchIssueType, runtimePolicyVersion);
    }

    await db.SaveChangesAsync();

    return Results.Ok(new BatchRuntimeReprobeResponse(inspected, attempted, updated, missingFiles, failed));
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

static LibraryItemDto MapLibraryItemDto(LibraryItem item)
{
    var audio = DeserializeLanguages(item.AudioLanguagesJson);
    var subtitles = DeserializeLanguages(item.SubtitleLanguagesJson);

    return new LibraryItemDto(
        item.Id,
        item.CanonicalKey,
        item.MediaType,
        item.Title,
        item.SortTitle,
        item.Year,
        item.TmdbId,
        item.TvdbId,
        item.ImdbId,
        item.PlexRatingKey,
        item.RuntimeMinutes,
        item.ActualRuntimeMinutes,
        audio,
        subtitles,
        item.IsAvailable,
        item.QualityProfile,
        item.SourceUpdatedAtUtc,
        item.UpdatedAtUtc,
        item.PrimaryFilePath);
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

static IQueryable<LibraryItem> ApplyLibraryItemFilters(IQueryable<LibraryItem> query, string? mediaType, string? q, bool? available)
{
    if (!string.IsNullOrWhiteSpace(mediaType))
    {
        query = query.Where(x => x.MediaType == mediaType);
    }

    if (!string.IsNullOrWhiteSpace(q))
    {
        var needle = q.Trim();
        query = query.Where(x => x.Title.Contains(needle) || x.CanonicalKey.Contains(needle));
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

static void ApplyIntegrationAuthHeaders(IntegrationConfig integration, HttpRequestMessage request)
{
    var authType = IntegrationCatalog.NormalizeAuthType(integration.AuthType);
    if (authType == "ApiKey")
    {
        request.Headers.Add("X-Api-Key", integration.ApiKey);
        return;
    }

    if (authType == "Basic")
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{integration.Username}:{integration.Password}"));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", encoded);
    }
}

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

static int? GetJsonInt(JsonElement element, string property)
{
    if (!element.TryGetProperty(property, out var value)) return null;
    return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var v) ? v : null;
}

static double? GetJsonDouble(JsonElement element, string property)
{
    if (!element.TryGetProperty(property, out var value)) return null;
    return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var v) ? v : null;
}

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

static RuntimeProbeResult ProbeRuntime(string filePath)
{
    try
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return new RuntimeProbeResult(null, null, "File not found.");
        }

        var psi = new ProcessStartInfo
        {
            FileName = "ffprobe",
            Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{filePath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        if (proc is null)
        {
            return new RuntimeProbeResult(null, null, "Failed to start ffprobe process.");
        }

        if (!proc.WaitForExit(5000))
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            return new RuntimeProbeResult(null, null, "ffprobe timed out after 5s.");
        }

        var stdout = proc.StandardOutput.ReadToEnd().Trim();
        var stderr = proc.StandardError.ReadToEnd().Trim();
        var exitCode = proc.ExitCode;

        if (double.TryParse(stdout, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
        {
            return new RuntimeProbeResult(Math.Round(seconds / 60d, 2), exitCode, string.Empty);
        }

        var error = !string.IsNullOrWhiteSpace(stderr)
            ? stderr[..Math.Min(300, stderr.Length)]
            : (exitCode != 0
                ? $"ffprobe exited with code {exitCode}."
                : "ffprobe returned no duration output.");

        return new RuntimeProbeResult(null, exitCode, error);
    }
    catch (Exception ex)
    {
        var error = ex.Message;
        if (error.Length > 300) error = error[..300];
        return new RuntimeProbeResult(null, null, error);
    }
}

static double? ProbeRuntimeMinutes(string filePath)
    => ProbeRuntime(filePath).RuntimeMinutes;

file sealed record RuntimePolicyValues(double ToleranceMinutesFloor, double TolerancePercent, double WarningPercent, double HighMinutes, double CriticalPercent, double CriticalMinutes);
file sealed record RuntimeMismatchEvaluation(bool IsMismatch, double DiffMinutes, double DiffPercent, double ThresholdMinutes, string Severity);
file sealed record RuntimeProbeResult(double? RuntimeMinutes, int? ExitCode, string Error);

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
public record TriggerIntegrationSyncRequest(bool ForceFullResync);
public record TriggerIntegrationSyncResponse(long IntegrationId, bool Accepted, string Message);
public record IntegrationSyncStateDto(long IntegrationId, DateTimeOffset? LastAttemptedAtUtc, DateTimeOffset? LastSuccessfulAtUtc, string LastCursor, string LastEtag, string LastError, int ConsecutiveFailureCount, DateTimeOffset UpdatedAtUtc);
public record LibraryItemDto(long Id, string CanonicalKey, string MediaType, string Title, string SortTitle, int? Year, int? TmdbId, int? TvdbId, string ImdbId, string PlexRatingKey, double? RuntimeMinutes, double? ActualRuntimeMinutes, IReadOnlyList<string> AudioLanguages, IReadOnlyList<string> SubtitleLanguages, bool IsAvailable, string QualityProfile, DateTimeOffset? SourceUpdatedAtUtc, DateTimeOffset UpdatedAtUtc, string PrimaryFilePath);
public record LibraryItemRuntimeProbeResponse(long Id, string MediaType, string Title, string PrimaryFilePath, bool FileExists, bool Success, double? ActualRuntimeMinutes, string Message, int? ProbeExitCode, string ProbeError);
public record BatchRuntimeReprobeRequest(string? MediaType, int Take = 200, bool ForceAll = false);
public record BatchRuntimeReprobeResponse(int Inspected, int Attempted, int Updated, int MissingFiles, int Failed);
public record LibraryItemSourceLinkDto(long Id, long LibraryItemId, long IntegrationId, string ServiceKey, string InstanceName, string ExternalId, string ExternalType, DateTimeOffset? ExternalUpdatedAtUtc, DateTimeOffset LastSeenAtUtc, bool IsDeletedAtSource);
public record LibraryItemCountResponse(int Total);
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

public static class IntegrationCatalog
{
    public static readonly (string Key, string Name)[] SupportedServices =
    [
        ("overseerr", "Overseerr"),
        ("radarr", "Radarr"),
        ("sonarr", "Sonarr"),
        ("lidarr", "Lidarr"),
        ("prowlarr", "Prowlarr"),
        ("plex", "Plex")
    ];

    private static readonly string[] SupportedAuthTypes = ["None", "ApiKey", "Basic"];

    public static bool IsSupported(string key) => SupportedServices.Any(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
    public static string GetName(string key) => SupportedServices.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Name ?? key;

    public static bool ServiceRequiresCredentials(string key) => IsSupported(key);
    public static IReadOnlyList<string> GetAllowedAuthTypesForService(string key)
        => ServiceRequiresCredentials(key) ? ["ApiKey", "Basic"] : ["None", "ApiKey", "Basic"];

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
