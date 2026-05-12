using Microsoft.Extensions.Logging.Abstractions;
using TerraformRegistry.Migrations;
using TerraformRegistry.Models;
using TerraformRegistry.Services;

namespace TerraformRegistry.Tests.UnitTests.Database;

public class SqliteModuleExtractionAdminQueryTests
{
    [Fact]
    public async Task ModuleExtractionAdminQueriesReturnSummaryListAndDetail()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"module-docs-admin-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";
        var service = new SqliteDatabaseService(
            connectionString,
            "https://registry.example.com",
            NullLogger<SqliteDatabaseService>.Instance,
            new DbUpMigrator(NullLogger<DbUpMigrator>.Instance));

        try
        {
            await service.InitializeDatabase();

            await service.AddModuleAsync(new ModuleStorage
            {
                Namespace = "acme",
                Name = "network",
                Provider = "aws",
                Version = "1.0.0",
                Description = "Network module",
                FilePath = "/tmp/network.zip",
                Dependencies = [],
                Metadata = new ModuleArtifactMetadata
                {
                    Extraction = new ModuleExtractionState { Status = "failed", Error = "tool missing" }
                }
            });

            var document = new ModuleExtractionDocument
            {
                Readme = new ModuleReadmeDocument { Path = "README.md", Title = "Network" },
                Inputs = [new ModuleInputDefinition { Name = "cidr", Required = true }],
                Outputs = [new ModuleOutputDefinition { Name = "vpc_id" }],
                Examples = [new ModuleExampleDefinition { Name = "basic", Path = "examples/basic" }]
            };

            await service.UpsertModuleExtractionAsync("acme", "network", "aws", "1.0.0", document);
            await service.UpdateModuleMetadataAsync("acme", "network", "aws", "1.0.0", metadata =>
            {
                metadata.Extraction = new ModuleExtractionState
                {
                    Status = "succeeded",
                    LastAttemptedAt = DateTime.UtcNow,
                    LastSucceededAt = DateTime.UtcNow
                };
                metadata.Documentation = new ModuleDocumentationSummary
                {
                    PrimaryReadmePath = "README.md",
                    InputCount = 1,
                    OutputCount = 1,
                    ExampleCount = 1
                };
            });

            var summary = await service.GetModuleExtractionAdminSummaryAsync();
            var page = await service.ListModuleExtractionsAdminAsync(new ModuleExtractionAdminQuery
            {
                Q = "network",
                Status = "succeeded",
                Limit = 10,
                Offset = 0
            });
            var detail = await service.GetModuleExtractionAdminDetailAsync("acme", "network", "aws", "1.0.0");

            Assert.Equal(1, summary.Succeeded);
            Assert.Equal(0, summary.Failed);
            Assert.Equal(1, page.Total);
            Assert.Equal("acme", page.Items.Single().Namespace);
            Assert.Equal("succeeded", page.Items.Single().Status);
            Assert.NotNull(detail);
            Assert.Equal("README.md", detail!.Document!.Readme!.Path);
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }
}
