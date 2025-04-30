using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Threading;
using Testcontainers.PostgreSql;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Builders;
using Xunit;
using Xunit.Abstractions;
using Xunit.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace TerraformRegistry.Tests.IntegrationTests;

// Base class for integration tests
public abstract class IntegrationTestBase : IAsyncLifetime
{
    protected PostgreSqlContainer _postgresContainer = null!;
    protected WebApplicationFactory<Program> _factory = null!;
    protected HttpClient _client = null!;
    protected readonly ITestOutputHelper _output;
    protected XunitLoggerProvider _loggerProvider = null!;
    private CancellationTokenSource _logMonitorCts = new();

    protected IntegrationTestBase(ITestOutputHelper output)
    {
        _output = output;
    }

    public virtual async Task InitializeAsync()
    {
        _output.WriteLine("Starting PostgreSQL test container...");

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
            _output.WriteLine($"PostgreSQL container started successfully. Connection string: {_postgresContainer.GetConnectionString()}");

            // Start monitoring logs in the background
            // _ = MonitorContainerLogsAsync();
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Failed to start PostgreSQL container: {ex}");
            throw;
        }

        _loggerProvider = new XunitLoggerProvider(_output, (category, level) => true);

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((context, config) =>
                {
                    var connStr = _postgresContainer.GetConnectionString();
                    config.AddInMemoryCollection([
                        new KeyValuePair<string, string?>("PostgreSQL:ConnectionString", connStr),
                        new KeyValuePair<string, string?>("DatabaseProvider", "postgres"),
                        new KeyValuePair<string, string?>("BaseUrl", "http://localhost:5000")
                    ]);
                });

                builder.ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddProvider(_loggerProvider);

                    logging.SetMinimumLevel(LogLevel.Information);
                    logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
                    logging.AddFilter("Testcontainers", LogLevel.Information);
                });
            });

        _client = _factory.CreateClient();
    }

    public virtual async Task DisposeAsync()
    {
        _logMonitorCts.Cancel();
        _logMonitorCts.Dispose();

        _client?.Dispose();
        _factory?.Dispose();

        if (_postgresContainer != null)
        {
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

                    if (!string.IsNullOrEmpty(stdout))
                    {
                        _output.WriteLine($"PostgreSQL Container Stdout: {stdout}");
                    }

                    if (!string.IsNullOrEmpty(stderr))
                    {
                        _output.WriteLine($"PostgreSQL Container Stderr: {stderr}");
                    }
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
    private readonly ITestOutputHelper _output;
    private readonly StringBuilder _lineBuffer = new();

    public OutputToTestConsoleStream(ITestOutputHelper output)
    {
        _output = output;
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => 0;
    public override long Position { get => 0; set { } }

    public override void Flush() { }

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
        string text = Encoding.UTF8.GetString(buffer, offset, count);

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
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

