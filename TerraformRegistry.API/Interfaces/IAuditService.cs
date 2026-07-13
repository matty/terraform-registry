namespace TerraformRegistry.API.Interfaces;

public interface IAuditService
{
    Task LogAsync(string? userId, string action, string resourceType, string? resourceId, object? details, string? ipAddress);
    Task<AuditLogPage> QueryAsync(string? action, string? userId, string? resourceType, DateTime? from, DateTime? toTimestamp, int limit = 50, int offset = 0);
    Task<AuditLogEntry?> GetAsync(Guid id);
}

public interface IAuditLogStore : IAuditService
{
}

public record AuditLogEntry(Guid Id, string? UserId, string Action, string ResourceType, string? ResourceId, string? Details, string? IpAddress, DateTime Timestamp);
public record AuditLogPage(IReadOnlyList<AuditLogEntry> Entries, int Total);
