using AdaskoTheBeAsT.Interop.Execution;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AdaskoTheBeAsT.Interop.Execution.AspNetCore;

/// <summary>
/// Minimal-API endpoint and route-builder extensions for execution exception handling,
/// leak-free worker/pool diagnostics endpoints, and liveness/readiness health probes.
/// </summary>
public static class ExecutionAspNetCoreEndpointExtensions
{
    /// <summary>
    /// Attaches <see cref="ExecutionEndpointFilter"/> and OpenAPI problem-response metadata
    /// (<c>400 Bad Request</c> and <c>503 Service Unavailable</c>) to a minimal-API route handler.
    /// </summary>
    /// <param name="builder">The route handler builder.</param>
    /// <param name="unavailableTitle">
    /// Optional custom <c>503 Service Unavailable</c> problem title. Defaults to
    /// <see cref="ExecutionAspNetCoreOptions.DefaultUnavailableTitle"/> when <see langword="null"/>.
    /// </param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static RouteHandlerBuilder WithExecutionExceptionHandling(
        this RouteHandlerBuilder builder,
        string? unavailableTitle = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = BuildFilterOptions(unavailableTitle);
        return builder
            .AddEndpointFilter(new ExecutionEndpointFilter(options))
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    /// <summary>
    /// Attaches <see cref="ExecutionEndpointFilter"/> configured via <paramref name="configure"/>
    /// and OpenAPI problem-response metadata to a minimal-API route handler.
    /// </summary>
    /// <param name="builder">The route handler builder.</param>
    /// <param name="configure">Callback configuring <see cref="ExecutionAspNetCoreOptions"/>.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static RouteHandlerBuilder WithExecutionExceptionHandling(
        this RouteHandlerBuilder builder,
        Action<ExecutionAspNetCoreOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ExecutionAspNetCoreOptions();
        configure(options);
        return builder
            .AddEndpointFilter(new ExecutionEndpointFilter(options))
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    /// <summary>
    /// Attaches <see cref="ExecutionEndpointFilter"/> to all endpoints in a <see cref="RouteGroupBuilder"/>.
    /// </summary>
    /// <param name="group">The route group builder.</param>
    /// <param name="unavailableTitle">
    /// Optional custom <c>503 Service Unavailable</c> problem title. Defaults to
    /// <see cref="ExecutionAspNetCoreOptions.DefaultUnavailableTitle"/> when <see langword="null"/>.
    /// </param>
    /// <returns>The same <paramref name="group"/> for chaining.</returns>
    public static RouteGroupBuilder WithExecutionExceptionHandling(
        this RouteGroupBuilder group,
        string? unavailableTitle = null)
    {
        ArgumentNullException.ThrowIfNull(group);

        var options = BuildFilterOptions(unavailableTitle);
        group.AddEndpointFilter(new ExecutionEndpointFilter(options));
        return group;
    }

    /// <summary>
    /// Attaches <see cref="ExecutionEndpointFilter"/> configured via <paramref name="configure"/>
    /// to all endpoints in a <see cref="RouteGroupBuilder"/>.
    /// </summary>
    /// <param name="group">The route group builder.</param>
    /// <param name="configure">Callback configuring <see cref="ExecutionAspNetCoreOptions"/>.</param>
    /// <returns>The same <paramref name="group"/> for chaining.</returns>
    public static RouteGroupBuilder WithExecutionExceptionHandling(
        this RouteGroupBuilder group,
        Action<ExecutionAspNetCoreOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ExecutionAspNetCoreOptions();
        configure(options);
        group.AddEndpointFilter(new ExecutionEndpointFilter(options));
        return group;
    }

