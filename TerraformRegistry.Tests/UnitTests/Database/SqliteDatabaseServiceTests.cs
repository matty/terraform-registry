using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Migrations;
using TerraformRegistry.Models;
using TerraformRegistry.Services;
using Xunit;

namespace TerraformRegistry.Tests.UnitTests.Database;

public class SqliteDatabaseServiceTests : IAsyncLifetime
{
    private string _dbPath = null!;
    private string _connectionString = null!;

    public Task InitializeAsync()
    {
        // Create a unique temporary file-based SQLite database for each test class instance
        var tempDir = Path.Combine(Path.GetTempPath(), "TerraformRegistryTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        _dbPath = Path.Combine(tempDir, "test.db");
        _connectionString = $"Data Source={_dbPath}";
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_dbPath))
            {
                var dir = Path.GetDirectoryName(_dbPath)!;
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
        catch
        {
            // ignore cleanup issues
        }

        return Task.CompletedTask;
    }

    private static SqliteDatabaseService CreateService(string connStr, string baseUrl = "http://localhost")
    {
        var logger = new Mock<ILogger<SqliteDatabaseService>>();
        var migratorLogger = new Mock<ILogger<DbUpMigrator>>();
        var dbUpMigrator = new DbUpMigrator(migratorLogger.Object);
        return new SqliteDatabaseService(connStr, baseUrl, logger.Object, dbUpMigrator);
    }

    private static ModuleStorage MakeModule(
        string ns = "hashicorp",
        string name = "vpc",
        string provider = "aws",
        string version = "1.0.0",
        string? desc = "VPC module",
        string? filePath = "/modules/vpc/1.0.0.zip",
        DateTime? publishedAt = null,
        params string[] deps)
    {
        return new ModuleStorage
        {
            Namespace = ns,
            Name = name,
            Provider = provider,
            Version = version,
            Description = desc ?? string.Empty,
            FilePath = filePath ?? string.Empty,
            PublishedAt = publishedAt ?? DateTime.UtcNow,
            Dependencies = deps.ToList()
        };
    }

    [Fact]
    public async Task InitializeDatabase_CreatesSchemaAndAllowsInsertAndFetch()
    {
        var svc = CreateService(_connectionString);
        await (svc as IInitializableDb).InitializeDatabase();

        var mod = MakeModule();
        var added = await svc.AddModuleAsync(mod);
        Assert.True(added);

        var fetched = await svc.GetModuleAsync(mod.Namespace, mod.Name, mod.Provider, mod.Version);
        Assert.NotNull(fetched);
        Assert.Equal(mod.Namespace, fetched!.Namespace);
        Assert.Equal(mod.Name, fetched.Name);
        Assert.Equal(mod.Provider, fetched.Provider);
        Assert.Equal(mod.Version, fetched.Version);
        Assert.Equal("http://localhost/v1/modules/hashicorp/vpc/aws/1.0.0/download", fetched.DownloadUrl);
        Assert.Contains("1.0.0", fetched.Versions);
    }

    [Fact]
    public async Task AddModule_ReturnsFalseOnDuplicate_AndKeepsOriginalMetadata()
    {
        var svc = CreateService(_connectionString);
        await (svc as IInitializableDb).InitializeDatabase();

        var mod = MakeModule(desc: "desc1", filePath: "/path/one.zip");
        Assert.True(await svc.AddModuleAsync(mod));

        var modUpdated = MakeModule(desc: "desc2", filePath: "/path/two.zip");
        Assert.False(await svc.AddModuleAsync(modUpdated));

        var storage = await svc.GetModuleStorageAsync(mod.Namespace, mod.Name, mod.Provider, mod.Version);
        Assert.NotNull(storage);
        Assert.Equal("desc1", storage!.Description);
        Assert.Equal("/path/one.zip", storage.FilePath);
    }

    [Fact]
    public async Task ListModules_ReturnsLatestVersionPerTuple_AndSupportsFilters()
    {
        var svc = CreateService(_connectionString);
        await (svc as IInitializableDb).InitializeDatabase();

        // Insert multiple versions and different providers
        await svc.AddModuleAsync(MakeModule(version: "1.0.0", desc: "alpha vpc"));
        await svc.AddModuleAsync(MakeModule(version: "1.1.0", desc: "beta vpc"));
        await svc.AddModuleAsync(MakeModule(provider: "azurerm", version: "0.1.0", desc: "azure vpc"));

        // No filter should return one entry per (ns,name,provider) with latest version
        var all = await svc.ListModulesAsync(new ModuleSearchRequest { Limit = 50, Offset = 0 });
        Assert.Equal(2, all.Modules.Count);
        var awsEntry = all.Modules.First(m => m.Provider == "aws");
        Assert.Equal("1.1.0", awsEntry.Version);
        Assert.Contains("1.0.0", awsEntry.Versions);
        Assert.Contains("1.1.0", awsEntry.Versions);

        // Filter by provider
        var awsOnly = await svc.ListModulesAsync(new ModuleSearchRequest { Provider = "aws", Limit = 50 });
        Assert.Single(awsOnly.Modules);

        // Filter by namespace and search query (name/description)
        var search = await svc.ListModulesAsync(new ModuleSearchRequest { Namespace = "hashicorp", Q = "azure" });
        Assert.Single(search.Modules);
        Assert.Equal("azurerm", search.Modules[0].Provider);
    }

