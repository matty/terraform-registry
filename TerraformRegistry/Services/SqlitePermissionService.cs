using System.Text.Json;
using Microsoft.Data.Sqlite;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services;

public class SqlitePermissionService : IPermissionService
{
    private readonly string _connectionString;

    public SqlitePermissionService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<string[]> GetUserPermissionsAsync(string userId)
    {
        var allPermissions = new List<string>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT r.permissions FROM user_roles ur JOIN roles r ON ur.role_id = r.id WHERE ur.user_id = $userId";
        cmd.Parameters.AddWithValue("$userId", userId);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var permissionsJson = reader.GetString(0);
            var permissions = JsonSerializer.Deserialize<string[]>(permissionsJson) ?? [];
            allPermissions.AddRange(permissions);
        }

        return allPermissions.Distinct().ToArray();
    }

    public async Task<IEnumerable<Role>> GetUserRolesAsync(string userId)
    {
        var roles = new List<Role>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT r.id, r.name, r.description, r.permissions, r.is_system, r.created_at, r.updated_at FROM user_roles ur JOIN roles r ON ur.role_id = r.id WHERE ur.user_id = $userId";
        cmd.Parameters.AddWithValue("$userId", userId);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var permissionsJson = reader.GetString(3);
            var permissions = JsonSerializer.Deserialize<string[]>(permissionsJson) ?? [];

            roles.Add(new Role
            {
                Id = Guid.Parse(reader.GetString(0)),
                Name = reader.GetString(1),
                Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                Permissions = permissions,
                IsSystem = reader.GetInt32(4) == 1,
                CreatedAt = DateTime.Parse(reader.GetString(5)),
                UpdatedAt = DateTime.Parse(reader.GetString(6))
            });
        }

        return roles;
    }

    public async Task<bool> AssignRoleAsync(string userId, Guid roleId, string? assignedBy)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT OR IGNORE INTO user_roles (user_id, role_id, assigned_at, assigned_by)
                            VALUES ($userId, $roleId, $assignedAt, $assignedBy)";
        cmd.Parameters.AddWithValue("$userId", userId);
        cmd.Parameters.AddWithValue("$roleId", roleId.ToString());
        cmd.Parameters.AddWithValue("$assignedAt", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("$assignedBy", (object?)assignedBy ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
        return true;
    }

    public async Task<bool> RemoveRoleAsync(string userId, Guid roleId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM user_roles WHERE user_id = $userId AND role_id = $roleId";
        cmd.Parameters.AddWithValue("$userId", userId);
        cmd.Parameters.AddWithValue("$roleId", roleId.ToString());

        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public async Task EnsureDefaultRoleAsync(string userId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // Check if user has any roles
        await using var countCmd = connection.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM user_roles WHERE user_id = $userId";
        countCmd.Parameters.AddWithValue("$userId", userId);
        var count = (long)(await countCmd.ExecuteScalarAsync())!;

        if (count > 0) return;

        // Lookup the 'user' role by name
        await using var roleCmd = connection.CreateCommand();
        roleCmd.CommandText = "SELECT id FROM roles WHERE name = $name";
        roleCmd.Parameters.AddWithValue("$name", RoleNames.User);
        var roleIdObj = await roleCmd.ExecuteScalarAsync();

        if (roleIdObj == null) return;

        var roleId = roleIdObj.ToString()!;

        // Assign the default role
        await using var assignCmd = connection.CreateCommand();
        assignCmd.CommandText = @"INSERT OR IGNORE INTO user_roles (user_id, role_id, assigned_at)
                                  VALUES ($userId, $roleId, $assignedAt)";
        assignCmd.Parameters.AddWithValue("$userId", userId);
        assignCmd.Parameters.AddWithValue("$roleId", roleId);
        assignCmd.Parameters.AddWithValue("$assignedAt", DateTime.UtcNow.ToString("o"));

        await assignCmd.ExecuteNonQueryAsync();
    }
}
