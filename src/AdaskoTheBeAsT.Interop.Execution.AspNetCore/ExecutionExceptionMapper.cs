using System.Globalization;
using AdaskoTheBeAsT.Interop.Execution;
using Microsoft.AspNetCore.Http;

namespace AdaskoTheBeAsT.Interop.Execution.AspNetCore;

internal static class ExecutionExceptionMapper
{
    public static bool TryMap(
        Exception exception,
        ExecutionAspNetCoreOptions options,
        out int statusCode,
        out string? problemTitle,
        out string? retryAfterSeconds,
        CancellationToken requestAborted)
    {
        switch (exception)
        {
            case ArgumentException:
                statusCode = StatusCodes.Status400BadRequest;
                problemTitle = options.BadRequestTitle;
                retryAfterSeconds = null;
                return true;

            case OperationCanceledException when requestAborted.IsCancellationRequested:
                statusCode = StatusCodes.Status499ClientClosedRequest;
                problemTitle = null;
                retryAfterSeconds = null;
                return true;

            case TimeoutException:
            case ObjectDisposedException:
            case ExecutionAdmissionRejectedException:
                statusCode = StatusCodes.Status503ServiceUnavailable;
                problemTitle = options.UnavailableTitle;
                retryAfterSeconds = options.RetryAfter is { } retryAfter
                    ? FormatRetryAfterSeconds(retryAfter)
                    : null;
                return true;

            default:
                statusCode = 0;
                problemTitle = null;
                retryAfterSeconds = null;
                return false;
        }
    }

    private static string FormatRetryAfterSeconds(TimeSpan retryAfter)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        return seconds.ToString(CultureInfo.InvariantCulture);
    }
}
