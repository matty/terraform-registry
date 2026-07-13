using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.API.Logging;
using TerraformRegistry.AzureBlob;
using TerraformRegistry.Middleware;
using TerraformRegistry.Migrations;
using TerraformRegistry.Models;
using TerraformRegistry.PostgreSQL;
using TerraformRegistry.PostgreSQL.Repositories;
using TerraformRegistry.S3;
using TerraformRegistry.Services;
using TerraformRegistry.Services.Mirror;
using TerraformRegistry.Services.ModuleExtraction;
using TerraformRegistry.Services.Publishing;
using TerraformRegistry.Services.Sqlite;

namespace TerraformRegistry.Startup;

internal static class ServiceRegistrationExtensions
{
    public static IServiceCollection AddTerraformRegistryServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddRegistryRateLimiting(configuration);
        services.AddSingleton<ArtifactDownloadTokenService>();
        services.Configure<DatabaseRetryOptions>(configuration.GetSection("DatabaseRetry"));
        services.Configure<WebhookSecurityOptions>(configuration.GetSection("WebhookSecurity"));
        services.AddOptions<DurableOutboxOptions>()
            .Bind(configuration.GetSection("DurableOutbox"))
            .Validate(options =>
            {
                try { options.Validate(); return true; }
                catch (InvalidOperationException) { return false; }
            }, "Durable outbox worker limits must be greater than zero.")
            .ValidateOnStart();
        services.AddOptions<ModuleExtractionOptions>()
            .Bind(configuration.GetSection("ModuleExtraction"))
            .Validate(options =>
            {
                try
                {
                    options.Validate();
                    return true;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }, "Module extraction limits must all be greater than zero.")
            .ValidateOnStart();
        var providerUploadOptions = new ProviderUploadOptions();
        configuration.GetSection(ProviderUploadOptions.SectionName).Bind(providerUploadOptions);
        providerUploadOptions.Validate();
        services.AddSingleton(providerUploadOptions);
        services.AddOptions<MirrorOptions>()
            .Bind(configuration.GetSection("Mirror"))
            .Validate(options =>
            {
                try
                {
                    MirrorConfigurationValidator.Validate(options);
                    return true;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }, "Mirror configuration must map allowed provider hostnames to valid HTTPS upstreams and use valid runtime limits.")
            .ValidateOnStart();
        services.AddSingleton<IWebhookHostResolver, DnsWebhookHostResolver>();
        services.AddSingleton<IWebhookStreamConnector, SocketWebhookStreamConnector>();
        services.AddSingleton<WebhookPinnedConnectionHelper>();
        services.AddSingleton<IS3ClientFactory, S3ClientFactory>();

        services.AddSingleton<DbUpMigrator>();
        services.AddSingleton<IStartupReadiness, StartupReadiness>();
        services.AddSingleton<IInitializableDb>(provider =>
        {
            var db = provider.GetRequiredService<IDatabaseService>();
            return db as IInitializableDb ??
                   throw new InvalidOperationException("Database service does not implement IInitializableDb");
        });

        services.AddDatabaseServices();
        services.AddSingleton<INamespaceMaintainerStore>(provider =>
        {
            var config = provider.GetRequiredService<IConfiguration>();
            var databaseProvider = config["DatabaseProvider"]?.ToLowerInvariant() ?? "sqlite";
            return databaseProvider switch
            {
                "postgres" => new PostgreSqlNamespaceMaintainerStore(
                    config["PostgreSQL:ConnectionString"] ?? throw new InvalidOperationException(
                        "PostgreSQL connection string is missing for namespace maintainers.")),
                "sqlite" => new SqliteNamespaceMaintainerStore(
                    config["Sqlite:ConnectionString"] ?? "Data Source=terraform.db"),
                _ => throw new InvalidOperationException($"Invalid database provider: '{databaseProvider}'")
            };
        });
        services.AddSingleton<NamespaceAuthorizationService>();
        services.AddModuleStorageServices();
        services.AddProviderRegistryServices();

        services.AddHostedService<DatabaseInitializerHostedService>();
        services.AddHostedService<StorageInitializationHostedService>();
        services.AddHostedService<StorageReconciliationHostedService>();
        services.AddHttpClient();
        services.AddHttpClient("TerraformRegistryMirrorDiscovery", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("terraform-registry-mirror");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false
            });
        services.AddHttpClient("TerraformRegistryMirror", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(120);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("terraform-registry-mirror");
            })
            .ConfigurePrimaryHttpMessageHandler(servicesProvider => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                MaxConnectionsPerServer = 8,
                ConnectCallback = servicesProvider.GetRequiredService<MirrorPinnedConnectionHelper>().ConnectAsync
            });
        services.AddHttpClient("WebhookDelivery", c => c.Timeout = TimeSpan.FromSeconds(5))
            .ConfigurePrimaryHttpMessageHandler(servicesProvider => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                ConnectCallback = servicesProvider.GetRequiredService<WebhookPinnedConnectionHelper>().ConnectAsync
            });

        services.AddControllers();
        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "CustomBearer";
                options.DefaultChallengeScheme = "CustomBearer";
            })
            .AddScheme<AuthenticationSchemeOptions, CustomBearerHandler>("CustomBearer", options => { });

        var oidcOptions = new OidcOptions();
        configuration.GetSection("Oidc").Bind(oidcOptions);
        services.AddSingleton(oidcOptions);
        var userAdmissionOptions = new UserAdmissionOptions();
        configuration.GetSection(UserAdmissionOptions.SectionName).Bind(userAdmissionOptions);
        userAdmissionOptions.Validate();
        services.AddSingleton(userAdmissionOptions);
        var apiKeySecurityOptions = new ApiKeySecurityOptions();
        configuration.GetSection(ApiKeySecurityOptions.SectionName).Bind(apiKeySecurityOptions);
        apiKeySecurityOptions.Validate();
        services.AddSingleton(apiKeySecurityOptions);
        services.AddSingleton<ApiKeyVerificationGate>();
        services.AddSingleton<JwtService>();
        services.AddSingleton<OAuthService>();
        var terraformLoginOptions = new TerraformLoginOptions();
        configuration.GetSection("TerraformLogin").Bind(terraformLoginOptions);
        services.AddSingleton(terraformLoginOptions);
        services.AddSingleton<ITerraformAuthorizationCodeStore>(provider =>
        {
            var config = provider.GetRequiredService<IConfiguration>();
            var databaseProvider = config["DatabaseProvider"]?.ToLowerInvariant() ?? "sqlite";
            return databaseProvider switch
            {
                "postgres" => new PostgreSqlTerraformAuthorizationCodeStore(
                    config["PostgreSQL:ConnectionString"] ?? throw new InvalidOperationException(
                        "PostgreSQL connection string is missing for Terraform authorization codes."),
                    provider.GetRequiredService<TerraformLoginOptions>()),
                "sqlite" => new SqliteTerraformAuthorizationCodeStore(
                    config["Sqlite:ConnectionString"] ?? "Data Source=terraform.db",
                    provider.GetRequiredService<TerraformLoginOptions>()),
                _ => throw new InvalidOperationException($"Invalid database provider: '{databaseProvider}'")
            };
        });
        services.AddScoped<IApiKeyService, ApiKeyService>();

        services.AddAnalyticsService();
        services.AddDurableOutboxServices();
        services.AddHostedService<DurableOutboxHostedService>();
        services.AddWebhookServices();
        services.AddVcsServices();
        services.AddMirrorServices();
        services.AddModuleExtractionServices();
        services.AddAuthorizationServices();

        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });

        return services;
    }

    private static IServiceCollection AddDatabaseServices(this IServiceCollection services)
    {
        services.AddSingleton<IDatabaseService>(provider =>
        {
            var config = provider.GetRequiredService<IConfiguration>();
            var loggerDb = provider.GetRequiredService<ILogger<PostgreSqlDatabaseService>>();
            var dbUpMigrator = provider.GetRequiredService<DbUpMigrator>();
            var databaseProvider = config["DatabaseProvider"]?.ToLowerInvariant() ?? "sqlite";
            var baseUrl = config["BaseUrl"] ?? "http://localhost:5131";

            if (string.IsNullOrEmpty(baseUrl))
                throw new InvalidOperationException("BaseUrl is missing or empty. Please check your configuration.");

            return databaseProvider switch
            {
                "postgres" => new PostgreSqlDatabaseService(
                    config["PostgreSQL:ConnectionString"]
                    ?? throw new InvalidOperationException(
                        "PostgreSQL connection string is missing or empty. Please check your configuration."),
                    baseUrl,
                    loggerDb,
                    dbUpMigrator),
                "sqlite" => new SqliteDatabaseService(
                    config["Sqlite:ConnectionString"] ?? "Data Source=terraform.db",
                    baseUrl,
                    provider.GetRequiredService<ILogger<SqliteDatabaseService>>(),
                    dbUpMigrator),
                _ => throw new InvalidOperationException(
                    $"Invalid database provider specified: '{databaseProvider}'. Check configuration.")
            };
        });

        services.AddSingleton<IRuntimeSettingsService>(provider =>
        {
            var config = provider.GetRequiredService<IConfiguration>();
            var databaseProvider = config["DatabaseProvider"]?.ToLowerInvariant() ?? "sqlite";
            return databaseProvider switch
            {
                "postgres" => new PostgreSqlRuntimeSettingsService(
                    config["PostgreSQL:ConnectionString"]
                    ?? throw new InvalidOperationException(
                        "PostgreSQL connection string is missing for runtime settings service.")),
                "sqlite" => new SqliteRuntimeSettingsService(
                    config["Sqlite:ConnectionString"] ?? "Data Source=terraform.db"),
                _ => throw new InvalidOperationException($"Invalid database provider: '{databaseProvider}'")
            };
        });

        return services;
    }

    private static IServiceCollection AddModuleStorageServices(this IServiceCollection services)
    {
        services.AddSingleton<IModuleService>(provider =>
        {
            var config = provider.GetRequiredService<IConfiguration>();
            var db = provider.GetRequiredService<IDatabaseService>();
            var logger = provider.GetRequiredService<ILogger<LocalModuleService>>();
            var storageProvider = config["StorageProvider"]?.ToLowerInvariant() ?? "local";
            return storageProvider switch
            {
                "azure" => new AzureBlobModuleService(
                    config,
                    db,
                    provider.GetRequiredService<ILogger<AzureBlobModuleService>>()),
                "s3" => new S3ModuleService(
                    config,
                    db,
                    provider.GetRequiredService<ILogger<S3ModuleService>>(),
                    null,
                    provider.GetRequiredService<IS3ClientFactory>()),
                "local" => CreateLocalModuleService(config, db, logger, provider),
                _ => throw new InvalidOperationException(
                    $"Invalid storage provider specified: '{storageProvider}'. Check configuration.")
            };
        });

        return services;
    }

    private static LocalModuleService CreateLocalModuleService(
        IConfiguration config,
        IDatabaseService db,
        ILogger<LocalModuleService> logger,
        IServiceProvider provider)
    {
        var storagePath = config["ModuleStoragePath"];
        if (string.IsNullOrEmpty(storagePath))
        {
            RegistryLog.Error(logger,
                "ModuleStoragePath is missing or empty. Please check your configuration. Application cannot start.");
            throw new InvalidOperationException(
                "ModuleStoragePath is missing or empty. Please check your configuration.");
        }

        return new LocalModuleService(config, db, logger, provider.GetRequiredService<ArtifactDownloadTokenService>());
    }

    private static IServiceCollection AddProviderRegistryServices(this IServiceCollection services)
    {
        services.AddSingleton<IProviderArtifactStorage>(provider =>
        {
            var config = provider.GetRequiredService<IConfiguration>();
            var storageProvider = config["StorageProvider"]?.ToLowerInvariant() ?? "local";
            var expiryMinutes = int.TryParse(config["ProviderArtifactUrlExpiryMinutes"], out var parsed) ? parsed : 10;

            return storageProvider switch
            {
                "azure" => new AzureBlobProviderArtifactStorage(
                    config,
                    provider.GetRequiredService<ILogger<AzureBlobProviderArtifactStorage>>()),
                "s3" => new S3ProviderArtifactStorage(
                    config,
                    provider.GetRequiredService<ILogger<S3ProviderArtifactStorage>>(),
                    null,
                    provider.GetRequiredService<IS3ClientFactory>()),
                "local" => new LocalProviderArtifactStorage(
                    config["ProviderStoragePath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "providers"),
                    TimeSpan.FromMinutes(expiryMinutes),
                    provider.GetRequiredService<ILogger<LocalProviderArtifactStorage>>(),
                    provider.GetRequiredService<ArtifactDownloadTokenService>()),
                _ => throw new InvalidOperationException(
                    $"Provider artifact storage is not implemented for StorageProvider '{storageProvider}'.")
            };
        });

        services.AddSingleton<IProviderRegistryService, ProviderRegistryService>();
        services.AddSingleton<IProviderPackageValidator, ProviderPackageValidator>();
        services.AddSingleton<IProviderRepository>(provider =>
        {
            var config = provider.GetRequiredService<IConfiguration>();
            var databaseProvider = config["DatabaseProvider"]?.ToLowerInvariant() ?? "sqlite";
            return databaseProvider switch
            {
                "postgres" => new PostgreSqlProviderRepository(
                    config["PostgreSQL:ConnectionString"] ??
                    throw new InvalidOperationException("PostgreSQL connection string is missing.")),
                "sqlite" => new SqliteProviderRepository(
                    config["Sqlite:ConnectionString"] ?? "Data Source=terraform.db"),
                _ => throw new InvalidOperationException($"Invalid database provider: '{databaseProvider}'")
            };
        });

        return services;
    }

    private static IServiceCollection AddAnalyticsService(this IServiceCollection services)
    {
        services.AddSingleton<IAnalyticsService>(provider =>
        {
            var config = provider.GetRequiredService<IConfiguration>();
            var databaseProvider = config["DatabaseProvider"]?.ToLowerInvariant() ?? "sqlite";
            return databaseProvider switch
            {
                "postgres" => new PostgreSqlAnalyticsService(
                    config["PostgreSQL:ConnectionString"]
                    ?? throw new InvalidOperationException("PostgreSQL connection string is missing for analytics service.")),
                "sqlite" => new SqliteAnalyticsService(config["Sqlite:ConnectionString"] ?? "Data Source=terraform.db"),
                _ => throw new InvalidOperationException($"Invalid database provider: '{databaseProvider}'")
            };
        });

        return services;
    }

    private static IServiceCollection AddDurableOutboxServices(this IServiceCollection services)
    {
        services.AddSingleton<IOutboxEventRepository>(provider =>
        {
            var config = provider.GetRequiredService<IConfiguration>();
            var databaseProvider = config["DatabaseProvider"]?.ToLowerInvariant() ?? "sqlite";
            return databaseProvider switch
            {
                "postgres" => new PostgreSqlOutboxEventRepository(config["PostgreSQL:ConnectionString"]
                    ?? throw new InvalidOperationException("PostgreSQL connection string is missing for durable outbox.")),
                "sqlite" => new SqliteOutboxEventRepository(config["Sqlite:ConnectionString"] ?? "Data Source=terraform.db"),
                _ => throw new InvalidOperationException($"Invalid database provider: '{databaseProvider}'")
            };
        });
        services.AddSingleton<IOutboxDeliveryHandler, AuditOutboxDeliveryHandler>();
        services.AddSingleton<IOutboxDeliveryHandler, WebhookOutboxDeliveryHandler>();

        return services;
    }

    private static IServiceCollection AddWebhookServices(this IServiceCollection services)
    {
        services.AddSingleton<IWebhookService>(provider =>
        {
            var config = provider.GetRequiredService<IConfiguration>();
            var databaseProvider = config["DatabaseProvider"]?.ToLowerInvariant() ?? "sqlite";
            return databaseProvider switch
            {
                "postgres" => new PostgreSqlWebhookService(
                    config["PostgreSQL:ConnectionString"]
                    ?? throw new InvalidOperationException("PostgreSQL connection string is missing for webhook service.")),
                "sqlite" => new SqliteWebhookService(config["Sqlite:ConnectionString"] ?? "Data Source=terraform.db"),
                _ => throw new InvalidOperationException($"Invalid database provider: '{databaseProvider}'")
            };
        });
        services.AddSingleton<WebhookUrlValidator>();
        services.AddSingleton<WebhookDispatcher>();

        return services;
    }

    private static IServiceCollection AddVcsServices(this IServiceCollection services)
    {
        services.AddSingleton<IVcsSourceService>(provider =>
        {
            var config = provider.GetRequiredService<IConfiguration>();
            var databaseProvider = config["DatabaseProvider"]?.ToLowerInvariant() ?? "sqlite";
            return databaseProvider switch
            {
                "postgres" => new PostgreSqlVcsSourceService(
                    config["PostgreSQL:ConnectionString"]
                    ?? throw new InvalidOperationException("PostgreSQL connection string is missing for VCS source service.")),
                "sqlite" => new SqliteVcsSourceService(config["Sqlite:ConnectionString"] ?? "Data Source=terraform.db"),
                _ => throw new InvalidOperationException($"Invalid database provider: '{databaseProvider}'")
            };
        });

        services.AddSingleton<IVcsConnectionService>(provider =>
        {
            var config = provider.GetRequiredService<IConfiguration>();
            var databaseProvider = config["DatabaseProvider"]?.ToLowerInvariant() ?? "sqlite";
            return databaseProvider switch
            {
                "postgres" => new PostgreSqlVcsConnectionService(
                    config["PostgreSQL:ConnectionString"]
                    ?? throw new InvalidOperationException("PostgreSQL connection string is missing for VCS connection service.")),
                "sqlite" => new SqliteVcsConnectionService(config["Sqlite:ConnectionString"] ?? "Data Source=terraform.db"),
                _ => throw new InvalidOperationException($"Invalid database provider: '{databaseProvider}'")
            };
        });

        services.AddSingleton<GitHubVcsService>();
        services.AddSingleton<IGitHubVcsService>(provider => provider.GetRequiredService<GitHubVcsService>());
        services.AddHttpClient("GitHubVcs", c => c.Timeout = TimeSpan.FromSeconds(60));

        return services;
    }

    private static IServiceCollection AddModuleExtractionServices(this IServiceCollection services)
    {
        services.AddSingleton<IArchiveWorkspaceFactory, ArchiveWorkspaceFactory>();
        services.AddSingleton<IArchiveIngestionValidator>(provider => new ArchiveIngestionValidator(
            provider.GetRequiredService<IArchiveWorkspaceFactory>(),
            provider.GetRequiredService<IOptions<ModuleExtractionOptions>>().Value));
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<ReadmeDiscoveryService>();
        services.AddSingleton<ExampleDiscoveryService>();
        services.AddSingleton<SubmoduleDiscoveryService>();
        services.AddSingleton<ITerraformModuleInspector, TerraformConfigInspectRunner>();
        services.AddSingleton<IModuleLlmContextGenerator, ModuleLlmContextGenerator>();
        services.AddSingleton<IModuleExtractionConfigService, ModuleExtractionConfigService>();
        services.AddSingleton<IModuleExtractionService>(provider => new ModuleExtractionService(
            provider.GetRequiredService<IModuleService>(),
            provider.GetRequiredService<IDatabaseService>(),
            provider.GetRequiredService<IArchiveWorkspaceFactory>(),
            provider.GetRequiredService<ITerraformModuleInspector>(),
            provider.GetRequiredService<IModuleLlmContextGenerator>(),
            provider.GetRequiredService<IModuleExtractionConfigService>(),
            provider.GetRequiredService<ILogger<ModuleExtractionService>>(),
            provider.GetRequiredService<IOptions<ModuleExtractionOptions>>().Value));
        services.AddHostedService<ModuleExtractionHostedService>();
        services.AddSingleton<IModulePublishCoordinator, ModulePublishCoordinator>();

        return services;
    }

    private static IServiceCollection AddMirrorServices(this IServiceCollection services)
    {
        services.AddSingleton<IMirrorConfigService, MirrorConfigService>();
        services.AddSingleton<MirrorDownloadAdmission>();
        services.AddSingleton<MirrorCacheUsage>();
        services.AddSingleton<MirrorCacheBudgetService>();
        services.AddSingleton<IMirrorPolicyService, MirrorPolicyService>();
        services.AddSingleton<MirrorPinnedConnectionHelper>();
        services.AddSingleton<MirrorHttpClient>();
        services.AddSingleton<IMirrorLeaseService, MirrorLeaseService>();
        services.AddSingleton<MirrorPackageUrlSigner>();
        services.AddSingleton<IProviderMirrorService, ProviderMirrorService>();
        services.AddSingleton<IModuleMirrorService, ModuleMirrorService>();
        services.AddSingleton<IProviderMirrorRepository>(provider =>
        {
            var config = provider.GetRequiredService<IConfiguration>();
            var databaseProvider = config["DatabaseProvider"]?.ToLowerInvariant() ?? "sqlite";
            return databaseProvider switch
            {
                "postgres" => new PostgreSqlProviderMirrorRepository(
                    config["PostgreSQL:ConnectionString"]
                    ?? throw new InvalidOperationException("PostgreSQL connection string is missing for provider mirror repository.")),
                "sqlite" => new SqliteProviderMirrorRepository(
                    config["Sqlite:ConnectionString"] ?? "Data Source=terraform.db"),
                _ => throw new InvalidOperationException($"Invalid database provider: '{databaseProvider}'")
            };
        });
        services.AddSingleton<IModuleMirrorRepository>(provider =>
        {
            var config = provider.GetRequiredService<IConfiguration>();
            var databaseProvider = config["DatabaseProvider"]?.ToLowerInvariant() ?? "sqlite";
            return databaseProvider switch
            {
                "postgres" => new PostgreSqlModuleMirrorRepository(
                    config["PostgreSQL:ConnectionString"]
                    ?? throw new InvalidOperationException("PostgreSQL connection string is missing for module mirror repository.")),
                "sqlite" => new SqliteModuleMirrorRepository(
                    config["Sqlite:ConnectionString"] ?? "Data Source=terraform.db"),
                _ => throw new InvalidOperationException($"Invalid database provider: '{databaseProvider}'")
            };
        });
        services.AddSingleton<IMirrorLeaseRepository>(provider =>
        {
            var config = provider.GetRequiredService<IConfiguration>();
            var databaseProvider = config["DatabaseProvider"]?.ToLowerInvariant() ?? "sqlite";
            return databaseProvider switch
            {
                "postgres" => new PostgreSqlMirrorLeaseRepository(
                    config["PostgreSQL:ConnectionString"]
                    ?? throw new InvalidOperationException("PostgreSQL connection string is missing for mirror lease repository.")),
                "sqlite" => new SqliteMirrorLeaseRepository(
                    config["Sqlite:ConnectionString"] ?? "Data Source=terraform.db"),
                _ => throw new InvalidOperationException($"Invalid database provider: '{databaseProvider}'")
            };
        });

        return services;
    }

    private static IServiceCollection AddAuthorizationServices(this IServiceCollection services)
    {
        services.AddSingleton<IRoleService>(provider =>
        {
            var config = provider.GetRequiredService<IConfiguration>();
            var databaseProvider = config["DatabaseProvider"]?.ToLowerInvariant() ?? "sqlite";
            return databaseProvider switch
            {
                "postgres" => new PostgreSqlRoleService(
                    config["PostgreSQL:ConnectionString"]
                    ?? throw new InvalidOperationException("PostgreSQL connection string is missing for role service.")),
                "sqlite" => new SqliteRoleService(config["Sqlite:ConnectionString"] ?? "Data Source=terraform.db"),
                _ => throw new InvalidOperationException($"Invalid database provider: '{databaseProvider}'")
            };
        });

        services.AddSingleton<IPermissionService>(provider =>
        {
            var config = provider.GetRequiredService<IConfiguration>();
            var databaseProvider = config["DatabaseProvider"]?.ToLowerInvariant() ?? "sqlite";
            return databaseProvider switch
            {
                "postgres" => new PostgreSqlPermissionService(
                    config["PostgreSQL:ConnectionString"]
                    ?? throw new InvalidOperationException("PostgreSQL connection string is missing for permission service.")),
                "sqlite" => new SqlitePermissionService(config["Sqlite:ConnectionString"] ?? "Data Source=terraform.db"),
                _ => throw new InvalidOperationException($"Invalid database provider: '{databaseProvider}'")
            };
        });

        services.AddSingleton<IAuditService>(provider =>
        {
            var config = provider.GetRequiredService<IConfiguration>();
            var databaseProvider = config["DatabaseProvider"]?.ToLowerInvariant() ?? "sqlite";
            return databaseProvider switch
            {
                "postgres" => new PostgreSqlAuditService(
                    config["PostgreSQL:ConnectionString"]
                    ?? throw new InvalidOperationException("PostgreSQL connection string is missing for audit service."),
                    provider.GetRequiredService<ILogger<PostgreSqlAuditService>>()),
                "sqlite" => new SqliteAuditService(
                    config["Sqlite:ConnectionString"] ?? "Data Source=terraform.db",
                    provider.GetRequiredService<ILogger<SqliteAuditService>>()),
                _ => throw new InvalidOperationException($"Invalid database provider: '{databaseProvider}'")
            };
        });

        return services;
    }
}
