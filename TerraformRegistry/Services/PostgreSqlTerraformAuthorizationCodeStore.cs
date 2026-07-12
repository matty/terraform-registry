using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace TerraformRegistry.Services;

public sealed class PostgreSqlTerraformAuthorizationCodeStore(string connectionString, TerraformLoginOptions options)
    : ITerraformAuthorizationCodeStore
{
    public TerraformAuthorizationCode Create(TerraformAuthorizationCodeCreateRequest request)
    {
        var code = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var expiresAt = DateTime.UtcNow.Add(options.AuthorizationCodeLifetime);

        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        DeleteExpired(connection);
        using var command = new NpgsqlCommand("""
            INSERT INTO terraform_authorization_codes
                (code_hash, user_id, client_id, redirect_uri, state, code_challenge, code_challenge_method, expires_at)
            VALUES (@hash, @userId, @clientId, @redirectUri, @state, @challenge, @method, @expiresAt);
            """, connection);
        command.Parameters.AddWithValue("hash", Hash(code));
        command.Parameters.AddWithValue("userId", request.UserId);
        command.Parameters.AddWithValue("clientId", request.ClientId);
        command.Parameters.AddWithValue("redirectUri", request.RedirectUri);
        command.Parameters.AddWithValue("state", request.State);
        command.Parameters.AddWithValue("challenge", request.CodeChallenge);
        command.Parameters.AddWithValue("method", request.CodeChallengeMethod);
        command.Parameters.AddWithValue("expiresAt", expiresAt);
        command.ExecuteNonQuery();

        return new TerraformAuthorizationCode(code, request.UserId, request.ClientId, request.RedirectUri, request.State,
            request.CodeChallenge, request.CodeChallengeMethod, expiresAt);
    }

    public TerraformAuthorizationCode? Consume(string code, string clientId, string redirectUri)
    {
        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        DeleteExpired(connection);
        using var command = new NpgsqlCommand("""
            DELETE FROM terraform_authorization_codes
            WHERE code_hash = @hash AND client_id = @clientId AND redirect_uri = @redirectUri AND expires_at > @now
            RETURNING user_id, client_id, redirect_uri, state, code_challenge, code_challenge_method, expires_at;
            """, connection);
        command.Parameters.AddWithValue("hash", Hash(code));
        command.Parameters.AddWithValue("clientId", clientId);
        command.Parameters.AddWithValue("redirectUri", redirectUri);
        command.Parameters.AddWithValue("now", DateTime.UtcNow);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;

        return new TerraformAuthorizationCode(code, reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetDateTime(6));
    }

    private static string Hash(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

    private static void DeleteExpired(NpgsqlConnection connection)
    {
        using var command = new NpgsqlCommand(
            "DELETE FROM terraform_authorization_codes WHERE expires_at <= @now;", connection);
        command.Parameters.AddWithValue("now", DateTime.UtcNow);
        command.ExecuteNonQuery();
    }
}
