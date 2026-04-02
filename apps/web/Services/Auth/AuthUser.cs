namespace web.Services.Auth;

public record AuthUser(Guid UserId, string Username, string Role);
public record LoginResponse(Guid UserId, string Username, string Role, string Token);
public record UserSummaryResponse(Guid Id, string Username, string Role, DateTimeOffset CreatedAtUtc);
public record UserAuditLogResponse(long Id, DateTimeOffset OccurredAtUtc, Guid? ActorUserId, string ActorUsername, Guid TargetUserId, string TargetUsername, string Action, string Summary);

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
