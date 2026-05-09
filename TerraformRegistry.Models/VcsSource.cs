using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TerraformRegistry.Models;

[Table("vcs_sources")]
public class VcsSource
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("user_id")]
    public string UserId { get; set; } = string.Empty;

    [Column("namespace")]
    public string Namespace { get; set; } = string.Empty;

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("provider")]
    public string Provider { get; set; } = string.Empty;

    [Column("repo_owner")]
    public string RepoOwner { get; set; } = string.Empty;

    [Column("repo_name")]
    public string RepoName { get; set; } = string.Empty;

    [Column("connection_id")]
    public Guid ConnectionId { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("tag_pattern")]
    public string TagPattern { get; set; } = "v*";

    [Column("last_published_version")]
    public string? LastPublishedVersion { get; set; }

    [Column("last_sync_status")]
    public string LastSyncStatus { get; set; } = "never";

    [Column("last_sync_at")]
    public DateTime? LastSyncAt { get; set; }

    [Column("last_sync_error")]
    public string? LastSyncError { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
