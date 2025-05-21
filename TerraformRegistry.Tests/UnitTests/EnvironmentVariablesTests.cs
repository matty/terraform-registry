using Microsoft.Extensions.Configuration;

namespace TerraformRegistry.Tests.UnitTests;

public class EnvironmentVariablesTests
{
    // This test verifies that environment variables matching those used in Program.cs
    // (e.g., DatabaseProvider, StorageProvider, BaseUrl, ModuleStoragePath, AuthorizationToken)
    // are correctly picked up by the configuration builder when using the "TF_REG_" prefix.
    [Fact]
    public void ProgramEnvironmentVariables_AreAcceptedByConfiguration()
    {
        // Arrange
        var envVars = new[]
        {
            ("TF_REG_DatabaseProvider", "postgres"),
            ("TF_REG_StorageProvider", "azure"),
            ("TF_REG_BaseUrl", "https://example.com"),
            ("TF_REG_ModuleStoragePath", "modules"),
            ("TF_REG_AuthorizationToken", "super-secret-token")
        };
        foreach (var (key, value) in envVars)
            Environment.SetEnvironmentVariable(key, value);

        var config = new ConfigurationBuilder()
            .AddEnvironmentVariables("TF_REG_")
            .Build();

        // Act & Assert
        Assert.Equal("postgres", config["DatabaseProvider"]);
        Assert.Equal("azure", config["StorageProvider"]);
        Assert.Equal("https://example.com", config["BaseUrl"]);
        Assert.Equal("modules", config["ModuleStoragePath"]);
        Assert.Equal("super-secret-token", config["AuthorizationToken"]);
    }
}