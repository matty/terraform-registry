using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace TerraformRegistry.Models;

[Table("webhooks")]
public class Webhook
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("user_id")]
    [ForeignKey("User")]
    public string UserId { get; set; } = string.Empty;

    [Column("url")]
    [Required]
    public string Url { get; set; } = string.Empty;

    [Column("secret")]
    [JsonIgnore]
    public string? Secret { get; set; }

    [Column("events")]
    [Required]
    public string[] Events { get; set; } = [];

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
