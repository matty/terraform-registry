using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace TerraformRegistry.Models;

[Table("vcs_connections")]
public class VcsConnection
{
    [Key] [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("label")] [Required]
    public string Label { get; set; } = string.Empty;

    [Column("provider")]
    public string Provider { get; set; } = "github";

    [Column("pat_encrypted")]
    [JsonIgnore]
    public string? PatEncrypted { get; set; }

    [Column("default_org")]
    public string? DefaultOrg { get; set; }

    [Column("webhook_secret")]
    [JsonIgnore]
    public string WebhookSecret { get; set; } = string.Empty;

    [Column("created_by")]
    public string? CreatedBy { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
