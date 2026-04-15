using AIM.Web.Data;
using AIM.Web.Models;
using System.Text.Json;

namespace AIM.Web.Services;

public interface IAuditLogger
{
    void Log(string action, string entityType, string? entityId, object? oldValues, object? newValues);
}

public class AuditLogger(AimDbContext db, IHttpContextAccessor http) : IAuditLogger
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public void Log(string action, string entityType, string? entityId, object? oldValues, object? newValues)
    {
        var user = http.HttpContext?.User;
        var userId = user?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var name = user?.Identity?.Name;
        var ip = http.HttpContext?.Connection.RemoteIpAddress?.ToString();

        db.AuditLog.Add(new AuditLogEntry
        {
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            ActorUserId = userId,
            ActorDisplayName = name,
            OldValuesJson = oldValues is null ? null : JsonSerializer.Serialize(oldValues, JsonOpts),
            NewValuesJson = newValues is null ? null : JsonSerializer.Serialize(newValues, JsonOpts),
            IpAddress = ip
        });
    }
}