    [Fact]
    public async Task ListModules_UsesSemVerPrecedenceForLatestVersion()
    {
        var svc = CreateService(_connectionString);
        await (svc as IInitializableDb).InitializeDatabase();

        await svc.AddModuleAsync(MakeModule(version: "1.9.0"));
        await svc.AddModuleAsync(MakeModule(version: "1.10.0"));

        var all = await svc.ListModulesAsync(new ModuleSearchRequest { Limit = 50, Offset = 0 });

        var awsEntry = Assert.Single(all.Modules);
        Assert.Equal("1.10.0", awsEntry.Version);
    }

    [Fact]
    public async Task GetModuleVersions_ReturnsSemVerDescendingVersionList()
    {
        var svc = CreateService(_connectionString);
        await (svc as IInitializableDb).InitializeDatabase();

        await svc.AddModuleAsync(MakeModule(version: "1.0.0"));
        await svc.AddModuleAsync(MakeModule(version: "1.10.0"));
        await svc.AddModuleAsync(MakeModule(version: "1.2.0"));
        await svc.AddModuleAsync(MakeModule(version: "1.1.1"));

        var versions = await svc.GetModuleVersionsAsync("hashicorp", "vpc", "aws");
        var list = versions.Modules.Single().Versions.Select(v => v.Version).ToList();

        Assert.Equal(new[] { "1.10.0", "1.2.0", "1.1.1", "1.0.0" }, list);
    }

    [Fact]
    public async Task GetModuleStorage_ReturnsDependenciesAndFields()
    {
        var svc = CreateService(_connectionString);
        await (svc as IInitializableDb).InitializeDatabase();

        var published = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        await svc.AddModuleAsync(MakeModule(version: "2.0.0", publishedAt: published, deps: ["a", "b"]));

        var storage = await svc.GetModuleStorageAsync("hashicorp", "vpc", "aws", "2.0.0");
        Assert.NotNull(storage);
        Assert.Equal(["a", "b"], storage!.Dependencies);
        Assert.Equal(published, storage.PublishedAt);
    }

    [Fact]
    public async Task UpsertModuleExtraction_PersistsDocumentAndUpdatesMetadata()
    {
        var svc = CreateService(_connectionString);
        await (svc as IInitializableDb).InitializeDatabase();

        var module = MakeModule(version: "6.0.0");
        await svc.AddModuleAsync(module);

        var document = new ModuleExtractionDocument
        {
            Readme = new ModuleReadmeDocument
            {
                Path = "README.md",
                Title = "Network Module",
                Markdown = "# Network Module"
            },
            Inputs = [new ModuleInputDefinition { Name = "name", Required = true, Type = "string" }],
            Outputs = [new ModuleOutputDefinition { Name = "vpc_id" }],
            Examples = [new ModuleExampleDefinition { Name = "basic", Path = "examples/basic" }]
        };

        await svc.UpsertModuleExtractionAsync("hashicorp", "vpc", "aws", "6.0.0", document);
        await svc.UpdateModuleMetadataAsync("hashicorp", "vpc", "aws", "6.0.0", metadata =>
        {
            metadata.Documentation = new ModuleDocumentationSummary
            {
                PrimaryReadmePath = "README.md",
                InputCount = 1,
                OutputCount = 1,
                ExampleCount = 1
            };
            metadata.Extraction = new ModuleExtractionState { Status = "succeeded" };
        });

        var stored = await svc.GetModuleExtractionAsync("hashicorp", "vpc", "aws", "6.0.0");
        var moduleDetail = await svc.GetModuleAsync("hashicorp", "vpc", "aws", "6.0.0");

        Assert.NotNull(stored);
        Assert.Equal("README.md", stored!.Readme!.Path);
        Assert.NotNull(moduleDetail);
        Assert.Equal("succeeded", moduleDetail!.Metadata!.Extraction.Status);
        Assert.Equal(1, moduleDetail.Metadata.Documentation!.ExampleCount);
    }

