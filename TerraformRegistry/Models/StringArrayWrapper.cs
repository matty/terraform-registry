namespace TerraformRegistry.Models;

/// <summary>
///     Wrapper class for use in JsonSerialization where an array of strings is used.
///     This replaces anonymous types like new { property = new string[] { ... } }
/// </summary>
public class StringArrayWrapper
{
    public string[] Values { get; set; } = Array.Empty<string>();
}