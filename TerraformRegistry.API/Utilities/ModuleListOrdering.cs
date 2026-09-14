namespace TerraformRegistry.API.Utilities;

/// <summary>
///     Builds the ORDER BY clause for the module coordinate page query.
///     Shared by the repository implementations so every backing store orders identically.
/// </summary>
/// <remarks>
///     The listing is paged in SQL, so ordering has to happen in the same grouped query
///     that applies LIMIT/OFFSET — sorting the rows of a single page would order only
///     that page's contents and silently return the wrong modules.
/// </remarks>
public static class ModuleListOrdering
{
    public const string SortByName = "name";
    public const string SortByPublished = "published";
    public const string SortByVersions = "versions";

    private const string OrderDescending = "desc";

    // Ties are always broken on the full coordinate so paging is stable: without this
    // a module could be repeated on one page and missing from the next.
    private const string CoordinateOrder = "m.namespace ASC, m.name ASC, m.provider ASC";

    /// <summary>
    ///     Returns the ORDER BY clause for the requested sort field and direction.
    ///     Unrecognised input falls back to the registry protocol's coordinate ordering,
    ///     so existing API consumers see no change when they pass no sort.
    /// </summary>
    /// <remarks>
    ///     Every value this returns is a compile-time constant chosen by a switch over
    ///     known keys. Caller input never reaches the returned SQL, so the clause is safe
    ///     to interpolate into a command that cannot parameterise its ORDER BY.
    /// </remarks>
    public static string BuildOrderByClause(string? sort, string? order)
    {
        var direction = string.Equals(order?.Trim(), OrderDescending, StringComparison.OrdinalIgnoreCase)
            ? "DESC"
            : "ASC";

        return sort?.Trim().ToLowerInvariant() switch
        {
            SortByName => $"ORDER BY m.name {direction}, {CoordinateOrder}",
            // A module's recency is the publish date of its most recent version.
            SortByPublished => $"ORDER BY MAX(m.published_at) {direction}, {CoordinateOrder}",
            SortByVersions => $"ORDER BY COUNT(*) {direction}, {CoordinateOrder}",
            _ => $"ORDER BY {CoordinateOrder}"
        };
    }
}
