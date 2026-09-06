using Microsoft.Extensions.Logging;
using Xunit;

namespace LoggingWeaving.IntegrationTests;

public sealed partial class WeavingBehaviorTests
{
    private int evaluationCount;
    private int loggerEvaluationCount;

#if NET6_0_OR_GREATER
    [Fact]
    public void FrameworkConditionalCode_IsWoven()
    {
        var logger = new RecordingLogger(enabled: false);
        logger.LogInformation("Conditional value: {Value}", EvaluateArgument());
        Assert.Equal(0, evaluationCount);
        Assert.Equal(1, logger.IsEnabledCount);
    }
#endif

    [Fact]
    public void DisabledExtensionCall_DoesNotEvaluateArgument()
    {
        var logger = new RecordingLogger(enabled: false);

        logger.LogInformation("Value: {Value}", EvaluateArgument());

        Assert.Equal(0, evaluationCount);
        Assert.Equal(0, logger.LogCount);
        Assert.Equal(1, logger.IsEnabledCount);
    }

    [Fact]
    public void NullLogger_DoesNotEvaluateArgumentOrThrow()
    {
        ILogger? logger = null;

        logger!.LogWarning("Value: {Value}", EvaluateArgument());

        Assert.Equal(0, evaluationCount);
    }

    [Fact]
    public void EnabledExtensionCall_EvaluatesArgumentOnce()
    {
        var logger = new RecordingLogger(enabled: true);

        logger.LogError("Value: {Value}", EvaluateArgument());

        Assert.Equal(1, evaluationCount);
        Assert.Equal(1, logger.LogCount);
    }

    [Fact]
    public void LoggerReceiver_IsEvaluatedOnce()
    {
        var logger = new RecordingLogger(enabled: false);

        GetLogger(logger).LogDebug("Value: {Value}", EvaluateArgument());

        Assert.Equal(1, loggerEvaluationCount);
        Assert.Equal(0, evaluationCount);
        Assert.Equal(1, logger.IsEnabledCount);
    }

    [Fact]
    public void DisabledLoggerMessageCall_DoesNotEvaluateArgument()
    {
        var logger = new RecordingLogger(enabled: false);

        LogGeneratedValue(logger, EvaluateArgument());

        Assert.Equal(0, evaluationCount);
        Assert.Equal(0, logger.LogCount);
        Assert.Equal(1, logger.IsEnabledCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void NullForgivingLoggerMessageReceiver_IsGuardedBeforeArguments(bool? enabled)
    {
        var recordingLogger = new RecordingLogger(enabled: enabled == true);
        ILogger? GetNullableLogger()
        {
            loggerEvaluationCount++;
            return enabled.HasValue ? recordingLogger : null;
        }

        LogGeneratedValue(GetNullableLogger()!, EvaluateArgument());

        Assert.Equal(1, loggerEvaluationCount);
        Assert.Equal(enabled == true ? 1 : 0, evaluationCount);
        Assert.Equal(enabled == true ? 1 : 0, recordingLogger.LogCount);
        Assert.Equal(enabled.HasValue ? (enabled.Value ? 2 : 1) : 0, recordingLogger.IsEnabledCount);
    }

    [Fact]
    public void TypedLoggerMessageCall_DoesNotEraseLoggerType()
    {
        var logger = new RecordingLogger<WeavingBehaviorTests>(enabled: false);

        LogTypedGeneratedValue(logger, EvaluateArgument());

        Assert.Equal(0, evaluationCount);
        Assert.Equal(1, logger.IsEnabledCount);
    }

    [Fact]
    public void ReducedLoggerMessageExtension_DoesNotEvaluateArgument()
    {
        var logger = new RecordingLogger(enabled: false);

        logger.LogGeneratedExtension(EvaluateArgument());

        Assert.Equal(0, evaluationCount);
        Assert.Equal(1, logger.IsEnabledCount);
    }

    [Fact]
    public void MethodOptOut_LeavesCallUnwoven()
    {
        var logger = new RecordingLogger(enabled: false);

        ExecuteUnwovenCall(logger);

        Assert.Equal(1, evaluationCount);
        Assert.Equal(1, logger.LogCount);
        Assert.Equal(0, logger.IsEnabledCount);
    }

    [LoggingGuard(LoggingGuardMode.Disabled)]
    private void ExecuteUnwovenCall(ILogger logger)
    {
        logger.LogInformation("Value: {Value}", EvaluateArgument());
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Generated value: {Value}")]
    private static partial void LogGeneratedValue(ILogger logger, string value);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Typed generated value: {Value}")]
    private static partial void LogTypedGeneratedValue(
        ILogger<WeavingBehaviorTests> logger,
        string value);

    private string EvaluateArgument()
    {
        evaluationCount++;
        return "evaluated";
    }

    private ILogger GetLogger(ILogger logger)
    {
        loggerEvaluationCount++;
        return logger;
    }

    private class RecordingLogger(bool enabled) : ILogger
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

    private sealed class RecordingLogger<T>(bool enabled) : RecordingLogger(enabled), ILogger<T>;
}

internal static partial class TestLogMessages
{
    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Debug,
        Message = "Extension generated value: {Value}")]
    internal static partial void LogGeneratedExtension(this ILogger logger, string value);
}
