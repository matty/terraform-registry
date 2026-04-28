namespace TerraformRegistry.API.Utilities;

/// <summary>
///     Sorts valid Terraform module versions by Semantic Versioning precedence.
/// </summary>
public sealed class SemVerVersionComparer : IComparer<string>
{
    public static readonly SemVerVersionComparer Instance = new();

    private SemVerVersionComparer()
    {
    }

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        return SemVerValidator.Compare(x, y) ?? string.Compare(x, y, StringComparison.Ordinal);
    }
}
