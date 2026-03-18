using Npgsql;
using NpgsqlTypes;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.PostgreSQL;

public class PostgreSqlRoleService : IRoleService
{
    private readonly string _connectionString;

    public PostgreSqlRoleService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IEnumerable<Role>> ListRolesAsync()
    {
        var roles = new List<Role>();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = "SELECT id, name, description, permissions, is_system, created_at, updated_at FROM roles ORDER BY is_system DESC, name";
        await using var cmd = new NpgsqlCommand(sql, connection);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            roles.Add(MapRole(reader));
        }

        return roles;
    }

    public async Task<Role?> GetRoleAsync(Guid id)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = "SELECT id, name, description, permissions, is_system, created_at, updated_at FROM roles WHERE id = @id";
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", id);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return MapRole(reader);
    }

    public async Task<Role> CreateRoleAsync(string name, string? description, string[] permissions)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var role = new Role
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            Permissions = permissions,
            IsSystem = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var sql = @"INSERT INTO roles (id, name, description, permissions, is_system, created_at, updated_at)
                    VALUES (@id, @name, @description, @permissions, @isSystem, @createdAt, @updatedAt)";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", role.Id);
        cmd.Parameters.AddWithValue("@name", role.Name);
        cmd.Parameters.AddWithValue("@description", (object?)role.Description ?? DBNull.Value);
        cmd.Parameters.Add(new NpgsqlParameter("@permissions", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = role.Permissions });
        cmd.Parameters.AddWithValue("@isSystem", role.IsSystem);
        cmd.Parameters.AddWithValue("@createdAt", role.CreatedAt);
        cmd.Parameters.AddWithValue("@updatedAt", role.UpdatedAt);

        await cmd.ExecuteNonQueryAsync();
        return role;
    }

    public async Task<Role?> UpdateRoleAsync(Guid id, string? name, string? description, string[]? permissions)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        // Check if role exists and whether it's a system role
        var checkSql = "SELECT is_system FROM roles WHERE id = @id";
        await using var checkCmd = new NpgsqlCommand(checkSql, connection);
        checkCmd.Parameters.AddWithValue("@id", id);
        var result = await checkCmd.ExecuteScalarAsync();
        if (result == null) return null;

        var isSystem = (bool)result;

        var setClauses = new List<string> { "updated_at = @updatedAt" };
        var parameters = new List<NpgsqlParameter>
        {
            new("@id", id),
            new("@updatedAt", DateTime.UtcNow)
        };

        // If system role, reject name changes but allow permission updates
        if (name != null && !isSystem)
        {
            setClauses.Add("name = @name");
            parameters.Add(new NpgsqlParameter("@name", name));
        }

        if (description != null)
        {
            setClauses.Add("description = @description");
            parameters.Add(new NpgsqlParameter("@description", description));
        }

        if (permissions != null)
        {
            setClauses.Add("permissions = @permissions");
            parameters.Add(new NpgsqlParameter("@permissions", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = permissions });
        }

        var sql = $"UPDATE roles SET {string.Join(", ", setClauses)} WHERE id = @id RETURNING id, name, description, permissions, is_system, created_at, updated_at";
        await using var cmd = new NpgsqlCommand(sql, connection);
        foreach (var p in parameters) cmd.Parameters.Add(p);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return MapRole(reader);
    }

    public async Task<bool> DeleteRoleAsync(Guid id)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = "DELETE FROM roles WHERE id = @id AND is_system = false";
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", id);

        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public async Task SeedDefaultRolesAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = @"INSERT INTO roles (id, name, description, permissions, is_system)
                    VALUES (@id, @name, @description, @permissions, true)
                    ON CONFLICT (name) DO UPDATE SET permissions = EXCLUDED.permissions, updated_at = CURRENT_TIMESTAMP";

        // Seed admin role
        await using var adminCmd = new NpgsqlCommand(sql, connection);
        adminCmd.Parameters.AddWithValue("@id", Guid.NewGuid());
        adminCmd.Parameters.AddWithValue("@name", RoleNames.Admin);
        adminCmd.Parameters.AddWithValue("@description", "Full system administrator");
        adminCmd.Parameters.Add(new NpgsqlParameter("@permissions", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = Permissions.All });
        await adminCmd.ExecuteNonQueryAsync();

        // Seed user role
        await using var userCmd = new NpgsqlCommand(sql, connection);
        userCmd.Parameters.AddWithValue("@id", Guid.NewGuid());
        userCmd.Parameters.AddWithValue("@name", RoleNames.User);
        userCmd.Parameters.AddWithValue("@description", "Default user role");
        userCmd.Parameters.Add(new NpgsqlParameter("@permissions", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = Permissions.DefaultUserPermissions });
        await userCmd.ExecuteNonQueryAsync();
    }

    private static Role MapRole(NpgsqlDataReader reader)
    {
        return new Role
        {
            Id = reader.GetGuid(0),
            Name = reader.GetString(1),
            Description = reader.IsDBNull(2) ? null : reader.GetString(2),
            Permissions = reader.GetFieldValue<string[]>(3),
            IsSystem = reader.GetBoolean(4),
            CreatedAt = reader.GetDateTime(5),
            UpdatedAt = reader.GetDateTime(6)
        };
    }
}
