using Microsoft.Extensions.Logging;
using Serilog;

namespace LoggingWeaving;

internal sealed class Program
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
            builder.AddSerilog(dispose: true);
        });

        ILogger<Program> logger = loggerFactory.CreateLogger<Program>();

        logger.LogInformation("Application started at {StartedAt}", DateTimeOffset.Now);

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

        logger.LogInformation("Application finished");
    }
}
