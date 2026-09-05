using Microsoft.Extensions.Logging;
using LoggingWeaving;

int evaluationCount = 0;
ILogger logger = new DisabledLogger();

ExecuteUnwovenCall();

if (evaluationCount != 1)
{
    throw new InvalidOperationException("The generated opt-out attribute was not honored.");
}

evaluationCount = 0;
logger.LogInformation("Value: {Value}", EvaluateArgument());

if (evaluationCount != 0)
{
    throw new InvalidOperationException("The packaged rewriter did not defer argument evaluation.");
}

if (ExternalLoggingCase.Run(logger))
{
    throw new InvalidOperationException("A linked source argument was evaluated while logging was disabled.");
}

return;

[LoggingGuard(LoggingGuardMode.Disabled)]
void ExecuteUnwovenCall()
{
    logger.LogInformation("Value: {Value}", EvaluateArgument());
}

string EvaluateArgument()
{
    evaluationCount++;
    return "evaluated";
}

internal sealed class DisabledLogger : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => false;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
    }
}
