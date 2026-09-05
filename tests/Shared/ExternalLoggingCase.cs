using Microsoft.Extensions.Logging;

internal static class ExternalLoggingCase
{
    internal static bool Run(ILogger logger)
    {
        bool evaluated = false;

        logger.LogInformation("External value: {Value}", Evaluate());

        return evaluated;

        string Evaluate()
        {
            evaluated = true;
            return "evaluated";
        }
    }
}
