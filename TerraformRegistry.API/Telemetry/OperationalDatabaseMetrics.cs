using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace TerraformRegistry.API.Telemetry;

/// <summary>Shared measurements for database-backed paginated list operations.</summary>
public static class OperationalDatabaseMetrics
{
    private static readonly Meter Meter = new("TerraformRegistry.Operations");
    private static readonly Histogram<long> DurationMilliseconds =
        Meter.CreateHistogram<long>("terraform_registry.database.paginated_list.duration_ms");
    private static readonly Histogram<long> ReturnedRows =
        Meter.CreateHistogram<long>("terraform_registry.database.paginated_list.returned_rows");

    public static void RecordModulePage(string backend, TimeSpan elapsed, int returnedRows)
    {
        var tags = new TagList { { "backend", backend }, { "list", "modules" } };
        DurationMilliseconds.Record(Math.Max(0, (long)elapsed.TotalMilliseconds), tags);
        ReturnedRows.Record(Math.Max(0, returnedRows), tags);
    }
}
