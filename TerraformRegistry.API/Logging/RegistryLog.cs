using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace TerraformRegistry.API.Logging;

public static class RegistryLog
{
    private static readonly ConcurrentDictionary<LoggerMessageCacheKey, Delegate> MessageCache = new();

    public static void Trace(ILogger logger, string message)
    {
        Log(logger, LogLevel.Trace, null, message);
    }

    public static void Debug(ILogger logger, string message)
    {
        Log(logger, LogLevel.Debug, null, message);
    }

    public static void Information(ILogger logger, string message)
    {
        Log(logger, LogLevel.Information, null, message);
    }

    public static void Information<T0>(ILogger logger, string message, T0 arg0)
    {
        Log(logger, LogLevel.Information, null, message, arg0);
    }

    public static void Information<T0, T1>(ILogger logger, string message, T0 arg0, T1 arg1)
    {
        Log(logger, LogLevel.Information, null, message, arg0, arg1);
    }

    public static void Information<T0, T1, T2>(ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2)
    {
        Log(logger, LogLevel.Information, null, message, arg0, arg1, arg2);
    }

    public static void Information<T0, T1, T2, T3>(ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3)
    {
        Log(logger, LogLevel.Information, null, message, arg0, arg1, arg2, arg3);
    }

    public static void Information<T0, T1, T2, T3, T4>(ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
    {
        Log(logger, LogLevel.Information, null, message, arg0, arg1, arg2, arg3, arg4);
    }

    public static void Information<T0, T1, T2, T3, T4, T5>(ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
    {
        Log(logger, LogLevel.Information, null, message, arg0, arg1, arg2, arg3, arg4, arg5);
    }

    public static void Warning(ILogger logger, string message)
    {
        Log(logger, LogLevel.Warning, null, message);
    }

    public static void Warning<T0>(ILogger logger, string message, T0 arg0)
    {
        Log(logger, LogLevel.Warning, null, message, arg0);
    }

    public static void Warning<T0, T1>(ILogger logger, string message, T0 arg0, T1 arg1)
    {
        Log(logger, LogLevel.Warning, null, message, arg0, arg1);
    }

    public static void Warning<T0, T1, T2>(ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2)
    {
        Log(logger, LogLevel.Warning, null, message, arg0, arg1, arg2);
    }

    public static void Warning<T0, T1, T2, T3>(ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3)
    {
        Log(logger, LogLevel.Warning, null, message, arg0, arg1, arg2, arg3);
    }

    public static void Warning<T0, T1, T2, T3, T4>(ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
    {
        Log(logger, LogLevel.Warning, null, message, arg0, arg1, arg2, arg3, arg4);
    }

    public static void Warning<T0, T1, T2, T3, T4, T5>(ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
    {
        Log(logger, LogLevel.Warning, null, message, arg0, arg1, arg2, arg3, arg4, arg5);
    }

    public static void Warning(ILogger logger, Exception? exception, string message)
    {
        Log(logger, LogLevel.Warning, exception, message);
    }

    public static void Warning<T0>(ILogger logger, Exception? exception, string message, T0 arg0)
    {
        Log(logger, LogLevel.Warning, exception, message, arg0);
    }

    public static void Warning<T0, T1>(ILogger logger, Exception? exception, string message, T0 arg0, T1 arg1)
    {
        Log(logger, LogLevel.Warning, exception, message, arg0, arg1);
    }

    public static void Warning<T0, T1, T2>(ILogger logger, Exception? exception, string message, T0 arg0, T1 arg1, T2 arg2)
    {
        Log(logger, LogLevel.Warning, exception, message, arg0, arg1, arg2);
    }

    public static void Warning<T0, T1, T2, T3>(ILogger logger, Exception? exception, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3)
    {
        Log(logger, LogLevel.Warning, exception, message, arg0, arg1, arg2, arg3);
    }

    public static void Warning<T0, T1, T2, T3, T4>(ILogger logger, Exception? exception, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
    {
        Log(logger, LogLevel.Warning, exception, message, arg0, arg1, arg2, arg3, arg4);
    }

    public static void Warning<T0, T1, T2, T3, T4, T5>(ILogger logger, Exception? exception, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
    {
        Log(logger, LogLevel.Warning, exception, message, arg0, arg1, arg2, arg3, arg4, arg5);
    }

    public static void Error(ILogger logger, string message)
    {
        Log(logger, LogLevel.Error, null, message);
    }

    public static void Error<T0>(ILogger logger, string message, T0 arg0)
    {
        Log(logger, LogLevel.Error, null, message, arg0);
    }

    public static void Error<T0, T1>(ILogger logger, string message, T0 arg0, T1 arg1)
    {
        Log(logger, LogLevel.Error, null, message, arg0, arg1);
    }

    public static void Error<T0, T1, T2>(ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2)
    {
        Log(logger, LogLevel.Error, null, message, arg0, arg1, arg2);
    }

    public static void Error<T0, T1, T2, T3>(ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3)
    {
        Log(logger, LogLevel.Error, null, message, arg0, arg1, arg2, arg3);
    }

    public static void Error<T0, T1, T2, T3, T4>(ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
    {
        Log(logger, LogLevel.Error, null, message, arg0, arg1, arg2, arg3, arg4);
    }

    public static void Error<T0, T1, T2, T3, T4, T5>(ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
    {
        Log(logger, LogLevel.Error, null, message, arg0, arg1, arg2, arg3, arg4, arg5);
    }

    public static void Error(ILogger logger, Exception? exception, string message)
    {
        Log(logger, LogLevel.Error, exception, message);
    }

    public static void Error<T0>(ILogger logger, Exception? exception, string message, T0 arg0)
    {
        Log(logger, LogLevel.Error, exception, message, arg0);
    }

    public static void Error<T0, T1>(ILogger logger, Exception? exception, string message, T0 arg0, T1 arg1)
    {
        Log(logger, LogLevel.Error, exception, message, arg0, arg1);
    }

    public static void Error<T0, T1, T2>(ILogger logger, Exception? exception, string message, T0 arg0, T1 arg1, T2 arg2)
    {
        Log(logger, LogLevel.Error, exception, message, arg0, arg1, arg2);
    }

    public static void Error<T0, T1, T2, T3>(ILogger logger, Exception? exception, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3)
    {
        Log(logger, LogLevel.Error, exception, message, arg0, arg1, arg2, arg3);
    }

    public static void Error<T0, T1, T2, T3, T4>(ILogger logger, Exception? exception, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
    {
        Log(logger, LogLevel.Error, exception, message, arg0, arg1, arg2, arg3, arg4);
    }

    public static void Error<T0, T1, T2, T3, T4, T5>(ILogger logger, Exception? exception, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
    {
        Log(logger, LogLevel.Error, exception, message, arg0, arg1, arg2, arg3, arg4, arg5);
    }

    private static void Log(ILogger logger, LogLevel level, Exception? exception, string message)
    {
        if (!logger.IsEnabled(level))
            return;

        var callback = (Action<ILogger, Exception?>)MessageCache.GetOrAdd(
            CreateKey(level, message),
            static key => LoggerMessage.Define(key.Level, new EventId(0), key.Message));

        callback(logger, SensitiveDataRedactor.RedactException(exception));
    }

    private static void Log<T0>(ILogger logger, LogLevel level, Exception? exception, string message, T0 arg0)
    {
        if (!logger.IsEnabled(level))
            return;

        var callback = (Action<ILogger, T0, Exception?>)MessageCache.GetOrAdd(
            CreateKey<T0>(level, message),
            static key => LoggerMessage.Define<T0>(key.Level, new EventId(0), key.Message));

        callback(logger, SensitiveDataRedactor.RedactValue(arg0), SensitiveDataRedactor.RedactException(exception));
    }

    private static void Log<T0, T1>(ILogger logger, LogLevel level, Exception? exception, string message, T0 arg0, T1 arg1)
    {
        if (!logger.IsEnabled(level))
            return;

        var callback = (Action<ILogger, T0, T1, Exception?>)MessageCache.GetOrAdd(
            CreateKey<T0, T1>(level, message),
            static key => LoggerMessage.Define<T0, T1>(key.Level, new EventId(0), key.Message));

        callback(logger, SensitiveDataRedactor.RedactValue(arg0), SensitiveDataRedactor.RedactValue(arg1), SensitiveDataRedactor.RedactException(exception));
    }

    private static void Log<T0, T1, T2>(ILogger logger, LogLevel level, Exception? exception, string message, T0 arg0, T1 arg1, T2 arg2)
    {
        if (!logger.IsEnabled(level))
            return;

        var callback = (Action<ILogger, T0, T1, T2, Exception?>)MessageCache.GetOrAdd(
            CreateKey<T0, T1, T2>(level, message),
            static key => LoggerMessage.Define<T0, T1, T2>(key.Level, new EventId(0), key.Message));

        callback(logger, SensitiveDataRedactor.RedactValue(arg0), SensitiveDataRedactor.RedactValue(arg1), SensitiveDataRedactor.RedactValue(arg2), SensitiveDataRedactor.RedactException(exception));
    }

    private static void Log<T0, T1, T2, T3>(ILogger logger, LogLevel level, Exception? exception, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3)
    {
        if (!logger.IsEnabled(level))
            return;

        var callback = (Action<ILogger, T0, T1, T2, T3, Exception?>)MessageCache.GetOrAdd(
            CreateKey<T0, T1, T2, T3>(level, message),
            static key => LoggerMessage.Define<T0, T1, T2, T3>(key.Level, new EventId(0), key.Message));

        callback(logger, SensitiveDataRedactor.RedactValue(arg0), SensitiveDataRedactor.RedactValue(arg1), SensitiveDataRedactor.RedactValue(arg2), SensitiveDataRedactor.RedactValue(arg3), SensitiveDataRedactor.RedactException(exception));
    }

    private static void Log<T0, T1, T2, T3, T4>(ILogger logger, LogLevel level, Exception? exception, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
    {
        if (!logger.IsEnabled(level))
            return;

        var callback = (Action<ILogger, T0, T1, T2, T3, T4, Exception?>)MessageCache.GetOrAdd(
            CreateKey<T0, T1, T2, T3, T4>(level, message),
            static key => LoggerMessage.Define<T0, T1, T2, T3, T4>(key.Level, new EventId(0), key.Message));

        callback(logger, SensitiveDataRedactor.RedactValue(arg0), SensitiveDataRedactor.RedactValue(arg1), SensitiveDataRedactor.RedactValue(arg2), SensitiveDataRedactor.RedactValue(arg3), SensitiveDataRedactor.RedactValue(arg4), SensitiveDataRedactor.RedactException(exception));
    }

    private static void Log<T0, T1, T2, T3, T4, T5>(ILogger logger, LogLevel level, Exception? exception, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
    {
        if (!logger.IsEnabled(level))
            return;

        var callback = (Action<ILogger, T0, T1, T2, T3, T4, T5, Exception?>)MessageCache.GetOrAdd(
            CreateKey<T0, T1, T2, T3, T4, T5>(level, message),
            static key => LoggerMessage.Define<T0, T1, T2, T3, T4, T5>(key.Level, new EventId(0), key.Message));

        callback(logger, SensitiveDataRedactor.RedactValue(arg0), SensitiveDataRedactor.RedactValue(arg1), SensitiveDataRedactor.RedactValue(arg2), SensitiveDataRedactor.RedactValue(arg3), SensitiveDataRedactor.RedactValue(arg4), SensitiveDataRedactor.RedactValue(arg5), SensitiveDataRedactor.RedactException(exception));
    }

    private static LoggerMessageCacheKey CreateKey(LogLevel level, string message)
    {
        return new LoggerMessageCacheKey(level, message, null, null, null, null, null, null);
    }

    private static LoggerMessageCacheKey CreateKey<T0>(LogLevel level, string message)
    {
        return new LoggerMessageCacheKey(level, message, typeof(T0), null, null, null, null, null);
    }

    private static LoggerMessageCacheKey CreateKey<T0, T1>(LogLevel level, string message)
    {
        return new LoggerMessageCacheKey(level, message, typeof(T0), typeof(T1), null, null, null, null);
    }

    private static LoggerMessageCacheKey CreateKey<T0, T1, T2>(LogLevel level, string message)
    {
        return new LoggerMessageCacheKey(level, message, typeof(T0), typeof(T1), typeof(T2), null, null, null);
    }

    private static LoggerMessageCacheKey CreateKey<T0, T1, T2, T3>(LogLevel level, string message)
    {
        return new LoggerMessageCacheKey(level, message, typeof(T0), typeof(T1), typeof(T2), typeof(T3), null, null);
    }

    private static LoggerMessageCacheKey CreateKey<T0, T1, T2, T3, T4>(LogLevel level, string message)
    {
        return new LoggerMessageCacheKey(level, message, typeof(T0), typeof(T1), typeof(T2), typeof(T3), typeof(T4), null);
    }

    private static LoggerMessageCacheKey CreateKey<T0, T1, T2, T3, T4, T5>(LogLevel level, string message)
    {
        return new LoggerMessageCacheKey(level, message, typeof(T0), typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5));
    }

    private readonly record struct LoggerMessageCacheKey(
        LogLevel Level,
        string Message,
        Type? Argument0,
        Type? Argument1,
        Type? Argument2,
        Type? Argument3,
        Type? Argument4,
        Type? Argument5);
}
