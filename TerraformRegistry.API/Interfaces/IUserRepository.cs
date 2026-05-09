using TerraformRegistry.Models;

namespace TerraformRegistry.API.Interfaces;

/// <summary>
///     Stores registry user records.
/// </summary>
public interface IUserRepository
{
    Task<IReadOnlyList<User>> GetUsersByEmailCaseInsensitiveAsync(string email);

    Task<User?> GetUserByEmailAsync(string email);

    Task<User?> GetUserByIdAsync(string id);

    Task AddUserAsync(User user);

    Task UpdateUserAsync(User user);

    Task DeleteUserAsync(string userId);

    /// <summary>
    ///     Lists all users in the system.
    /// </summary>
    Task<IEnumerable<User>> ListAllUsersAsync();
}
