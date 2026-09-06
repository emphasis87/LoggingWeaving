using Microsoft.Extensions.Logging;

namespace LoggingWeaving.StandardSample;

internal static partial class Program
{
    private static void Main()
    {
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
            builder.SetMinimumLevel(LogLevel.Information).AddConsole());

        int evaluationCount = 0;
        string EvaluateArgument()
        {
            evaluationCount++;
            return "evaluated";
        }

        ILogger? nullableLogger = null;

        // The standard generator requires ILogger, not ILogger?. The woven guard handles null.
        LogValue(nullableLogger!, EvaluateArgument());
        LogValueWithoutEnabledCheck(nullableLogger!, EvaluateArgument());

        if (evaluationCount != 0)
        {
            throw new InvalidOperationException("Null logger calls must not evaluate arguments.");
        }

        nullableLogger = loggerFactory.CreateLogger("StandardSample");
        LogValue(nullableLogger!, EvaluateArgument());
        LogValueWithoutEnabledCheck(nullableLogger!, EvaluateArgument());

        if (evaluationCount != 2)
        {
            throw new InvalidOperationException("Enabled logger calls must evaluate each argument once.");
        }

        Console.WriteLine("Verified: null logger calls skipped arguments; enabled calls evaluated each once.");
    }

    // LOGGING

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Generated value: {Value}")]
    private static partial void LogValue(ILogger logger, string value);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "Generated value without the generated enabled check: {Value}",
        SkipEnabledCheck = true)]
    private static partial void LogValueWithoutEnabledCheck(ILogger logger, string value);
}
