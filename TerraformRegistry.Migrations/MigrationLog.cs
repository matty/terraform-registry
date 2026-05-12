using Microsoft.Extensions.Logging;

namespace TerraformRegistry.Migrations;

internal static partial class MigrationLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Trace, Message = "{Message}")]
    internal static partial void Trace(ILogger logger, string message);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "{Message}")]
    internal static partial void Debug(ILogger logger, string message);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "{Message}")]
    internal static partial void Information(ILogger logger, string message);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "{Message}")]
    internal static partial void Warning(ILogger logger, string message);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "{Message}")]
    internal static partial void Error(ILogger logger, string message);

    [LoggerMessage(EventId = 6, Level = LogLevel.Error, Message = "{Message}")]
    internal static partial void Error(ILogger logger, Exception? exception, string message);

    [LoggerMessage(EventId = 7, Level = LogLevel.Error, Message = "Database migration failed at script: {Script}")]
    internal static partial void DatabaseMigrationFailed(ILogger logger, Exception? exception, string? script);

    [LoggerMessage(EventId = 8, Level = LogLevel.Information, Message = "Database migration completed. Executed {Count} script(s)")]
    internal static partial void DatabaseMigrationCompleted(ILogger logger, int count);

    [LoggerMessage(EventId = 9, Level = LogLevel.Information, Message = "  Executed: {Script}")]
    internal static partial void ExecutedScript(ILogger logger, string script);

    [LoggerMessage(EventId = 10, Level = LogLevel.Information, Message = "Database is already up to date")]
    internal static partial void DatabaseAlreadyUpToDate(ILogger logger);

    [LoggerMessage(EventId = 11, Level = LogLevel.Information, Message = "Detected existing {Provider} database with {Count} legacy migration(s) — bootstrapping DbUp journal")]
    internal static partial void ExistingDatabaseDetected(ILogger logger, string provider, int count);

    [LoggerMessage(EventId = 12, Level = LogLevel.Information, Message = "Bootstrapped {Count} script(s) in DbUp journal")]
    internal static partial void BootstrappedScripts(ILogger logger, int count);

    [LoggerMessage(EventId = 13, Level = LogLevel.Information, Message = "  Marked as executed: {Script}")]
    internal static partial void MarkedAsExecuted(ILogger logger, string script);

    [LoggerMessage(EventId = 14, Level = LogLevel.Warning, Message = "Detected over-bootstrapped DbUp journal — roles table missing despite journal entry. Repairing...")]
    internal static partial void OverBootstrappedJournal(ILogger logger);

    [LoggerMessage(EventId = 15, Level = LogLevel.Information, Message = "  Removed bogus journal entry: {Script}")]
    internal static partial void RemovedBogusJournalEntry(ILogger logger, string script);

    [LoggerMessage(EventId = 16, Level = LogLevel.Information, Message = "Repaired journal — removed {Count} over-bootstrapped entries. Scripts will now run.")]
    internal static partial void RepairedJournal(ILogger logger, int count);

    [LoggerMessage(EventId = 17, Level = LogLevel.Information, Message = "Removed legacy schema_version table ({Count} entries)")]
    internal static partial void RemovedLegacySchemaVersion(ILogger logger, int count);
}
