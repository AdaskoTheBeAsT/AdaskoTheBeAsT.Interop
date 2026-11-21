using Microsoft.AspNetCore.Http;

namespace AdaskoTheBeAsT.Interop.Execution.AspNetCore;

/// <summary>
/// Minimal-API <see cref="IEndpointFilter"/> that maps dedicated-thread execution
/// exceptions (<see cref="ArgumentException"/>, caller-aborted
/// <see cref="OperationCanceledException"/>, <see cref="TimeoutException"/>,
/// <see cref="ObjectDisposedException"/>, and <see cref="ExecutionAdmissionRejectedException"/>)
/// to safe HTTP responses without leaking native paths or stack traces.
/// </summary>
public sealed class ExecutionEndpointFilter : IEndpointFilter
{
    private readonly ExecutionAspNetCoreOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExecutionEndpointFilter"/> class.
    /// </summary>
    /// <param name="options">Optional exception-mapping options.</param>
    public ExecutionEndpointFilter(ExecutionAspNetCoreOptions? options = null)
    {
        _options = (options ?? ExecutionAspNetCoreOptions.Default).Snapshot();
        _options.Validate();
    }

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        try
        {
            return await next(context).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            var httpContext = context.HttpContext;
            if (!ExecutionExceptionMapper.TryMap(
                    exception,
                    _options,
                    out var statusCode,
                    out var problemTitle,
                    out var retryAfterSeconds,
                    httpContext.RequestAborted))
            {
                throw;
            }

            if (retryAfterSeconds is not null)
            {
                httpContext.Response.Headers.RetryAfter = retryAfterSeconds;
            }

            if (problemTitle is null)
            {
                return TypedResults.StatusCode(statusCode);
            }

            return TypedResults.Problem(statusCode: statusCode, title: problemTitle);
        }
    }
}