    /// <summary>
    /// Maps a <c>GET</c> diagnostics endpoint projecting <see cref="IExecutionWorker{TSession}.GetSnapshot"/>
    /// into an AOT-safe, leak-free <see cref="ExecutionWorkerDiagnosticsDto"/>.
    /// </summary>
    /// <typeparam name="TSession">The session type owned by the worker.</typeparam>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">Route pattern (defaults to <c>"/api/diagnostics"</c>).</param>
    /// <returns>A <see cref="RouteHandlerBuilder"/> for further endpoint customization.</returns>
    public static RouteHandlerBuilder MapExecutionWorkerDiagnostics<TSession>(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/api/diagnostics")
        where TSession : class
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ValidatePattern(pattern, nameof(pattern));

        return endpoints
            .MapGet(
                pattern,
                static (IExecutionWorker<TSession> worker) =>
                    TypedResults.Ok(ExecutionWorkerDiagnosticsDto.FromSnapshot(worker.GetSnapshot())))
            .Produces<ExecutionWorkerDiagnosticsDto>();
    }

    /// <summary>
    /// Maps a <c>GET</c> diagnostics endpoint projecting <see cref="IExecutionWorkerPool{TSession}.GetSnapshot"/>
    /// into an AOT-safe, leak-free <see cref="ExecutionWorkerPoolDiagnosticsDto"/>.
    /// </summary>
    /// <typeparam name="TSession">The session type owned by the worker pool.</typeparam>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">Route pattern (defaults to <c>"/api/diagnostics"</c>).</param>
    /// <returns>A <see cref="RouteHandlerBuilder"/> for further endpoint customization.</returns>
    public static RouteHandlerBuilder MapExecutionWorkerPoolDiagnostics<TSession>(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/api/diagnostics")
        where TSession : class
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ValidatePattern(pattern, nameof(pattern));

        return endpoints
            .MapGet(
                pattern,
                static (IExecutionWorkerPool<TSession> pool) =>
                    TypedResults.Ok(ExecutionWorkerPoolDiagnosticsDto.FromSnapshot(pool.GetSnapshot())))
            .Produces<ExecutionWorkerPoolDiagnosticsDto>();
    }

    /// <summary>
    /// Maps standard liveness (<c>/healthz</c>, running zero checks) and readiness
    /// (<c>/readyz</c>, filtered by <paramref name="readyTag"/>) health-check endpoints.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="livenessPattern">Liveness route pattern (defaults to <c>"/healthz"</c>).</param>
    /// <param name="readinessPattern">Readiness route pattern (defaults to <c>"/readyz"</c>).</param>
    /// <param name="readyTag">
    /// Health-check tag included by the readiness probe (defaults to <c>"ready"</c>).
    /// Pass <see langword="null"/> to include all registered health checks on the readiness probe.
    /// </param>
    /// <returns>The same <paramref name="endpoints"/> for chaining.</returns>
    public static IEndpointRouteBuilder MapExecutionHealthProbes(
        this IEndpointRouteBuilder endpoints,
        string livenessPattern = "/healthz",
        string readinessPattern = "/readyz",
        string? readyTag = "ready")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ValidatePattern(livenessPattern, nameof(livenessPattern));
        ValidatePattern(readinessPattern, nameof(readinessPattern));

        if (readyTag is not null && string.IsNullOrWhiteSpace(readyTag))
        {
            throw new ArgumentException("Readiness tag must not be empty or whitespace.", nameof(readyTag));
        }

        endpoints.MapHealthChecks(
            livenessPattern,
            new HealthCheckOptions
            {
                Predicate = static _ => false,
            });

        var tag = readyTag;
        endpoints.MapHealthChecks(
            readinessPattern,
            new HealthCheckOptions
            {
                Predicate = tag is null
                    ? static _ => true
                    : registration => registration.Tags.Contains(tag),
            });

        return endpoints;
    }

    private static ExecutionAspNetCoreOptions BuildFilterOptions(string? unavailableTitle)
    {
        var options = new ExecutionAspNetCoreOptions();
        if (unavailableTitle is not null)
        {
            options.UnavailableTitle = unavailableTitle;
        }

        return options;
    }

    private static void ValidatePattern(string pattern, string paramName)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            throw new ArgumentException("Route pattern must not be null or whitespace.", paramName);
        }
    }
}
