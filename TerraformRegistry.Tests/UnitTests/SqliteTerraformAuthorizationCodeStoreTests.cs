using System.Globalization;
using Microsoft.Data.Sqlite;
using TerraformRegistry.Services;

namespace TerraformRegistry.Tests.UnitTests;

public sealed class SqliteTerraformAuthorizationCodeStoreTests
{
    [Fact]
    public void CodeCreatedByOneStoreInstanceIsConsumedOnceByAnother()
    {
        var database = Path.Combine(Path.GetTempPath(), $"terraform-auth-code-{Guid.NewGuid():N}.db");
        try
        {
            CreateSchema(database);
            var options = new TerraformLoginOptions { AuthorizationCodeLifetime = TimeSpan.FromMinutes(5) };
            var issuer = new SqliteTerraformAuthorizationCodeStore($"Data Source={database}", options);
            var consumer = new SqliteTerraformAuthorizationCodeStore($"Data Source={database}", options);

            var issued = issuer.Create(new TerraformAuthorizationCodeCreateRequest(
                "user-1", "terraform-cli", "http://127.0.0.1:10000/", "state", "challenge", "S256"));

            var first = consumer.Consume(issued.Code, "terraform-cli", "http://127.0.0.1:10000/");
            var second = issuer.Consume(issued.Code, "terraform-cli", "http://127.0.0.1:10000/");

            Assert.NotNull(first);
            Assert.Equal("user-1", first!.UserId);
            Assert.Null(second);
        }
        finally
        {
            File.Delete(database);
        }
    }

    [Fact]
    public void IncorrectClientOrRedirectDoesNotConsumeTheCode()
    {
        var database = Path.Combine(Path.GetTempPath(), $"terraform-auth-code-{Guid.NewGuid():N}.db");
        try
        {
            CreateSchema(database);
            var store = new SqliteTerraformAuthorizationCodeStore($"Data Source={database}", new TerraformLoginOptions());
            var issued = store.Create(new TerraformAuthorizationCodeCreateRequest(
                "user-1", "terraform-cli", "http://127.0.0.1:10000/", "state", "challenge", "S256"));

            Assert.Null(store.Consume(issued.Code, "other-client", "http://127.0.0.1:10000/"));
            Assert.Null(store.Consume(issued.Code, "terraform-cli", "http://127.0.0.1:20000/"));
            Assert.NotNull(store.Consume(issued.Code, "terraform-cli", "http://127.0.0.1:10000/"));
        }
        finally
        {
            File.Delete(database);
        }
    }

    [Fact]
    public void ExpiredCodeCannotBeConsumed()
    {
        var database = Path.Combine(Path.GetTempPath(), $"terraform-auth-code-{Guid.NewGuid():N}.db");
        try
        {
            CreateSchema(database);
            var store = new SqliteTerraformAuthorizationCodeStore($"Data Source={database}", new TerraformLoginOptions
            {
                AuthorizationCodeLifetime = TimeSpan.FromMilliseconds(-1)
            });
            var issued = store.Create(new TerraformAuthorizationCodeCreateRequest(
                "user-1", "terraform-cli", "http://127.0.0.1:10000/", "state", "challenge", "S256"));

            Assert.Null(store.Consume(issued.Code, "terraform-cli", "http://127.0.0.1:10000/"));
            Assert.Equal(0, CountCodes(database));
        }
        finally
        {
            File.Delete(database);
        }
    }

    private static void CreateSchema(string database)
    {
        using var connection = new SqliteConnection($"Data Source={database}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE terraform_authorization_codes (
                code_hash TEXT PRIMARY KEY,
                user_id TEXT NOT NULL,
                client_id TEXT NOT NULL,
                redirect_uri TEXT NOT NULL,
                state TEXT NOT NULL,
                code_challenge TEXT NOT NULL,
                code_challenge_method TEXT NOT NULL,
                expires_at TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private static int CountCodes(string database)
    {
        using var connection = new SqliteConnection($"Data Source={database}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM terraform_authorization_codes;";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }
}
