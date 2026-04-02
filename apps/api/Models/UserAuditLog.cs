namespace api.Models;

public class UserAuditLog
{
    public long Id { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public Guid? ActorUserId { get; set; }
    public string ActorUsername { get; set; } = "system";

    public Guid TargetUserId { get; set; }
    public string TargetUsername { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
}
