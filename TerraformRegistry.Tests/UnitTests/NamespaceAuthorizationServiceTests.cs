using TerraformRegistry.Services;
using Microsoft.Data.Sqlite;

namespace TerraformRegistry.Tests.UnitTests;

public sealed class NamespaceAuthorizationServiceTests
{
    [Fact]
    public async Task MaintainerCanMutateOnlyTheirAssignedNamespace()
    {
        var store = new TestNamespaceMaintainerStore
        {
            Maintainers = { ["owned"] = "owner-user" }
        };
        var service = new NamespaceAuthorizationService(store);

        Assert.True(await service.CanMutateAsync("owned", "owner-user", isSystemOverride: false));
        Assert.False(await service.CanMutateAsync("owned", "other-user", isSystemOverride: false));
        Assert.False(await service.CanMutateAsync("legacy", "owner-user", isSystemOverride: false));
        Assert.True(await service.CanMutateAsync("legacy", "system-user", isSystemOverride: true));
    }

    [Fact]
    public async Task SqliteMaintainerAssignmentSurvivesAStoreRestart()
    {
        var database = Path.Combine(Path.GetTempPath(), $"namespace-maintainer-{Guid.NewGuid():N}.db");
        try
        {
            using (var connection = new SqliteConnection($"Data Source={database}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE namespace_maintainers (namespace TEXT PRIMARY KEY, user_id TEXT NOT NULL, assigned_at TEXT NOT NULL);";
                command.ExecuteNonQuery();
            }

            var writer = new SqliteNamespaceMaintainerStore($"Data Source={database}");
            await writer.AssignMaintainerAsync("owned", "owner-user");
            var reader = new SqliteNamespaceMaintainerStore($"Data Source={database}");

            Assert.Equal("owner-user", await reader.GetMaintainerAsync("owned"));
        }
        finally
        {
            File.Delete(database);
        }
    }

    private sealed class TestNamespaceMaintainerStore : INamespaceMaintainerStore
    {
        public Dictionary<string, string> Maintainers { get; } = new(StringComparer.Ordinal);

        public Task<string?> GetMaintainerAsync(string @namespace) =>
            Task.FromResult(Maintainers.GetValueOrDefault(@namespace));

        public Task AssignMaintainerAsync(string @namespace, string userId)
        {
            Maintainers[@namespace] = userId;
            return Task.CompletedTask;
        }
    }
}
