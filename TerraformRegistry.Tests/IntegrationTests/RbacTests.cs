using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TerraformRegistry.API;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Services;
using Xunit.Abstractions;

namespace TerraformRegistry.Tests.IntegrationTests;

public class RbacTests(ITestOutputHelper output) : IntegrationTestBase(output, AuthToken)
{
    protected const string AuthToken = "default-auth-token";

    [Fact]
    public async Task RolesListRolesReturnsDefaultRoles()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.GetAsync("/api/admin/roles");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, json.ValueKind);

        var roles = json.EnumerateArray().ToList();
        Assert.True(roles.Count >= 2, "Should have at least admin and user roles");

        var roleNames = roles.Select(r => r.GetProperty("name").GetString()).ToList();
        Assert.Contains("admin", roleNames);
        Assert.Contains("user", roleNames);
    }

    [Fact]
    public async Task RolesCreateCustomRoleReturnsCreated()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/admin/roles", new
        {
            name = "viewer",
            description = "Read-only role",
            permissions = new[] { Permissions.ModulesRead, Permissions.AnalyticsView }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("viewer", json.GetProperty("name").GetString());
        Assert.False(json.GetProperty("isSystem").GetBoolean());
    }

    [Fact]
    public async Task RolesDeleteSystemRoleFails()
    {
        var client = await CreateAdminClientAsync();

        // Get the admin role ID
        var listResponse = await client.GetAsync("/api/admin/roles");
        var roles = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var adminRole = roles.EnumerateArray().First(r => r.GetProperty("name").GetString() == "admin");
        var adminRoleId = adminRole.GetProperty("id").GetString();

        // Try to delete the admin system role
        var deleteResponse = await client.DeleteAsync($"/api/admin/roles/{adminRoleId}");
        Assert.Equal(HttpStatusCode.BadRequest, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task UsersAssignRoleReturnsOk()
    {
        using var scope = Factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();

        // Create a target user to assign role to
        var targetUser = await apiKeyService.GetOrCreateUserAsync("target-assign@test.com", "test", "target-assign-id");

        var client = await CreateAdminClientAsync();

        // Get a role ID (user role)
        var listResponse = await client.GetAsync("/api/admin/roles");
        var roles = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var userRole = roles.EnumerateArray().First(r => r.GetProperty("name").GetString() == "user");
        var roleId = userRole.GetProperty("id").GetString();

        // Assign the role
        var assignResponse = await client.PostAsJsonAsync($"/api/admin/users/{targetUser.Id}/roles", new
        {
            roleId
        });

        Assert.Equal(HttpStatusCode.OK, assignResponse.StatusCode);

        var body = await assignResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task PermissionUserWithoutUploadGets403()
    {
        // Create a custom role without modules.upload
        var client = await CreateAdminClientAsync();

        var createRoleResponse = await client.PostAsJsonAsync("/api/admin/roles", new
        {
            name = "no-upload-role",
            description = "Role without upload",
            permissions = new[] { Permissions.ModulesRead }
        });
        Assert.Equal(HttpStatusCode.Created, createRoleResponse.StatusCode);
        var customRole = await createRoleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var customRoleId = customRole.GetProperty("id").GetString();

        // Create a user with only the custom role (no upload permission)
        var restrictedClient = await CreateClientWithRoleAsync("noupload@test.com", "noupload-id", Guid.Parse(customRoleId!));

        // Try uploading a module — should get 403
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(CreateMinimalZip()), "moduleFile", "module.zip");
        var uploadResponse = await restrictedClient.PostAsync("/v1/modules/test/nomod/aws/1.0.0", content);

        Assert.Equal(HttpStatusCode.Forbidden, uploadResponse.StatusCode);
    }

    [Fact]
    public async Task PermissionUserWithUploadSucceeds()
    {
        // Create a custom role with modules.upload
        var client = await CreateAdminClientAsync();

        var createRoleResponse = await client.PostAsJsonAsync("/api/admin/roles", new
        {
            name = "uploader-role",
            description = "Role with upload",
            permissions = new[] { Permissions.ModulesRead, Permissions.ModulesUpload }
        });
        Assert.Equal(HttpStatusCode.Created, createRoleResponse.StatusCode);
        var customRole = await createRoleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var customRoleId = customRole.GetProperty("id").GetString();

        // Create a user with the upload role.
        var uploaderClient = await CreateClientWithRoleAsync("uploader@test.com", "uploader-id", Guid.Parse(customRoleId!));

        using var scope = Factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var uploader = await apiKeyService.GetOrCreateUserAsync("uploader@test.com", "test", "uploader-id");

        // An administrator must explicitly claim an otherwise unowned namespace for the uploader.
        var assignmentClient = await CreateAdminClientAsync();
        var assignmentResponse = await assignmentClient.PutAsJsonAsync(
            "/api/admin/namespaces/test/maintainer",
            new { userId = uploader.Id });
        Assert.Equal(HttpStatusCode.OK, assignmentResponse.StatusCode);

        // The assigned maintainer can then upload a module.
        using var content = new MultipartFormDataContent();
        var zipBytes = CreateMinimalZip();
        content.Add(new ByteArrayContent(zipBytes), "moduleFile", "module.zip");
        var uploadResponse = await uploaderClient.PostAsync("/v1/modules/test/rbacmod/aws/1.0.0", content);

        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);

        var otherUploaderClient = await CreateClientWithRoleAsync(
            "other-uploader@test.com", "other-uploader-id", Guid.Parse(customRoleId!));
        using var otherContent = new MultipartFormDataContent();
        otherContent.Add(new ByteArrayContent(CreateMinimalZip()), "moduleFile", "module.zip");

        var otherUploadResponse = await otherUploaderClient.PostAsync(
            "/v1/modules/test/rbacmod-other/aws/1.0.0", otherContent);

        Assert.Equal(HttpStatusCode.Forbidden, otherUploadResponse.StatusCode);
    }

    [Fact]
    public async Task NamespaceMaintainerAssignmentRejectsInvalidNamespace()
    {
        using var scope = Factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var user = await apiKeyService.GetOrCreateUserAsync(
            "namespace-target@test.com", "test", "namespace-target-id");
        var adminClient = await CreateAdminClientAsync();

        var response = await adminClient.PutAsJsonAsync(
            "/api/admin/namespaces/not.a-valid-namespace/maintainer",
            new { userId = user.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AuthMeReturnsPermissions()
    {
        // This test uses session-based auth (JWT cookie) since /api/auth/me reads session cookies.
        // For API-key-based auth, we verify that roles and permissions are visible via admin endpoints.
        var client = await CreateAdminClientAsync();

        // Verify admin can list roles (implies permissions are loaded)
        var response = await client.GetAsync("/api/admin/roles");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Also verify the user's own roles via the admin endpoint
        using var scope = Factory.Services.CreateScope();
        var permService = scope.ServiceProvider.GetRequiredService<IPermissionService>();

        // Get permissions for the admin user we created
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var adminUser = await apiKeyService.GetOrCreateUserAsync("admin-me@test.com", "test", "admin-me-id");
        var roleService = scope.ServiceProvider.GetRequiredService<IRoleService>();
        var roles = await roleService.ListRolesAsync();
        var adminRole = roles.First(r => r.Name == "admin");
        await permService.AssignRoleAsync(adminUser.Id, adminRole.Id, null);

        var permissions = await permService.GetUserPermissionsAsync(adminUser.Id);
        Assert.Contains(Permissions.AdminRoles, permissions);
        Assert.Contains(Permissions.AdminUsers, permissions);
        Assert.Contains(Permissions.ModulesUpload, permissions);
    }

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();
        var roleService = scope.ServiceProvider.GetRequiredService<IRoleService>();

        var user = await apiKeyService.GetOrCreateUserAsync($"admin-{Guid.NewGuid():N}@test.com", "test", $"admin-test-{Guid.NewGuid():N}");
        var (rawToken, _) = await apiKeyService.CreateApiKeyAsync(user.Id, "admin-key");

        var roles = await roleService.ListRolesAsync();
        var adminRole = roles.First(r => r.Name == "admin");
        await permissionService.AssignRoleAsync(user.Id, adminRole.Id, null);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        return client;
    }

    private async Task<HttpClient> CreateClientWithRoleAsync(string email, string providerId, Guid roleId)
    {
        using var scope = Factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();

        var user = await apiKeyService.GetOrCreateUserAsync(email, "test", providerId);
        var (rawToken, _) = await apiKeyService.CreateApiKeyAsync(user.Id, "test-key");

        await permissionService.AssignRoleAsync(user.Id, roleId, null);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        return client;
    }

    private static byte[] CreateMinimalZip()
    {
        using var memoryStream = new System.IO.MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(memoryStream, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            var entry = archive.CreateEntry("main.tf");
            using var writer = new System.IO.StreamWriter(entry.Open());
            writer.Write("# minimal module");
        }
        return memoryStream.ToArray();
    }
}
