using OnchainClob.Common;
using Incendium;
using Microsoft.Extensions.Logging;

namespace OnchainClob.Common
{
    public static class ILoggerExtensions
    {
        public static void LogError(
            this ILogger logger,
            Error? error,
            string? message)
        {
            if (logger == null)
                return;

            if (error == null)
                throw new ArgumentNullException(nameof(error));

            logger.LogError(
                error.Exception,
                $"{message}. Code: {{errorCode}}. Message: {{errorMessage}}",
                error.Code, error.Message);
        }

        public static void LogError(
            this ILogger logger,
            Error? error,
            string? message,
            params object?[] args)
        {
            if (logger == null)
                return;

            if (error == null)
                throw new ArgumentNullException(nameof(error));

            logger.LogError(
                error.Exception,
                $"{message}. Code: {{errorCode}}. Message: {{errorMessage}}",
                [.. args, .. new object?[] { error.Code, error.Message }]);
        }

        public static void LogWarning(
            this ILogger logger,
            Error? error,
            string? message)
        {
            if (logger == null)
                return;

            if (error == null)
                throw new ArgumentNullException(nameof(error));

            logger.LogWarning(
                error.Exception,
                $"{message}. Code: {{errorCode}}. Message: {{errorMessage}}",
                error.Code, error.Message);
        }

        public static void LogWarning(
            this ILogger logger,
            Error? error,
            string? message,
            params object?[] args)
        {
            if (logger == null)
                return;

            if (error == null)
                throw new ArgumentNullException(nameof(error));

            logger.LogWarning(
                error.Exception,
                $"{message}. Code: {{errorCode}}. Message: {{errorMessage}}",
                [.. args, .. new object?[] { error.Code, error.Message }]);
        }
    }
}
