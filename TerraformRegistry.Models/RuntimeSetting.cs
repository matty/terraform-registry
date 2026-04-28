namespace TerraformRegistry.Models;

public sealed class RuntimeSetting
{
    public required string Key { get; init; }
    public required string ValueJson { get; init; }
    public DateTime UpdatedAt { get; init; }
    public string? UpdatedBy { get; init; }
}
