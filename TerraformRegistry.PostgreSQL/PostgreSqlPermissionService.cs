using Npgsql;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.PostgreSQL;

public class PostgreSqlPermissionService : IPermissionService
{
    private readonly string _connectionString;

    public PostgreSqlPermissionService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<string[]> GetUserPermissionsAsync(string userId)
    {
        var allPermissions = new List<string>();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = "SELECT r.permissions FROM user_roles ur JOIN roles r ON ur.role_id = r.id WHERE ur.user_id = @userId";
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@userId", userId);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var permissions = reader.GetFieldValue<string[]>(0);
            allPermissions.AddRange(permissions);
        }

        return allPermissions.Distinct().ToArray();
    }

    public async Task<IEnumerable<Role>> GetUserRolesAsync(string userId)
    {
        var roles = new List<Role>();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = "SELECT r.id, r.name, r.description, r.permissions, r.is_system, r.created_at, r.updated_at FROM user_roles ur JOIN roles r ON ur.role_id = r.id WHERE ur.user_id = @userId";
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@userId", userId);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            roles.Add(new Role
            {
                Id = reader.GetGuid(0),
                Name = reader.GetString(1),
                Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                Permissions = reader.GetFieldValue<string[]>(3),
                IsSystem = reader.GetBoolean(4),
                CreatedAt = reader.GetDateTime(5),
                UpdatedAt = reader.GetDateTime(6)
            });
        }

        return roles;
    }

    public async Task<bool> AssignRoleAsync(string userId, Guid roleId, string? assignedBy)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = @"INSERT INTO user_roles (user_id, role_id, assigned_at, assigned_by)
                    VALUES (@userId, @roleId, @assignedAt, @assignedBy)
                    ON CONFLICT DO NOTHING";
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@roleId", roleId);
        cmd.Parameters.AddWithValue("@assignedAt", DateTime.UtcNow);
        cmd.Parameters.AddWithValue("@assignedBy", (object?)assignedBy ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
        return true;
    }

    public async Task<bool> RemoveRoleAsync(string userId, Guid roleId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = "DELETE FROM user_roles WHERE user_id = @userId AND role_id = @roleId";
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@roleId", roleId);

        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public async Task<IEnumerable<string>> GetUsersWithRoleAsync(Guid roleId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = "SELECT user_id FROM user_roles WHERE role_id = @roleId";
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@roleId", roleId);

        var userIds = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            userIds.Add(reader.GetString(0));
        }
        return userIds;
    }

    public async Task EnsureDefaultRoleAsync(string userId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        // Check if user has any roles
        var countSql = "SELECT COUNT(*) FROM user_roles WHERE user_id = @userId";
        await using var countCmd = new NpgsqlCommand(countSql, connection);
        countCmd.Parameters.AddWithValue("@userId", userId);
        var count = (long)(await countCmd.ExecuteScalarAsync())!;

        if (count > 0) return;

        // Lookup the 'user' role by name
        var roleSql = "SELECT id FROM roles WHERE name = @name";
        await using var roleCmd = new NpgsqlCommand(roleSql, connection);
        roleCmd.Parameters.AddWithValue("@name", RoleNames.User);
        var roleIdObj = await roleCmd.ExecuteScalarAsync();

        if (roleIdObj == null) return;

        var roleId = (Guid)roleIdObj;

        // Assign the default role
        var assignSql = @"INSERT INTO user_roles (user_id, role_id, assigned_at)
                          VALUES (@userId, @roleId, @assignedAt)
                          ON CONFLICT DO NOTHING";
        await using var assignCmd = new NpgsqlCommand(assignSql, connection);
        assignCmd.Parameters.AddWithValue("@userId", userId);
        assignCmd.Parameters.AddWithValue("@roleId", roleId);
        assignCmd.Parameters.AddWithValue("@assignedAt", DateTime.UtcNow);

        await assignCmd.ExecuteNonQueryAsync();
    }
}
