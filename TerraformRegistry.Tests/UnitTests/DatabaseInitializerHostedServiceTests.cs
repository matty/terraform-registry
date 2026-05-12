using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using TerraformRegistry.Services;

namespace TerraformRegistry.Tests.UnitTests;

public class DatabaseInitializerHostedServiceTests
{
    [Fact]
    public async Task StartAsyncSkipsBootstrapAdminAssignmentWhenEmailMatchesMultipleLegacyUsers()
    {
        var dbService = new Mock<IDatabaseService>();
        dbService.As<IInitializableDb>()
            .Setup(service => service.InitializeDatabase())
            .Returns(Task.CompletedTask);
        dbService.Setup(service => service.GetUsersByEmailCaseInsensitiveAsync("admin@example.com"))
            .ReturnsAsync(
                [
                    CreateUser("Admin@Example.com", "github", "gh-1"),
                    CreateUser("admin@example.com", "azuread", "aad-2")
                ]);

        var roleService = new Mock<IRoleService>();
        roleService.Setup(service => service.SeedDefaultRolesAsync()).Returns(Task.CompletedTask);
        roleService.Setup(service => service.ListRolesAsync())
            .ReturnsAsync([new Role { Id = Guid.NewGuid(), Name = RoleNames.Admin }]);

        var permissionService = new Mock<IPermissionService>();

        var hostedService = CreateHostedService(
            dbService.Object,
            dbService.As<IInitializableDb>().Object,
            roleService.Object,
            permissionService.Object,
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["AdminEmails"] = "admin@example.com" });

        await hostedService.StartAsync(CancellationToken.None);

        permissionService.Verify(
            service => service.AssignRoleAsync(It.IsAny<string>(), It.IsAny<Guid>(), "system-bootstrap"),
            Times.Never);
    }

    [Fact]
    public async Task StartAsyncBootstrapsAdminWhenEmailResolvesToSingleUser()
    {
        var user = CreateUser("admin@example.com", "github", "gh-1");
        var adminRole = new Role { Id = Guid.NewGuid(), Name = RoleNames.Admin };

        var dbService = new Mock<IDatabaseService>();
        dbService.As<IInitializableDb>()
            .Setup(service => service.InitializeDatabase())
            .Returns(Task.CompletedTask);
        dbService.Setup(service => service.GetUsersByEmailCaseInsensitiveAsync("admin@example.com"))
            .ReturnsAsync([user]);

        var roleService = new Mock<IRoleService>();
        roleService.Setup(service => service.SeedDefaultRolesAsync()).Returns(Task.CompletedTask);
        roleService.Setup(service => service.ListRolesAsync()).ReturnsAsync([adminRole]);

        var permissionService = new Mock<IPermissionService>();
        permissionService.Setup(service => service.AssignRoleAsync(user.Id, adminRole.Id, "system-bootstrap"))
            .ReturnsAsync(true);

        var hostedService = CreateHostedService(
            dbService.Object,
            dbService.As<IInitializableDb>().Object,
            roleService.Object,
            permissionService.Object,
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["AdminEmails"] = "admin@example.com" });

        await hostedService.StartAsync(CancellationToken.None);

        permissionService.Verify(
            service => service.AssignRoleAsync(user.Id, adminRole.Id, "system-bootstrap"),
            Times.Once);
    }

    private static DatabaseInitializerHostedService CreateHostedService(
        IDatabaseService dbService,
        IInitializableDb initializableDb,
        IRoleService roleService,
        IPermissionService permissionService,
        IDictionary<string, string?> configurationValues)
    {
        var services = new ServiceCollection();
        services.AddSingleton(dbService);
        services.AddSingleton(initializableDb);
        services.AddSingleton(roleService);
        services.AddSingleton(permissionService);
        services.AddSingleton<IConfiguration>(_ => new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues)
            .Build());

        return new DatabaseInitializerHostedService(
            services.BuildServiceProvider(),
            Options.Create(new DatabaseRetryOptions
            {
                MaxRetryAttempts = 1,
                InitialDelaySeconds = 0,
                MaxDelaySeconds = 0
            }),
            NullLogger<DatabaseInitializerHostedService>.Instance);
    }

    private static User CreateUser(string email, string provider, string providerId) =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            Email = email,
            Provider = provider,
            ProviderId = providerId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
}
