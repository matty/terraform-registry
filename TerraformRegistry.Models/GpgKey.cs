namespace TerraformRegistry.Models;

/// <summary>
/// Represents a stored GPG Key in the database
/// </summary>
public class GpgKey
{
    public string KeyId { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public string AsciiArmor { get; set; } = string.Empty;
    public string TrustSignature { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
