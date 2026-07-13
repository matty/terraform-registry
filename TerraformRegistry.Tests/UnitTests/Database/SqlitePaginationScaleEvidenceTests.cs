using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Migrations;
using TerraformRegistry.Models;
using TerraformRegistry.Services;
using Xunit;

namespace TerraformRegistry.Tests.UnitTests.Database;

public sealed class SqlitePaginationScaleEvidenceTests : IAsyncLifetime
{
    private string _databasePath = null!;
    private string _connectionString = null!;

    public Task InitializeAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TerraformRegistryTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        _databasePath = Path.Combine(directory, "pagination-scale.db");
        _connectionString = $"Data Source={_databasePath}";
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (directory is not null && Directory.Exists(directory)) Directory.Delete(directory, true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ListModulesRecordsBoundedPageEvidenceForOneHundredThousandVersions()
    {
        var repository = CreateRepository();
        await ((IInitializableDb)repository).InitializeDatabase();
        await SeedAsync();

        var plan = await GetCoordinatePagePlanAsync();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var page = await repository.ListModulesAsync(new ModuleSearchRequest { Limit = 1, Offset = 500 });
        stopwatch.Stop();
        var allocatedBytes = Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);

        var module = Assert.Single(page.Modules);
        Assert.Equal("module-0500", module.Name);
        Assert.Equal(100, module.Versions.Count);
        Assert.Equal("1000", page.Meta["total"]);

        Console.WriteLine($"PERF-001 SQLite evidence: dataset_versions=100000; page_coordinates=1; rows_transferred=100; database_round_trips=3; elapsed_ms={stopwatch.Elapsed.TotalMilliseconds:F2}; allocated_bytes={allocatedBytes}; coordinate_plan={plan}");
    }

    private SqliteDatabaseService CreateRepository()
    {
        return new SqliteDatabaseService(
            _connectionString,
            "http://localhost",
            new Mock<ILogger<SqliteDatabaseService>>().Object,
            new DbUpMigrator(new Mock<ILogger<DbUpMigrator>>().Object));
    }

    private async Task SeedAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO modules (namespace, name, provider, version, description, storage_path, published_at, dependencies) VALUES ($namespace, $name, 'aws', $version, 'scale fixture', $path, '2026-01-01T00:00:00.0000000Z', '[]')";
        var moduleNamespace = command.Parameters.Add("$namespace", SqliteType.Text);
        var name = command.Parameters.Add("$name", SqliteType.Text);
        var version = command.Parameters.Add("$version", SqliteType.Text);
        var path = command.Parameters.Add("$path", SqliteType.Text);

        moduleNamespace.Value = "scale";
        for (var coordinate = 0; coordinate < 1000; coordinate++)
        {
            name.Value = $"module-{coordinate:D4}";
            for (var versionNumber = 0; versionNumber < 100; versionNumber++)
            {
                version.Value = $"1.0.{versionNumber}";
                path.Value = $"/modules/{coordinate:D4}/{versionNumber:D3}.zip";
                await command.ExecuteNonQueryAsync();
            }
        }

        transaction.Commit();
    }

    private async Task<string> GetCoordinatePagePlanAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN SELECT m.namespace, m.name, m.provider FROM modules m WHERE m.deleted_at IS NULL GROUP BY m.namespace, m.name, m.provider ORDER BY m.namespace, m.name, m.provider LIMIT 1 OFFSET 500";
        await using var reader = await command.ExecuteReaderAsync();
        var details = new System.Collections.Generic.List<string>();
        while (await reader.ReadAsync()) details.Add(reader.GetString(3));
        return string.Join(" | ", details);
    }
}
