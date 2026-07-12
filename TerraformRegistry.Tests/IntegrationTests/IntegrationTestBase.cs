using System.Net.Http.Headers;
using System.Text;
using DotNet.Testcontainers.Builders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using TerraformRegistry.Services;
using TerraformRegistry.Startup;
using Testcontainers.PostgreSql;
using Xunit.Abstractions;
using Xunit.Extensions.Logging;

namespace TerraformRegistry.Tests.IntegrationTests;

// Base class for integration tests
public abstract class IntegrationTestBase : IAsyncLifetime
{
    private readonly string _authToken;
    private CancellationTokenSource LogMonitorCts { get; } = new();
    protected ITestOutputHelper Output { get; }
    protected HttpClient Client { get; private set; } = null!;
    protected WebApplicationFactory<Program> Factory { get; private set; } = null!;
    protected PostgreSqlContainer PostgresContainer { get; private set; } = null!;
    private XunitLoggerProvider LoggerProvider { get; set; } = null!;

    protected IntegrationTestBase(ITestOutputHelper output, string authToken)
    {
        Output = output;
        _authToken = authToken;
    }

    public virtual async Task InitializeAsync()
    {
        Output.WriteLine("Starting PostgreSQL test container...");

        var randomSuffix = Path.GetRandomFileName().Replace(".", "", StringComparison.Ordinal);
        var moduleStoragePath = Path.Combine(Directory.GetCurrentDirectory(), $"modules/{randomSuffix}");
        var providerStoragePath = Path.Combine(Directory.GetCurrentDirectory(), $"providers/{randomSuffix}");
        if (!string.IsNullOrEmpty(moduleStoragePath) && Directory.Exists(moduleStoragePath))
        {
            Directory.Delete(moduleStoragePath, true);
            Output.WriteLine($"Cleared directory: {moduleStoragePath}");
        }
        if (Directory.Exists(providerStoragePath))
        {
            Directory.Delete(providerStoragePath, true);
            Output.WriteLine($"Cleared directory: {providerStoragePath}");
        }

        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    var connStr = PostgresContainer.GetConnectionString();
                    config.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["PostgreSQL:ConnectionString"] = connStr,
                        ["DatabaseProvider"] = "postgres",
                        ["StorageProvider"] = "local",
                        ["BaseUrl"] = "http://localhost:5000",
                        ["ModuleStoragePath"] = moduleStoragePath,
                        ["ModuleExtraction:Enabled"] = "false",
                        ["ProviderStoragePath"] = providerStoragePath,
                        ["PORT"] = "0",
                        ["AuthorizationToken"] = _authToken,
                        ["UserAdmission:Mode"] = "ConstrainedAutoProvision",
                        ["UserAdmission:AllowedDomains:0"] = "example.com",
                        ["Oidc:JwtSecretKey"] = "integration-test-jwt-secret-key-32-chars-minimum"
                    });
                });

                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<OidcOptions>();
                    services.AddSingleton(new OidcOptions
                    {
                        JwtSecretKey = "integration-test-jwt-secret-key-32-chars-minimum",
                        JwtExpiryHours = 24
                    });
                    services.RemoveAll<UserAdmissionOptions>();
                    services.AddSingleton(new UserAdmissionOptions
                    {
                        Mode = UserAdmissionMode.ConstrainedAutoProvision,
                        AllowedDomains = ["example.com"],
                        RequireVerifiedEmail = false
                    });
                });

                builder.ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddProvider(LoggerProvider);
                    logging.SetMinimumLevel(LogLevel.Information);
                    logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
                    logging.AddFilter("Testcontainers", LogLevel.Information);
                });

                builder.UseEnvironment("Test");
                ConfigureTestApp(builder);
            });

        var testOutputConsumer = Consume.RedirectStdoutAndStderrToStream(
            new OutputToTestConsoleStream(Output), new OutputToTestConsoleStream(Output));

        PostgresContainer = new PostgreSqlBuilder()
            .WithDatabase("testdb")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
            .WithOutputConsumer(testOutputConsumer)
            .Build();

        try
        {
            await PostgresContainer.StartAsync();
            Output.WriteLine(
                $"PostgreSQL container started successfully. Connection string: {PostgresContainer.GetConnectionString()}");

            // Optionally, start monitoring logs in the background
            // _ = MonitorContainerLogsAsync();
        }
        catch (Exception ex)
        {
            Output.WriteLine($"Failed to start PostgreSQL container: {ex}");
            throw;
        }

        LoggerProvider = new XunitLoggerProvider(Output, (_, _) => true);

        Client = Factory.CreateClient();
    }

    protected virtual void ConfigureTestApp(IWebHostBuilder builder)
    {
    }

    protected async Task<HttpClient> CreateClientWithPermissionsAsync(string email, string providerId,
        string[] permissions)
    {
        using var scope = Factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();
        var roleService = scope.ServiceProvider.GetRequiredService<IRoleService>();

        var user = await apiKeyService.GetOrCreateUserAsync(email, "test", providerId);
        var (rawToken, _) = await apiKeyService.CreateApiKeyAsync(user.Id, "test-key");
        var role = await roleService.CreateRoleAsync($"test-role-{Guid.NewGuid():N}", null, permissions);
        await permissionService.AssignRoleAsync(user.Id, role.Id, null);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        return client;
    }

    public virtual async Task DisposeAsync()
    {
        LogMonitorCts.Cancel();
        LogMonitorCts.Dispose();

        Client?.Dispose();
        Factory?.Dispose();

        if (PostgresContainer != null)
        {
            try
            {
                Output.WriteLine("Stopping PostgreSQL container...");
                await PostgresContainer.DisposeAsync();
                Output.WriteLine("PostgreSQL container stopped.");
            }
            catch (Exception ex)
            {
                Output.WriteLine($"Error stopping PostgreSQL container: {ex.Message}");
            }
        }

        LoggerProvider?.Dispose();
    }

    // Monitor container logs in the background
    private async Task MonitorContainerLogsAsync()
    {
        try
        {
            while (!LogMonitorCts.Token.IsCancellationRequested && PostgresContainer != null)
            {
                try
                {
                    // Get logs since the last minute
                    var since = DateTime.UtcNow.AddMinutes(-1);
                    var (stdout, stderr) = await PostgresContainer.GetLogsAsync(since);

                    if (!string.IsNullOrEmpty(stdout)) Output.WriteLine($"PostgreSQL Container Stdout: {stdout}");

                    if (!string.IsNullOrEmpty(stderr)) Output.WriteLine($"PostgreSQL Container Stderr: {stderr}");
                }
                catch (Exception ex)
                {
                    Output.WriteLine($"Error retrieving container logs: {ex.Message}");
                }

                // Wait before checking logs again
                await Task.Delay(5000, LogMonitorCts.Token);
            }
        }
        catch (OperationCanceledException ex)
        {
            Output.WriteLine($"Container log monitoring stopped: {ex.Message}");
        }
        catch (Exception ex) when (!LogMonitorCts.Token.IsCancellationRequested)
        {
            Output.WriteLine($"Error monitoring container logs: {ex.Message}");
        }
    }
}

// Custom stream class to redirect container output to test output
public class OutputToTestConsoleStream : Stream
{
    private readonly StringBuilder _lineBuffer = new();
    private readonly ITestOutputHelper _output;

    public OutputToTestConsoleStream(ITestOutputHelper output)
    {
        _output = output;
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => 0;

    public override long Position
    {
        get => 0;
        set { }
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        var text = Encoding.UTF8.GetString(buffer, offset, count);

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\n')
            {
                // End of line found, write to output
                try
                {
                    _output.WriteLine($"[Container] {_lineBuffer}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to write container output during test cleanup: {ex.Message}");
                }

                _lineBuffer.Clear();
            }
            else if (c != '\r')
            {
                // Append to buffer (skipping \r characters)
                _lineBuffer.Append(c);
            }
        }
    }
}
