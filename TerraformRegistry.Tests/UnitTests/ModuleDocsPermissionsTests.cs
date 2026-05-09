using TerraformRegistry.API;

namespace TerraformRegistry.Tests.UnitTests;

public class ModuleDocsPermissionsTests
{
    [Fact]
    public void PermissionsAll_IncludesModuleDocsPermissions()
    {
        Assert.Contains("module_docs.read", Permissions.All);
        Assert.Contains("module_docs.manage", Permissions.All);
        Assert.Contains("module_docs.configure", Permissions.All);
    }

    [Fact]
    public void DefaultUserPermissions_DoNotIncludeModuleDocsPermissions()
    {
        Assert.DoesNotContain("module_docs.read", Permissions.DefaultUserPermissions);
        Assert.DoesNotContain("module_docs.manage", Permissions.DefaultUserPermissions);
        Assert.DoesNotContain("module_docs.configure", Permissions.DefaultUserPermissions);
    }
}
