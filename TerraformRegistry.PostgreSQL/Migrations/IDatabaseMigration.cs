namespace TerraformRegistry.PostgreSQL.Migrations;

using Npgsql;

/// <summary>
/// Interface for database migrations
/// </summary>
public interface IDatabaseMigration
{
    /// <summary>
    /// Gets the migration version in SemVer format (e.g. 1.0.0)
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Gets a description of what this migration does
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Applies the migration to the database
    /// </summary>
    Task ApplyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction);
}