using Microsoft.Extensions.Logging;
using Xunit;

namespace LoggingWeaving.TelemetryIntegrationTests;

public sealed partial class NullableLoggerMessageTests
{
    private int argumentEvaluationCount;
    private int loggerEvaluationCount;

    [Theory]
    [InlineData(null, false, 0)]
    [InlineData(null, true, 0)]
    [InlineData(false, false, 1)]
    [InlineData(false, true, 1)]
    [InlineData(true, false, 2)]
    [InlineData(true, true, 1)]
    public void NullableLoggerMessage_RespectsWovenGuard(
        bool? enabled,
        bool skipEnabledCheck,
        int expectedEnabledCheckCount)
    {
        var recordingLogger = new RecordingLogger(enabled: enabled == true);
        ILogger? logger = enabled.HasValue ? recordingLogger : null;

        if (skipEnabledCheck)
        {
            LogValueWithoutEnabledCheck(GetLogger(logger), EvaluateArgument());
        }
        else
        {
            LogValue(GetLogger(logger), EvaluateArgument());
        }

        Assert.Equal(1, loggerEvaluationCount);
        Assert.Equal(enabled == true ? 1 : 0, argumentEvaluationCount);
        Assert.Equal(enabled == true ? 1 : 0, recordingLogger.LogCount);
        Assert.Equal(expectedEnabledCheckCount, recordingLogger.IsEnabledCount);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Nullable generated value: {Value}")]
    private static partial void LogValue(ILogger? logger, string value);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "Nullable generated value without the generated enabled check: {Value}",
        SkipEnabledCheck = true)]
    private static partial void LogValueWithoutEnabledCheck(ILogger? logger, string value);

    private ILogger? GetLogger(ILogger? logger)
    {
        loggerEvaluationCount++;
        return logger;
    }

    private string EvaluateArgument()
    {
        argumentEvaluationCount++;
        return "evaluated";
    }

    private sealed class RecordingLogger(bool enabled) : ILogger
    {
        public int IsEnabledCount { get; private set; }

        public int LogCount { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel)
        {
            IsEnabledCount++;
            return enabled;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            LogCount++;
        }
    }
}
