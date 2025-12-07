using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace TerraformRegistry.Models;

[Table("api_keys")]
public class ApiKey
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("user_id")]
    [ForeignKey("User")]
    public string UserId { get; set; } = string.Empty;

    [JsonIgnore]
    public virtual User? User { get; set; }

    [Column("description")]
    [Required]
    [MaxLength(255)]
    public string Description { get; set; } = string.Empty;

    [Column("token_hash")]
    [Required]
    [JsonIgnore]
    public string TokenHash { get; set; } = string.Empty;

    [Column("prefix")]
    [Required]
    [MaxLength(10)]
    public string Prefix { get; set; } = string.Empty;

    [Column("is_shared")]
    public bool IsShared { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("expires_at")]
    public DateTime? ExpiresAt { get; set; }

    [Column("last_used_at")]
    public DateTime? LastUsedAt { get; set; }
}
