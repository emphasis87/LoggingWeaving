using Microsoft.Extensions.Logging;
using Serilog;

namespace LoggingWeaving.TelemetrySample;

internal sealed partial class Program
{
    private static void Main()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.File(
                "example.log",
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddConsole();
            builder.AddDebug();
            builder.AddSerilog(dispose: true);
        });

        ILogger<Program> logger = loggerFactory.CreateLogger<Program>();

        LogApplicationStarted(
            logger,
            "LoggingWeaving".Replace("Weaving", " Weaving"),
            DateTimeOffset.Now);

        LogApplicationStartedWithoutEnabledCheck(
            logger,
            "LoggingWeaving".Replace("Weaving", " Weaving"),
            DateTimeOffset.Now);

        Microsoft.Extensions.Logging.ILogger? nullableLogger = logger;

        LogApplicationStartedWithNullableLogger(
            nullableLogger,
            "LoggingWeaving".Replace("Weaving", " Weaving"),
            DateTimeOffset.Now);

        LogApplicationStartedWithNullableLoggerWithoutEnabledCheck(
            nullableLogger,
            "LoggingWeaving".Replace("Weaving", " Weaving"),
            DateTimeOffset.Now);

        nullableLogger = null;

        // The woven null guards skip both calls, including argument evaluation.
        LogApplicationStartedWithNullableLogger(
            nullableLogger,
            "SkippedWithNullLogger".Replace("With", " With "),
            DateTimeOffset.Now);

        LogApplicationStartedWithNullableLoggerWithoutEnabledCheck(
            nullableLogger,
            "SkippedWithNullLogger".Replace("With", " With "),
            DateTimeOffset.Now);

        for (int itemNumber = 1; itemNumber <= 3; itemNumber++)
        {
            logger.LogDebug("Processing item {ItemNumber}", itemNumber);
        }

        logger.LogWarning("This warning demonstrates the {LogLevel} log level", LogLevel.Warning);

        try
        {
            throw new InvalidOperationException("Example exception");
        }
        catch (InvalidOperationException exception)
        {
            logger.LogError(exception, "An expected error was captured");
        }

        logger.LogInformation(
            "Application {ApplicationName} finished",
            "LoggingWeaving".Replace("Weaving", " Weaving"));
    }

    // LOGGING

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Application {ApplicationName} started at {StartedAt}")]
    private static partial void LogApplicationStarted(
        Microsoft.Extensions.Logging.ILogger logger,
        string applicationName,
        DateTimeOffset startedAt);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "Application {ApplicationName} started without the generated enabled check at {StartedAt}",
        SkipEnabledCheck = true)]
    private static partial void LogApplicationStartedWithoutEnabledCheck(
        Microsoft.Extensions.Logging.ILogger logger,
        string applicationName,
        DateTimeOffset startedAt);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Information,
        Message = "Application {ApplicationName} started with a nullable logger at {StartedAt}")]
    private static partial void LogApplicationStartedWithNullableLogger(
        Microsoft.Extensions.Logging.ILogger? logger,
        string applicationName,
        DateTimeOffset startedAt);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Information,
        Message = "Application {ApplicationName} started with a nullable logger and without the generated enabled check at {StartedAt}",
        SkipEnabledCheck = true)]
    private static partial void LogApplicationStartedWithNullableLoggerWithoutEnabledCheck(
        Microsoft.Extensions.Logging.ILogger? logger,
        string applicationName,
        DateTimeOffset startedAt);
}
