using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LoggingWeaving.AttributeSample;

internal static class Program
{
    private static void Main()
    {
        ILogger logger = NullLogger.Instance;
        int defaultMethodCount = 0;
        int disabledMethodCount = 0;
        int disabledClassCount = 0;
        int enabledOverrideCount = 0;

        DefaultMethod(logger, ref defaultMethodCount);
        DisabledMethod(logger, ref disabledMethodCount);
        DisabledClass.DefaultMethod(logger, ref disabledClassCount);
        DisabledClass.EnabledMethod(logger, ref enabledOverrideCount);

        if (defaultMethodCount != 0 || disabledMethodCount != 1 ||
            disabledClassCount != 1 || enabledOverrideCount != 0)
        {
            throw new InvalidOperationException(
                $"Expected argument counts 0, 1, 1, 0; got {defaultMethodCount}, " +
                $"{disabledMethodCount}, {disabledClassCount}, {enabledOverrideCount}.");
        }

        Console.WriteLine(
            $"Verified argument counts: {defaultMethodCount}, {disabledMethodCount}, " +
            $"{disabledClassCount}, {enabledOverrideCount}.");
        Console.WriteLine("With a disabled ILogger, woven calls skip arguments; unwoven calls evaluate them.");
        Console.WriteLine("Default: woven; disabled method/class: unwoven; enabled method overrides disabled class.");
    }

    private static void DefaultMethod(ILogger logger, ref int evaluationCount)
    {
        logger.LogInformation("Value: {Value}", ++evaluationCount);
    }

    [LoggingGuard(LoggingGuardMode.Disabled)]
    private static void DisabledMethod(ILogger logger, ref int evaluationCount)
    {
        logger.LogInformation("Value: {Value}", ++evaluationCount);
    }

    [LoggingGuard(LoggingGuardMode.Disabled)]
    private static class DisabledClass
    {
        public static void DefaultMethod(ILogger logger, ref int evaluationCount)
        {
            logger.LogInformation("Value: {Value}", ++evaluationCount);
        }

        [LoggingGuard(LoggingGuardMode.Enabled)]
        public static void EnabledMethod(ILogger logger, ref int evaluationCount)
        {
            logger.LogInformation("Value: {Value}", ++evaluationCount);
        }
    }
}
