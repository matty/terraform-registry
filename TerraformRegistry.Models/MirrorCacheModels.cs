namespace TerraformRegistry.Models;

public sealed record MirrorProviderIndex
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Hostname { get; init; }
    public required string Namespace { get; init; }
    public required string Type { get; init; }
    public required string VersionsJson { get; init; }
    public string? ETag { get; init; }
    public string State { get; init; } = "pending";
    public string? LastError { get; init; }
    public DateTime? LastSyncAt { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
}

public sealed record MirrorProviderPackage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Hostname { get; init; }
    public required string Namespace { get; init; }
    public required string Type { get; init; }
    public required string Version { get; init; }
    public required string Os { get; init; }
    public required string Arch { get; init; }
    public required string DownloadUrl { get; init; }
    public string? Filename { get; init; }
    public string? PackageStoragePath { get; init; }
    public long? SizeBytes { get; init; }
    public string ProtocolsJson { get; init; } = "[]";
    public string HashesJson { get; init; } = "[]";
    public string? Shasum { get; init; }
    public string? SigningKeysJson { get; init; }
    public string State { get; init; } = "pending";
    public string? LastError { get; init; }
    public int? HttpStatusCode { get; init; }
    public DateTime? LastSyncAt { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
}

public sealed record MirrorModuleVersions
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Hostname { get; init; }
    public required string Namespace { get; init; }
    public required string Name { get; init; }
    public required string Provider { get; init; }
    public required string VersionsJson { get; init; }
    public string? ETag { get; init; }
    public string State { get; init; } = "pending";
    public string? LastError { get; init; }
    public DateTime? LastSyncAt { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
}

public sealed record MirrorModulePackage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Hostname { get; init; }
    public required string Namespace { get; init; }
    public required string Name { get; init; }
    public required string Provider { get; init; }
    public required string Version { get; init; }
    public required string DownloadUrl { get; init; }
    public string? Source { get; init; }
    public string? PackageStoragePath { get; init; }
    public long? SizeBytes { get; init; }
    public string? MetadataJson { get; init; }
    public string State { get; init; } = "pending";
    public string? LastError { get; init; }
    public int? HttpStatusCode { get; init; }
    public DateTime? LastSyncAt { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
}

public sealed record MirrorCacheLease
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string LeaseKey { get; init; }
    public required string OperationType { get; init; }
    public required string OwnerInstanceId { get; init; }
    public required DateTime ExpiresAt { get; init; }
    public DateTime? HeartbeatAt { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
}

public sealed record MirrorLeaseHandle
{
    public required Guid Id { get; init; }
    public required string LeaseKey { get; init; }
    public required string OperationType { get; init; }
    public required string OwnerInstanceId { get; init; }
    public required DateTime ExpiresAt { get; init; }
}
