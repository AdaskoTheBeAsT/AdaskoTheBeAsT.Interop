using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AdaskoTheBeAsT.Interop.Execution.AspNetCore;

/// <summary>
/// Global ASP.NET Core <see cref="IExceptionHandler"/> that maps execution
/// validation, cancellation, timeout, disposal, and queue-capacity rejection
/// exceptions into safe HTTP status codes and <see cref="ProblemDetails"/> responses.
/// </summary>
public sealed class ExecutionExceptionHandler : IExceptionHandler
{
    private readonly ExecutionAspNetCoreOptions _options;
    private readonly IProblemDetailsService? _problemDetailsService;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExecutionExceptionHandler"/> class.
    /// </summary>
    /// <param name="options">Optional configured <see cref="ExecutionAspNetCoreOptions"/>.</param>
    /// <param name="problemDetailsService">Optional <see cref="IProblemDetailsService"/> writer.</param>
    public ExecutionExceptionHandler(
        IOptions<ExecutionAspNetCoreOptions>? options = null,
        IProblemDetailsService? problemDetailsService = null)
    {
        _options = (options?.Value ?? ExecutionAspNetCoreOptions.Default).Snapshot();
        _options.Validate();
        _problemDetailsService = problemDetailsService;
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        if (httpContext.Response.HasStarted)
        {
            return false;
        }

        if (!ExecutionExceptionMapper.TryMap(
                exception,
                _options,
                out var statusCode,
                out var problemTitle,
                out var retryAfterSeconds,
                httpContext.RequestAborted))
        {
            return false;
        }

        httpContext.Response.StatusCode = statusCode;
        if (retryAfterSeconds is not null)
        {
            httpContext.Response.Headers.RetryAfter = retryAfterSeconds;
        }

        if (problemTitle is null)
        {
            return true;
        }

        if (_problemDetailsService is not null)
        {
            var written = await _problemDetailsService.TryWriteAsync(
                new ProblemDetailsContext
                {
                    HttpContext = httpContext,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = statusCode,
                        Title = problemTitle,
                    },
                }).ConfigureAwait(false);
            if (written)
            {
                return true;
            }
        }

        await TypedResults.Problem(statusCode: statusCode, title: problemTitle)
            .ExecuteAsync(httpContext)
            .ConfigureAwait(false);
        return true;
    }
}
