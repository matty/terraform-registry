using Microsoft.Data.Sqlite;
using TerraformRegistry.Models;
using TerraformRegistry.Services.Sqlite;

namespace TerraformRegistry.Tests.UnitTests.Database;

public sealed class SqliteOutboxEventRepositoryTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly string connectionString;

    public SqliteOutboxEventRepositoryTests()
    {
        connectionString = $"Data Source=outbox-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE durable_outbox_events (
                id TEXT PRIMARY KEY, kind TEXT NOT NULL, idempotency_key TEXT NOT NULL UNIQUE, payload_json TEXT NOT NULL,
                state TEXT NOT NULL, owner_id TEXT, lease_expires_at TEXT, attempt_count INTEGER NOT NULL DEFAULT 0,
                last_error TEXT, created_at TEXT NOT NULL, updated_at TEXT NOT NULL, delivered_at TEXT);
            """;
        command.ExecuteNonQuery();
    }

    [Fact]
    public async Task ClaimAndCompleteDeliversAnEnqueuedEventOnlyOnce()
    {
        var repository = new SqliteOutboxEventRepository(connectionString);
        var now = DateTime.UtcNow;
        var @event = new OutboxEvent
        {
            Id = Guid.NewGuid(),
            Kind = "audit",
            IdempotencyKey = "audit:1",
            PayloadJson = "{}",
            State = OutboxEventState.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };

        Assert.True(await repository.EnqueueAsync(@event));
        Assert.False(await repository.EnqueueAsync(@event with { Id = Guid.NewGuid() }));
        var claimed = await repository.TryClaimNextAsync("worker-a", TimeSpan.FromMinutes(1));
        Assert.NotNull(claimed);
        Assert.Equal(OutboxEventState.Processing, claimed.State);
        Assert.True(await repository.TryCompleteAsync(claimed.Id, "worker-a"));
        Assert.Null(await repository.TryClaimNextAsync("worker-b", TimeSpan.FromMinutes(1)));
    }

    public void Dispose() => connection.Dispose();
}