    [Fact]
    public async Task UpsertModuleLlmContext_PersistsStoredArtifact()
    {
        var svc = CreateService(_connectionString);
        await (svc as IInitializableDb).InitializeDatabase();

        var module = MakeModule(version: "7.0.0");
        await svc.AddModuleAsync(module);

        var document = new ModuleLlmContextDocument
        {
            Module = new ModuleLlmModuleReference
            {
                Namespace = "hashicorp",
                Name = "vpc",
                Provider = "aws",
                Version = "7.0.0"
            },
            Summary = new ModuleLlmContextSummary
            {
                OneLine = "Creates AWS VPC networking primitives."
            }
        };

        await svc.UpsertModuleLlmContextAsync("hashicorp", "vpc", "aws", "7.0.0", document);
        var stored = await svc.GetModuleLlmContextAsync("hashicorp", "vpc", "aws", "7.0.0");

        Assert.NotNull(stored);
        Assert.Equal("Creates AWS VPC networking primitives.", stored!.Summary.OneLine);
    }

    [Fact]
    public async Task RemoveModule_DeletesRow()
    {
        var svc = CreateService(_connectionString);
        await (svc as IInitializableDb).InitializeDatabase();

        var mod = MakeModule(version: "3.0.0");
        await svc.AddModuleAsync(mod);

        var removed = await svc.RemoveModuleAsync(mod);
        Assert.True(removed);

        var fetched = await svc.GetModuleAsync(mod.Namespace, mod.Name, mod.Provider, mod.Version);
        Assert.Null(fetched);
    }

    [Fact]
    public async Task RemoveModuleExact_OnlyDeletesMatchingRow()
    {
        var svc = CreateService(_connectionString);
        await (svc as IInitializableDb).InitializeDatabase();

        var published = new DateTime(2024, 4, 1, 12, 34, 56, DateTimeKind.Utc);
        var mod = MakeModule(version: "3.1.0", desc: "exact-desc", publishedAt: published, deps: ["a", "b"]);
        await svc.AddModuleAsync(mod);

        var wrongPublishedAt = MakeModule(
            version: "3.1.0",
            desc: "exact-desc",
            publishedAt: published.AddSeconds(1),
            deps: ["a", "b"]);

        Assert.False(await svc.RemoveModuleExactAsync(wrongPublishedAt));
        Assert.NotNull(await svc.GetModuleStorageAsync(mod.Namespace, mod.Name, mod.Provider, mod.Version));

        Assert.True(await svc.RemoveModuleExactAsync(mod));
        Assert.Null(await svc.GetModuleStorageAsync(mod.Namespace, mod.Name, mod.Provider, mod.Version));
    }

    [Fact]
    public async Task RemoveModuleExact_DeletesSnapshotFetchedFromDatabase()
    {
        var svc = CreateService(_connectionString);
        await (svc as IInitializableDb).InitializeDatabase();

        var mod = MakeModule(
            version: "3.2.0",
            publishedAt: new DateTime(2024, 4, 1, 12, 34, 56, DateTimeKind.Utc));
        await svc.AddModuleAsync(mod);

        var fetched = await svc.GetModuleStorageAsync(mod.Namespace, mod.Name, mod.Provider, mod.Version);

        Assert.NotNull(fetched);
        Assert.True(await svc.RemoveModuleExactAsync(fetched!));
        Assert.Null(await svc.GetModuleStorageAsync(mod.Namespace, mod.Name, mod.Provider, mod.Version));
    }

    [Fact]
    public async Task ReplaceModuleExact_UpdatesSnapshotFetchedFromDatabase()
    {
        var svc = CreateService(_connectionString);
        await (svc as IInitializableDb).InitializeDatabase();

        var mod = MakeModule(
            version: "3.3.0",
            desc: "old-desc",
            filePath: "/modules/vpc/3.3.0-old.zip",
            publishedAt: new DateTime(2024, 4, 1, 12, 34, 56, DateTimeKind.Utc),
            deps: ["a"]);
        await svc.AddModuleAsync(mod);

        var fetched = await svc.GetModuleStorageAsync(mod.Namespace, mod.Name, mod.Provider, mod.Version);
        Assert.NotNull(fetched);

        var replacement = MakeModule(
            version: "3.3.0",
            desc: "new-desc",
            filePath: "/modules/vpc/3.3.0-new.zip",
            publishedAt: new DateTime(2024, 4, 1, 12, 35, 56, DateTimeKind.Utc),
            deps: ["a", "b"]);

        Assert.True(await svc.ReplaceModuleExactAsync(fetched!, replacement));

        var updated = await svc.GetModuleStorageAsync(mod.Namespace, mod.Name, mod.Provider, mod.Version);
        Assert.NotNull(updated);
        Assert.Equal("new-desc", updated!.Description);
        Assert.Equal("/modules/vpc/3.3.0-new.zip", updated.FilePath);
        Assert.Equal(new DateTime(2024, 4, 1, 12, 35, 56, DateTimeKind.Utc), updated.PublishedAt);
        Assert.Equal(["a", "b"], updated.Dependencies);
    }

