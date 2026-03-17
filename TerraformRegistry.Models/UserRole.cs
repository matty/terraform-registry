using System.ComponentModel.DataAnnotations.Schema;

namespace TerraformRegistry.Models;

[Table("user_roles")]
public class UserRole
{
    [Column("user_id")]
    public string UserId { get; set; } = string.Empty;

    [Column("role_id")]
    public Guid RoleId { get; set; }

    [Column("assigned_at")]
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

    [Column("assigned_by")]
    public string? AssignedBy { get; set; }
}
