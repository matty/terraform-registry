using System.Globalization;
using DbUp.Engine.Output;
using Microsoft.Extensions.Logging;

namespace TerraformRegistry.Migrations;

/// <summary>
///     Adapts DbUp's IUpgradeLog to Microsoft.Extensions.Logging.ILogger.
/// </summary>
public class DbUpLogger : IUpgradeLog
{
    private readonly ILogger _logger;

    public DbUpLogger(ILogger logger)
    {
        _logger = logger;
    }

    public void LogTrace(string format, params object[] args)
    {
        if (_logger.IsEnabled(LogLevel.Trace))
        {
            var message = FormatMessage(format, args);
            MigrationLog.Trace(_logger, message);
        }
    }

    public void LogDebug(string format, params object[] args)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            var message = FormatMessage(format, args);
            MigrationLog.Debug(_logger, message);
        }
    }

    public void LogInformation(string format, params object[] args)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            var message = FormatMessage(format, args);
            MigrationLog.Information(_logger, message);
        }
    }

    public void LogWarning(string format, params object[] args)
    {
        if (_logger.IsEnabled(LogLevel.Warning))
        {
            var message = FormatMessage(format, args);
            MigrationLog.Warning(_logger, message);
        }
    }

    public void LogError(string format, params object[] args)
    {
        if (_logger.IsEnabled(LogLevel.Error))
        {
            var message = FormatMessage(format, args);
            MigrationLog.Error(_logger, message);
        }
    }

    public void LogError(Exception ex, string format, params object[] args)
    {
        if (_logger.IsEnabled(LogLevel.Error))
        {
            var message = FormatMessage(format, args);
            MigrationLog.Error(_logger, ex, message);
        }
    }

    private static string FormatMessage(string format, object[] args)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0)
            return format;

        try
        {
            return string.Format(CultureInfo.InvariantCulture, format, args);
        }
        catch (FormatException)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{format} [{string.Join(", ", args)}]");
        }
    }
}
