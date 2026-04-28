using System.Net.Http.Headers;
using System.Text;
using DotNet.Testcontainers.Builders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;
using TerraformRegistry.Services;
using Testcontainers.PostgreSql;
using Xunit.Abstractions;
using Xunit.Extensions.Logging;

namespace TerraformRegistry.Tests.IntegrationTests;

// Base class for integration tests
public abstract class IntegrationTestBase : IAsyncLifetime
{
    private readonly string _authToken;
    private readonly CancellationTokenSource _logMonitorCts = new();
    protected readonly ITestOutputHelper _output;
    protected HttpClient _client = null!;
    protected WebApplicationFactory<Program> _factory = null!;
    protected XunitLoggerProvider _loggerProvider = null!;
    protected PostgreSqlContainer _postgresContainer = null!;

    protected IntegrationTestBase(ITestOutputHelper output, string authToken)
    {
        _output = output;
        _authToken = authToken;
    }

    public virtual async Task InitializeAsync()
    {
        _output.WriteLine("Starting PostgreSQL test container...");

        var randomSuffix = Path.GetRandomFileName().Replace(".", "");
        var moduleStoragePath = Path.Combine(Directory.GetCurrentDirectory(), $"modules/{randomSuffix}");
        if (!string.IsNullOrEmpty(moduleStoragePath) && Directory.Exists(moduleStoragePath))
        {
            Directory.Delete(moduleStoragePath, true);
            _output.WriteLine($"Cleared directory: {moduleStoragePath}");
        }

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    var connStr = _postgresContainer.GetConnectionString();
                    config.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["PostgreSQL:ConnectionString"] = connStr,
                        ["DatabaseProvider"] = "postgres",
                        ["StorageProvider"] = "local",
                        ["BaseUrl"] = "http://localhost:5000",
                        ["ModuleStoragePath"] = moduleStoragePath,
                        ["ModuleExtraction:Enabled"] = "false",
                        ["AuthorizationToken"] = _authToken,
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
                });

                builder.ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddProvider(_loggerProvider);
                    logging.SetMinimumLevel(LogLevel.Information);
                    logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
                    logging.AddFilter("Testcontainers", LogLevel.Information);
                });

                builder.UseEnvironment("Test");
                ConfigureTestApp(builder);
            });

        var testOutputConsumer = Consume.RedirectStdoutAndStderrToStream(
            new OutputToTestConsoleStream(_output), new OutputToTestConsoleStream(_output));

        _postgresContainer = new PostgreSqlBuilder()
            .WithDatabase("testdb")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
            .WithOutputConsumer(testOutputConsumer)
            .Build();

        try
        {
            await _postgresContainer.StartAsync();
            _output.WriteLine(
                $"PostgreSQL container started successfully. Connection string: {_postgresContainer.GetConnectionString()}");

            // Optionally, start monitoring logs in the background
            // _ = MonitorContainerLogsAsync();
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Failed to start PostgreSQL container: {ex}");
            throw;
        }

        _loggerProvider = new XunitLoggerProvider(_output, (_, _) => true);

        _client = _factory.CreateClient();
    }

    protected virtual void ConfigureTestApp(IWebHostBuilder builder)
    {
    }

    protected async Task<HttpClient> CreateClientWithPermissionsAsync(string email, string providerId,
        string[] permissions)
    {
        using var scope = _factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();
        var roleService = scope.ServiceProvider.GetRequiredService<IRoleService>();

        var user = await apiKeyService.GetOrCreateUserAsync(email, "test", providerId);
        var (rawToken, _) = await apiKeyService.CreateApiKeyAsync(user.Id, "test-key");
        var role = await roleService.CreateRoleAsync($"test-role-{Guid.NewGuid():N}", null, permissions);
        await permissionService.AssignRoleAsync(user.Id, role.Id, null);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        return client;
    }

    public virtual async Task DisposeAsync()
    {
        _logMonitorCts.Cancel();
        _logMonitorCts.Dispose();

        _client?.Dispose();
        _factory?.Dispose();

        if (_postgresContainer != null)
            try
            {
                _output.WriteLine("Stopping PostgreSQL container...");
                await _postgresContainer.DisposeAsync();
                _output.WriteLine("PostgreSQL container stopped.");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error stopping PostgreSQL container: {ex.Message}");
            }

        _loggerProvider?.Dispose();
    }

    // Monitor container logs in the background
    private async Task MonitorContainerLogsAsync()
    {
        try
        {
            while (!_logMonitorCts.Token.IsCancellationRequested && _postgresContainer != null)
            {
                try
                {
                    // Get logs since the last minute
                    var since = DateTime.UtcNow.AddMinutes(-1);
                    var (stdout, stderr) = await _postgresContainer.GetLogsAsync(since);

                    if (!string.IsNullOrEmpty(stdout)) _output.WriteLine($"PostgreSQL Container Stdout: {stdout}");

                    if (!string.IsNullOrEmpty(stderr)) _output.WriteLine($"PostgreSQL Container Stderr: {stderr}");
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Error retrieving container logs: {ex.Message}");
                }

                // Wait before checking logs again
                await Task.Delay(5000, _logMonitorCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation token is triggered
        }
        catch (Exception ex) when (!_logMonitorCts.Token.IsCancellationRequested)
        {
            _output.WriteLine($"Error monitoring container logs: {ex.Message}");
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
                catch (Exception)
                {
                    // Ignore write errors during test cleanup
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
