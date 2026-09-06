using Microsoft.Extensions.Logging;

namespace LoggingWeaving.NetStandardFixture;

public static partial class NetStandardLoggingCase
{
    public static void LogExtension(Func<ILogger?> getLogger, Func<string> getArgument)
    {
        getLogger()!.LogInformation("Value: {Value}", getArgument());
    }

    public static void LogGenerated(Func<ILogger?> getLogger, Func<string> getArgument)
    {
        LogValue(getLogger()!, getArgument());
    }

    [LoggingGuard(LoggingGuardMode.Disabled)]
    public static void LogUnwoven(Func<ILogger?> getLogger, Func<string> getArgument)
    {
        getLogger()!.LogInformation("Value: {Value}", getArgument());
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Generated value: {Value}")]
    private static partial void LogValue(ILogger logger, string value);

    [LoggingGuard(LoggingGuardMode.Disabled)]
    public static class DisabledLogging
    {
        [LoggingGuard(LoggingGuardMode.Enabled)]
        public static void LogOptedIn(Func<ILogger?> getLogger, Func<string> getArgument)
        {
            getLogger()!.LogInformation("Value: {Value}", getArgument());
        }
    }
}
