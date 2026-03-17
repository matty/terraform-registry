using System.Text.Json;
using Microsoft.Data.Sqlite;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services;

public class SqliteRoleService : IRoleService
{
    private readonly string _connectionString;

    public SqliteRoleService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IEnumerable<Role>> ListRolesAsync()
    {
        var roles = new List<Role>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, name, description, permissions, is_system, created_at, updated_at FROM roles ORDER BY is_system DESC, name";

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            roles.Add(MapRole(reader));
        }

        return roles;
    }

    public async Task<Role?> GetRoleAsync(Guid id)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, name, description, permissions, is_system, created_at, updated_at FROM roles WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id.ToString());

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return MapRole(reader);
    }

    public async Task<Role> CreateRoleAsync(string name, string? description, string[] permissions)
    {
        await using var connection = new SqliteConnection(_connectionString);
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

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO roles (id, name, description, permissions, is_system, created_at, updated_at)
                            VALUES ($id, $name, $description, $permissions, $isSystem, $createdAt, $updatedAt)";
        cmd.Parameters.AddWithValue("$id", role.Id.ToString());
        cmd.Parameters.AddWithValue("$name", role.Name);
        cmd.Parameters.AddWithValue("$description", (object?)role.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$permissions", JsonSerializer.Serialize(role.Permissions));
        cmd.Parameters.AddWithValue("$isSystem", role.IsSystem ? 1 : 0);
        cmd.Parameters.AddWithValue("$createdAt", role.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$updatedAt", role.UpdatedAt.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
        return role;
    }

    public async Task<Role?> UpdateRoleAsync(Guid id, string? name, string? description, string[]? permissions)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // Check if role exists and whether it's a system role
        await using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = "SELECT is_system FROM roles WHERE id = $id";
        checkCmd.Parameters.AddWithValue("$id", id.ToString());
        var result = await checkCmd.ExecuteScalarAsync();
        if (result == null) return null;

        var isSystem = Convert.ToInt32(result) == 1;

        var setClauses = new List<string> { "updated_at = $updatedAt" };
        var parameters = new List<SqliteParameter>
        {
            new("$id", id.ToString()),
            new("$updatedAt", DateTime.UtcNow.ToString("o"))
        };

        // If system role, reject name changes but allow permission updates
        if (name != null && !isSystem)
        {
            setClauses.Add("name = $name");
            parameters.Add(new SqliteParameter("$name", name));
        }

        if (description != null)
        {
            setClauses.Add("description = $description");
            parameters.Add(new SqliteParameter("$description", description));
        }

        if (permissions != null)
        {
            setClauses.Add("permissions = $permissions");
            parameters.Add(new SqliteParameter("$permissions", JsonSerializer.Serialize(permissions)));
        }

        var sql = $"UPDATE roles SET {string.Join(", ", setClauses)} WHERE id = $id";
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in parameters) cmd.Parameters.Add(p);
        await cmd.ExecuteNonQueryAsync();

        // Fetch updated record
        await using var fetchCmd = connection.CreateCommand();
        fetchCmd.CommandText = "SELECT id, name, description, permissions, is_system, created_at, updated_at FROM roles WHERE id = $id";
        fetchCmd.Parameters.AddWithValue("$id", id.ToString());

        await using var reader = await fetchCmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return MapRole(reader);
    }

    public async Task<bool> DeleteRoleAsync(Guid id)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM roles WHERE id = $id AND is_system = 0";
        cmd.Parameters.AddWithValue("$id", id.ToString());

        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public async Task SeedDefaultRolesAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var sql = @"INSERT OR REPLACE INTO roles (id, name, description, permissions, is_system)
                    VALUES ($id, $name, $description, $permissions, 1)";

        // Seed admin role
        await using var adminCmd = connection.CreateCommand();
        adminCmd.CommandText = sql;
        adminCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        adminCmd.Parameters.AddWithValue("$name", "admin");
        adminCmd.Parameters.AddWithValue("$description", "Full system administrator");
        adminCmd.Parameters.AddWithValue("$permissions", JsonSerializer.Serialize(Permissions.All));
        await adminCmd.ExecuteNonQueryAsync();

        // Seed user role
        await using var userCmd = connection.CreateCommand();
        userCmd.CommandText = sql;
        userCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        userCmd.Parameters.AddWithValue("$name", "user");
        userCmd.Parameters.AddWithValue("$description", "Default user role");
        userCmd.Parameters.AddWithValue("$permissions", JsonSerializer.Serialize(Permissions.DefaultUserPermissions));
        await userCmd.ExecuteNonQueryAsync();
    }

    private static Role MapRole(SqliteDataReader reader)
    {
        var permissionsJson = reader.GetString(3);
        var permissions = JsonSerializer.Deserialize<string[]>(permissionsJson) ?? [];

        return new Role
        {
            Id = Guid.Parse(reader.GetString(0)),
            Name = reader.GetString(1),
            Description = reader.IsDBNull(2) ? null : reader.GetString(2),
            Permissions = permissions,
            IsSystem = reader.GetInt32(4) == 1,
            CreatedAt = DateTime.Parse(reader.GetString(5)),
            UpdatedAt = DateTime.Parse(reader.GetString(6))
        };
    }
}
