using AdaskoTheBeAsT.Interop.Execution;
using AdaskoTheBeAsT.Interop.Execution.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace AdaskoTheBeAsT.Interop.Execution.Hosting;

/// <summary>
/// <see cref="IServiceCollection"/> and <see cref="IHealthChecksBuilder"/>
/// extensions that register the execution worker / pool (and forwarded
/// <see cref="IExecutionDispatcher{TSession}"/>) together with a matching
/// <see cref="IHostedService"/> wrapper and readiness health checks for the generic host.
/// </summary>
public static class ExecutionWorkerHostingExtensions
{
    /// <summary>
    /// Registers <see cref="IExecutionWorker{TSession}"/>,
    /// <see cref="IExecutionDispatcher{TSession}"/>, and
    /// <see cref="ExecutionWorkerHostedService{TSession}"/> as singletons.
    /// </summary>
    /// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
    /// <param name="services">The service collection to mutate.</param>
    /// <param name="configure">Optional options configuration delegate.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddExecutionWorkerHostedService<TSession>(
        this IServiceCollection services,
        Action<ExecutionWorkerOptions>? configure = null)
        where TSession : class
    {
        ThrowIfNull(services, nameof(services));

        services.AddExecutionWorker<TSession>(configure);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, ExecutionWorkerHostedService<TSession>>());

        return services;
    }

    /// <summary>
    /// Registers <see cref="IExecutionWorker{TSession}"/>,
    /// <see cref="IExecutionDispatcher{TSession}"/>, and
    /// <see cref="ExecutionWorkerHostedService{TSession}"/> as singletons using an
    /// <see cref="IServiceProvider"/>-aware options configuration callback.
    /// </summary>
    /// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
    /// <param name="services">The service collection to mutate.</param>
    /// <param name="configure">Options configuration delegate receiving the root <see cref="IServiceProvider"/>.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    public static IServiceCollection AddExecutionWorkerHostedService<TSession>(
        this IServiceCollection services,
        Action<IServiceProvider, ExecutionWorkerOptions> configure)
        where TSession : class
    {
        ThrowIfNull(services, nameof(services));
        ThrowIfNull(configure, nameof(configure));

        services.AddExecutionWorker<TSession>(configure);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, ExecutionWorkerHostedService<TSession>>());

        return services;
    }

    /// <summary>
    /// Registers <see cref="IExecutionWorker{TSession}"/>,
    /// <see cref="IExecutionDispatcher{TSession}"/>, and
    /// <see cref="ExecutionWorkerHostedService{TSession}"/> as singletons using inline
    /// contextual session creation and optional disposal delegates.
    /// </summary>
    /// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
    /// <param name="services">The service collection to mutate.</param>
    /// <param name="createSession">Delegate invoked on the dedicated worker thread to create a session.</param>
    /// <param name="configure">Optional <see cref="IServiceProvider"/>-aware options configuration delegate.</param>
    /// <param name="disposeSession">Optional delegate invoked on the dedicated worker thread to dispose a session.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="createSession"/> is <see langword="null"/>.
    /// </exception>
    public static IServiceCollection AddExecutionWorkerHostedService<TSession>(
        this IServiceCollection services,
        Func<IServiceProvider, SessionLifecycleContext, CancellationToken, TSession> createSession,
        Action<IServiceProvider, ExecutionWorkerOptions>? configure = null,
        Action<TSession, SessionLifecycleContext>? disposeSession = null)
        where TSession : class
    {
        ThrowIfNull(services, nameof(services));
        ThrowIfNull(createSession, nameof(createSession));

        services.AddExecutionWorker(createSession, configure, disposeSession);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, ExecutionWorkerHostedService<TSession>>());

        return services;
    }

    /// <summary>
    /// Registers <see cref="IExecutionWorkerPool{TSession}"/>,
    /// <see cref="IExecutionDispatcher{TSession}"/>, and
    /// <see cref="ExecutionWorkerPoolHostedService{TSession}"/> as singletons.
    /// </summary>
    /// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
    /// <param name="services">The service collection to mutate.</param>
    /// <param name="configure">Optional options configuration delegate.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddExecutionWorkerPoolHostedService<TSession>(
        this IServiceCollection services,
        Action<ExecutionWorkerPoolOptions>? configure = null)
        where TSession : class
    {
        ThrowIfNull(services, nameof(services));

        services.AddExecutionWorkerPool<TSession>(configure);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, ExecutionWorkerPoolHostedService<TSession>>());

        return services;
    }

    /// <summary>
    /// Registers <see cref="IExecutionWorkerPool{TSession}"/>,
    /// <see cref="IExecutionDispatcher{TSession}"/>, and
    /// <see cref="ExecutionWorkerPoolHostedService{TSession}"/> as singletons using an
    /// <see cref="IServiceProvider"/>-aware options configuration callback.
    /// </summary>
    /// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
    /// <param name="services">The service collection to mutate.</param>
    /// <param name="configure">Options configuration delegate receiving the root <see cref="IServiceProvider"/>.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    public static IServiceCollection AddExecutionWorkerPoolHostedService<TSession>(
        this IServiceCollection services,
        Action<IServiceProvider, ExecutionWorkerPoolOptions> configure)
        where TSession : class
    {
        ThrowIfNull(services, nameof(services));
        ThrowIfNull(configure, nameof(configure));

        services.AddExecutionWorkerPool<TSession>(configure);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, ExecutionWorkerPoolHostedService<TSession>>());

        return services;
    }

    /// <summary>
    /// Registers <see cref="IExecutionWorkerPool{TSession}"/> and
    /// <see cref="IExecutionDispatcher{TSession}"/> using a worker-indexed session
    /// factory resolver together with <see cref="ExecutionWorkerPoolHostedService{TSession}"/>.
    /// </summary>
    /// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
    /// <param name="services">The service collection to mutate.</param>
    /// <param name="sessionFactoryResolver">
    /// Callback invoked per worker slot (<c>0..WorkerCount-1</c>) to resolve that
    /// worker's <see cref="IExecutionSessionFactory{TSession}"/>.
    /// </param>
    /// <param name="configure">Optional options configuration delegate.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="sessionFactoryResolver"/> is <see langword="null"/>.
    /// </exception>
    public static IServiceCollection AddExecutionWorkerPoolHostedService<TSession>(
        this IServiceCollection services,
        Func<IServiceProvider, int, IExecutionSessionFactory<TSession>> sessionFactoryResolver,
        Action<ExecutionWorkerPoolOptions>? configure = null)
        where TSession : class
    {
        ThrowIfNull(services, nameof(services));
        ThrowIfNull(sessionFactoryResolver, nameof(sessionFactoryResolver));

        services.AddExecutionWorkerPool<TSession>(sessionFactoryResolver, configure);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, ExecutionWorkerPoolHostedService<TSession>>());

        return services;
    }

    /// <summary>
    /// Registers <see cref="IExecutionWorkerPool{TSession}"/> and
    /// <see cref="IExecutionDispatcher{TSession}"/> using a worker-indexed session
    /// factory resolver and an <see cref="IServiceProvider"/>-aware options configuration
    /// callback together with <see cref="ExecutionWorkerPoolHostedService{TSession}"/>.
    /// </summary>
    /// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
    /// <param name="services">The service collection to mutate.</param>
    /// <param name="sessionFactoryResolver">
    /// Callback invoked per worker slot (<c>0..WorkerCount-1</c>) to resolve that
    /// worker's <see cref="IExecutionSessionFactory{TSession}"/>.
    /// </param>
    /// <param name="configure">Options configuration delegate receiving the root <see cref="IServiceProvider"/>.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/>, <paramref name="sessionFactoryResolver"/>, or
    /// <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    public static IServiceCollection AddExecutionWorkerPoolHostedService<TSession>(
        this IServiceCollection services,
        Func<IServiceProvider, int, IExecutionSessionFactory<TSession>> sessionFactoryResolver,
        Action<IServiceProvider, ExecutionWorkerPoolOptions> configure)
        where TSession : class
    {
        ThrowIfNull(services, nameof(services));
        ThrowIfNull(sessionFactoryResolver, nameof(sessionFactoryResolver));
        ThrowIfNull(configure, nameof(configure));

        services.AddExecutionWorkerPool<TSession>(sessionFactoryResolver, configure);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, ExecutionWorkerPoolHostedService<TSession>>());

        return services;
    }

    /// <summary>
    /// Registers <see cref="IExecutionWorkerPool{TSession}"/>,
    /// <see cref="IExecutionDispatcher{TSession}"/>, and
    /// <see cref="ExecutionWorkerPoolHostedService{TSession}"/> as singletons using inline
    /// contextual session creation and optional disposal delegates.
    /// </summary>
    /// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
    /// <param name="services">The service collection to mutate.</param>
    /// <param name="createSession">
    /// Delegate invoked on each dedicated worker thread with its <see cref="SessionLifecycleContext"/>
    /// to create a session.
    /// </param>
    /// <param name="configure">Optional <see cref="IServiceProvider"/>-aware options configuration delegate.</param>
    /// <param name="disposeSession">Optional delegate invoked on the dedicated worker thread to dispose a session.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="createSession"/> is <see langword="null"/>.
    /// </exception>
    public static IServiceCollection AddExecutionWorkerPoolHostedService<TSession>(
        this IServiceCollection services,
        Func<IServiceProvider, SessionLifecycleContext, CancellationToken, TSession> createSession,
        Action<IServiceProvider, ExecutionWorkerPoolOptions>? configure = null,
        Action<TSession, SessionLifecycleContext>? disposeSession = null)
        where TSession : class
    {
        ThrowIfNull(services, nameof(services));
        ThrowIfNull(createSession, nameof(createSession));

        services.AddExecutionWorkerPool(createSession, configure, disposeSession);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, ExecutionWorkerPoolHostedService<TSession>>());

        return services;
    }

    /// <summary>
    /// Registers an <see cref="ExecutionWorkerHealthCheck{TSession}"/> on the
    /// supplied <see cref="IHealthChecksBuilder"/>.
    /// </summary>
    /// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
    /// <param name="builder">The health-checks builder.</param>
    /// <param name="name">
    /// Optional health-check registration name. Defaults to
    /// <c>execution_worker_{TSession}</c>.
    /// </param>
    /// <param name="configure">Optional health-check options callback.</param>
    /// <param name="failureStatus">
    /// Status reported when the check fails (defaults to
    /// <see cref="HealthStatus.Unhealthy"/>).
    /// </param>
    /// <param name="tags">Optional filter tags (for example <c>"ready"</c>).</param>
    /// <param name="timeout">Optional health-check execution timeout.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static IHealthChecksBuilder AddExecutionWorkerHealthCheck<TSession>(
        this IHealthChecksBuilder builder,
        string? name = null,
        Action<ExecutionHealthCheckOptions>? configure = null,
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null,
        TimeSpan? timeout = null)
        where TSession : class
    {
        ThrowIfNull(builder, nameof(builder));

        var options = BuildHealthCheckOptions(configure);
        var checkName = ResolveCheckName(name, "execution_worker_", typeof(TSession).Name);

        return builder.Add(new HealthCheckRegistration(
            checkName,
            sp => new ExecutionWorkerHealthCheck<TSession>(
                sp.GetRequiredService<IExecutionWorker<TSession>>(),
                options),
            failureStatus,
            tags,
            timeout));
    }

    /// <summary>
    /// Registers an <see cref="ExecutionWorkerPoolHealthCheck{TSession}"/> on
    /// the supplied <see cref="IHealthChecksBuilder"/>.
    /// </summary>
    /// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
    /// <param name="builder">The health-checks builder.</param>
    /// <param name="name">
    /// Optional health-check registration name. Defaults to
    /// <c>execution_worker_pool_{TSession}</c>.
    /// </param>
    /// <param name="configure">Optional health-check options callback.</param>
    /// <param name="failureStatus">
    /// Status reported when the check fails (defaults to
    /// <see cref="HealthStatus.Unhealthy"/>).
    /// </param>
    /// <param name="tags">Optional filter tags (for example <c>"ready"</c>).</param>
    /// <param name="timeout">Optional health-check execution timeout.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static IHealthChecksBuilder AddExecutionWorkerPoolHealthCheck<TSession>(
        this IHealthChecksBuilder builder,
        string? name = null,
        Action<ExecutionHealthCheckOptions>? configure = null,
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null,
        TimeSpan? timeout = null)
        where TSession : class
    {
        ThrowIfNull(builder, nameof(builder));

        var options = BuildHealthCheckOptions(configure);
        var checkName = ResolveCheckName(name, "execution_worker_pool_", typeof(TSession).Name);

        return builder.Add(new HealthCheckRegistration(
            checkName,
            sp => new ExecutionWorkerPoolHealthCheck<TSession>(
                sp.GetRequiredService<IExecutionWorkerPool<TSession>>(),
                options),
            failureStatus,
            tags,
            timeout));
    }

    private static ExecutionHealthCheckOptions BuildHealthCheckOptions(
        Action<ExecutionHealthCheckOptions>? configure)
    {
        var options = new ExecutionHealthCheckOptions();
        configure?.Invoke(options);
        var snapshot = options.Snapshot();
        snapshot.Validate();
        return snapshot;
    }

    private static string ResolveCheckName(string? name, string defaultPrefix, string typeName)
    {
        if (name is null)
        {
            return defaultPrefix + typeName;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Health check name must not be empty or whitespace.", nameof(name));
        }

        return name;
    }

    private static void ThrowIfNull<T>(T? argument, string paramName)
        where T : class
    {
        _ = argument ?? throw new ArgumentNullException(paramName);
    }
}