    [Fact]
    public async Task RemoveDeletedModule_DeletesOnlySoftDeletedRow()
    {
        var svc = CreateService(_connectionString);
        await (svc as IInitializableDb).InitializeDatabase();

        var mod = MakeModule(version: "3.4.0");
        await svc.AddModuleAsync(mod);
        Assert.True(await svc.SoftDeleteModuleAsync(mod.Namespace, mod.Name, mod.Provider, mod.Version));

        Assert.True(await svc.RemoveDeletedModuleAsync(mod.Namespace, mod.Name, mod.Provider, mod.Version));
        Assert.Null(await svc.GetModuleStorageIncludingDeletedAsync(mod.Namespace, mod.Name, mod.Provider, mod.Version));
    }

    [Fact]
    public async Task AddDeletedModule_AddsSoftDeletedRow()
    {
        var svc = CreateService(_connectionString);
        await (svc as IInitializableDb).InitializeDatabase();

        var mod = MakeModule(version: "3.5.0");

        Assert.True(await svc.AddDeletedModuleAsync(mod));
        Assert.Null(await svc.GetModuleStorageAsync(mod.Namespace, mod.Name, mod.Provider, mod.Version));
        Assert.NotNull(await svc.GetModuleStorageIncludingDeletedAsync(mod.Namespace, mod.Name, mod.Provider, mod.Version));
    }

    [Fact]
    public async Task RemoveDeletedModule_ReturnsFalseForActiveRow()
    {
        var svc = CreateService(_connectionString);
        await (svc as IInitializableDb).InitializeDatabase();

        var mod = MakeModule(version: "3.6.0");
        await svc.AddModuleAsync(mod);

        Assert.False(await svc.RemoveDeletedModuleAsync(mod.Namespace, mod.Name, mod.Provider, mod.Version));
        Assert.NotNull(await svc.GetModuleStorageAsync(mod.Namespace, mod.Name, mod.Provider, mod.Version));
    }

    [Fact]
    public async Task GetModule_ReturnsNullAfterSoftDelete()
    {
        var svc = CreateService(_connectionString);
        await (svc as IInitializableDb).InitializeDatabase();

        var mod = MakeModule(version: "4.0.0");
        await svc.AddModuleAsync(mod);
        Assert.True(await svc.SoftDeleteModuleAsync(mod.Namespace, mod.Name, mod.Provider, mod.Version));

        var fetched = await svc.GetModuleAsync(mod.Namespace, mod.Name, mod.Provider, mod.Version);

        Assert.Null(fetched);
    }

    [Fact]
    public async Task GetUserByEmail_IsCaseInsensitiveForLegacyMixedCaseRows()
    {
        var svc = CreateService(_connectionString);
        await (svc as IInitializableDb).InitializeDatabase();

        var legacyUser = new User
        {
            Id = Guid.NewGuid().ToString(),
            Email = "Admin@Example.com",
            Provider = "github",
            ProviderId = "gh-legacy",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await svc.AddUserAsync(legacyUser);

        var fetched = await svc.GetUserByEmailAsync("admin@example.com");

        Assert.NotNull(fetched);
        Assert.Equal(legacyUser.Id, fetched!.Id);
        Assert.Equal("Admin@Example.com", fetched.Email);
    }

    [Fact]
    public async Task GetUsersByEmailCaseInsensitive_ReturnsAllLegacyCaseVariants()
    {
        var svc = CreateService(_connectionString);
        await (svc as IInitializableDb).InitializeDatabase();

        var firstUser = new User
        {
            Id = Guid.NewGuid().ToString(),
            Email = "Admin@Example.com",
            Provider = "github",
            ProviderId = "gh-legacy-1",
            CreatedAt = DateTime.UtcNow.AddMinutes(-2),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-2)
        };

        var secondUser = new User
        {
            Id = Guid.NewGuid().ToString(),
            Email = "admin@example.com",
            Provider = "azuread",
            ProviderId = "aad-legacy-2",
            CreatedAt = DateTime.UtcNow.AddMinutes(-1),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-1)
        };

        await svc.AddUserAsync(firstUser);
        await svc.AddUserAsync(secondUser);

        var fetched = await svc.GetUsersByEmailCaseInsensitiveAsync("ADMIN@example.com");

        Assert.Equal(2, fetched.Count);
        Assert.Contains(fetched, user => user.Id == firstUser.Id);
        Assert.Contains(fetched, user => user.Id == secondUser.Id);
    }
}
