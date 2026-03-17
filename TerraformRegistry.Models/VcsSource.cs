using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

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

    [Column("pat_encrypted")]
    [JsonIgnore]
    public string? PatEncrypted { get; set; }

    [Column("webhook_secret")]
    [JsonIgnore]
    public string WebhookSecret { get; set; } = string.Empty;

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
