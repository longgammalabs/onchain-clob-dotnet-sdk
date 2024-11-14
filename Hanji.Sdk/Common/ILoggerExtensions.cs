using Hanji.Common;
using Incendium;
using Microsoft.Extensions.Logging;

namespace Hanji.Common
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
                exception: error.Exception,
                message: $"{message}. Code: {{errorCode}}. Message: {{errorMessage}}",
                args: [error.Code, error.Message]);
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
                exception: error.Exception,
                message: $"{message}. Code: {{errorCode}}. Message: {{errorMessage}}",
                args: [.. args, .. new object?[] { error.Code, error.Message }]);
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
                exception: error.Exception,
                message: $"{message}. Code: {{errorCode}}. Message: {{errorMessage}}",
                args: [error.Code, error.Message]);
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
                exception: error.Exception,
                message: $"{message}. Code: {{errorCode}}. Message: {{errorMessage}}",
                args: [.. args, .. new object?[] { error.Code, error.Message }]);
        }
    }
}
