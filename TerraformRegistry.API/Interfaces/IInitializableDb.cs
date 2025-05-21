namespace TerraformRegistry.API.Interfaces;

/// <summary>
///     Interface for services that can be explicitly initialized at startup.
/// </summary>
public interface IInitializableDb
{
    Task InitializeDatabase();
}