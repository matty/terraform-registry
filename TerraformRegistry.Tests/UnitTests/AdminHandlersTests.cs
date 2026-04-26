using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Handlers;
using TerraformRegistry.Models;

namespace TerraformRegistry.Tests.UnitTests;

public class AdminHandlersTests
{
    [Fact]
    public async Task RemoveUserRole_AllowsRemoval_WhenAdminEmailMatchesMultipleLegacyUsers()
    {
        var roleId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid().ToString();

        var permissionService = new Mock<IPermissionService>();
        permissionService.Setup(service => service.GetUsersWithRoleAsync(roleId))
            .ReturnsAsync(["admin-1", "admin-2"]);
        permissionService.Setup(service => service.RemoveRoleAsync(targetUserId, roleId))
            .ReturnsAsync(true);

        var roleService = new Mock<IRoleService>();
        roleService.Setup(service => service.GetRoleAsync(roleId))
            .ReturnsAsync(new Role { Id = roleId, Name = RoleNames.Admin });

        var dbService = new Mock<IDatabaseService>();
        dbService.Setup(service => service.GetUsersByEmailCaseInsensitiveAsync("admin@example.com"))
            .ReturnsAsync(
                [
                    new User { Id = targetUserId, Email = "Admin@Example.com", Provider = "github", ProviderId = "gh-1" },
                    new User { Id = Guid.NewGuid().ToString(), Email = "admin@example.com", Provider = "azuread", ProviderId = "aad-2" }
                ]);

        var auditService = new Mock<IAuditService>();
        auditService.Setup(service => service.LogAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<object?>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        var context = CreateHttpContext(
            new Dictionary<string, string?> { ["AdminEmails"] = "admin@example.com" },
            dbService.Object);

        var result = await AdminHandlers.RemoveUserRole(targetUserId, roleId, permissionService.Object, roleService.Object, auditService.Object, context);

        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
        permissionService.Verify(service => service.RemoveRoleAsync(targetUserId, roleId), Times.Once);
    }

    private static DefaultHttpContext CreateHttpContext(
        IDictionary<string, string?> configurationValues,
        IDatabaseService dbService)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(_ => new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues)
            .Build());
        services.AddSingleton(dbService);

        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider()
        };
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "super-admin"),
                new Claim("permission", Permissions.AdminUsers)
            ],
            authenticationType: "TestAuth"));

        return context;
    }
}
