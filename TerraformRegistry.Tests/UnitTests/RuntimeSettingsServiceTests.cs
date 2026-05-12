using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using TerraformRegistry.Migrations;
using TerraformRegistry.Services;

namespace TerraformRegistry.Tests.UnitTests;

public class RuntimeSettingsServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly string _connectionString;

    public RuntimeSettingsServiceTests()
    {
        var dbName = $"RuntimeSettings_{Guid.NewGuid():N}";
        _connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";
        _connection = new SqliteConnection(_connectionString);
        _connection.Open();

        var migrator = new DbUpMigrator(NullLogger<DbUpMigrator>.Instance);
        migrator.Migrate("sqlite", _connectionString);
    }

    [Fact]
    public async Task GetAsyncReturnsNullWhenSettingDoesNotExist()
    {
        var service = new SqliteRuntimeSettingsService(_connectionString);

        var setting = await service.GetAsync("module_extraction", CancellationToken.None);

        Assert.Null(setting);
    }

    [Fact]
    public async Task SetAsyncUpsertsJsonValueAndAuditFields()
    {
        var service = new SqliteRuntimeSettingsService(_connectionString);

        await service.SetAsync("module_extraction", """{"enabled":false}""", "user-123", CancellationToken.None);
        await service.SetAsync("module_extraction", """{"enabled":true}""", "user-456", CancellationToken.None);

        var setting = await service.GetAsync("module_extraction", CancellationToken.None);

        Assert.NotNull(setting);
        Assert.Equal("module_extraction", setting!.Key);
        Assert.Equal("""{"enabled":true}""", setting.ValueJson);
        Assert.Equal("user-456", setting.UpdatedBy);
        Assert.True(setting.UpdatedAt > DateTime.UtcNow.AddMinutes(-1));
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
