using System.ComponentModel.DataAnnotations;

namespace AIM.Web.Models;

public class AuditLogEntry
{
    public long Id { get; set; }
    [MaxLength(450)] public string? ActorUserId { get; set; }
    [MaxLength(256)] public string? ActorDisplayName { get; set; }
    [Required, MaxLength(50)] public string Action { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string EntityType { get; set; } = string.Empty;
    [MaxLength(100)] public string? EntityId { get; set; }
    public string? OldValuesJson { get; set; }
    public string? NewValuesJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(45)] public string? IpAddress { get; set; }
}

public static class AuditAction
{
    public const string Create = "Create";
    public const string Update = "Update";
    public const string Transition = "Transition";
    public const string Submit = "Submit";
    public const string Delete = "Delete";
    public const string Login = "Login";
    public const string Logout = "Logout";
    public const string ImportBatch = "ImportBatch";
}
