using System.Diagnostics.Metrics;
using DotNet.Testcontainers.Builders;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Migrations;
using TerraformRegistry.Models;
using TerraformRegistry.PostgreSQL;
using TerraformRegistry.Services;
using Testcontainers.PostgreSql;

namespace TerraformRegistry.Tests.UnitTests.Database;

public sealed class SqliteOperationalDatabaseMetricsFacadeTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly string _connectionString;

    public SqliteOperationalDatabaseMetricsFacadeTests()
    {
        _connectionString = $"Data Source=operational_metrics_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _connection = new SqliteConnection(_connectionString);
        _connection.Open();
        new DbUpMigrator(NullLogger<DbUpMigrator>.Instance).Migrate("sqlite", _connectionString);
    }

    [Fact]
    public async Task ListModulesEmitsOnlyBoundedSqlitePageTagsFromTheDatabaseFacade()
    {
        var database = new SqliteDatabaseService(
            _connectionString,
            "http://localhost",
            NullLogger<SqliteDatabaseService>.Instance,
            new DbUpMigrator(NullLogger<DbUpMigrator>.Instance));
        await database.AddModuleAsync(OperationalDatabaseMetricTestData.CreateModule());

        using var listener = new OperationalDatabaseMetricListener();
        var page = await database.ListModulesAsync(new ModuleSearchRequest { Limit = 10, Offset = 0 });

        Assert.Single(page.Modules);
        listener.AssertModulePage("sqlite", 1);
    }

    public void Dispose() => _connection.Dispose();
}

[Trait("Category", "Integration")]
public sealed class PostgreSqlOperationalDatabaseMetricsFacadeTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;
    private string _connectionString = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder("postgres:15.1")
            .WithDatabase("operational_metrics")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5432))
            .Build();
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();
        new DbUpMigrator(NullLogger<DbUpMigrator>.Instance).Migrate("postgres", _connectionString);
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task ListModulesEmitsOnlyBoundedPostgreSqlPageTagsFromTheDatabaseFacade()
    {
        var database = new PostgreSqlDatabaseService(
            _connectionString,
            "http://localhost",
            NullLogger<PostgreSqlDatabaseService>.Instance,
            new DbUpMigrator(NullLogger<DbUpMigrator>.Instance));
        await database.AddModuleAsync(OperationalDatabaseMetricTestData.CreateModule());

        using var listener = new OperationalDatabaseMetricListener();
        var page = await database.ListModulesAsync(new ModuleSearchRequest { Limit = 10, Offset = 0 });

        Assert.Single(page.Modules);
        listener.AssertModulePage("postgresql", 1);
    }
}

internal sealed class OperationalDatabaseMetricListener : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly List<(string Name, long Value, IReadOnlyDictionary<string, string?> Tags)> _measurements = [];

    public OperationalDatabaseMetricListener()
    {
        _listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "TerraformRegistry.Operations")
                meterListener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            _measurements.Add((instrument.Name, value, tags.ToArray().ToDictionary(
                tag => tag.Key,
                tag => tag.Value?.ToString(),
                StringComparer.Ordinal))));
        _listener.Start();
    }

    public void AssertModulePage(string backend, int returnedRows)
    {
        var pageMeasurements = _measurements.Where(measurement =>
            measurement.Name.StartsWith("terraform_registry.database.paginated_list.", StringComparison.Ordinal) &&
            measurement.Tags.TryGetValue("backend", out var recordedBackend) && recordedBackend == backend).ToList();

        Assert.Contains(pageMeasurements, measurement =>
            measurement.Name == "terraform_registry.database.paginated_list.duration_ms");
        Assert.Contains(pageMeasurements, measurement =>
            measurement.Name == "terraform_registry.database.paginated_list.returned_rows" && measurement.Value == returnedRows);
        Assert.All(pageMeasurements, measurement =>
        {
            Assert.Equal(["backend", "list"], measurement.Tags.Keys.OrderBy(key => key, StringComparer.Ordinal));
            Assert.Equal(backend, measurement.Tags["backend"]);
            Assert.Equal("modules", measurement.Tags["list"]);
        });
    }

    public void Dispose() => _listener.Dispose();
}

internal static class OperationalDatabaseMetricTestData
{
    public static ModuleStorage CreateModule() => new()
    {
        Namespace = "metrics",
        Name = "module",
        Provider = "test",
        Version = "1.0.0",
        Description = "Operational metric test module",
        FilePath = "modules/metrics/module/test/1.0.0.zip",
        PublishedAt = DateTime.UtcNow,
        Dependencies = []
    };
}
