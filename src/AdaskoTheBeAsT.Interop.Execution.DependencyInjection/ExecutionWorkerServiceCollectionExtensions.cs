using AdaskoTheBeAsT.Interop.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace AdaskoTheBeAsT.Interop.Execution.DependencyInjection;

/// <summary>
/// <see cref="IServiceCollection"/> extensions that register
/// <see cref="IExecutionWorker{TSession}"/> and
/// <see cref="IExecutionWorkerPool{TSession}"/> as singletons with
/// <see cref="Microsoft.Extensions.Options"/>-style configuration.
/// </summary>
public static class ExecutionWorkerServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IExecutionWorker{TSession}"/> as a singleton.
    /// Requires <see cref="IExecutionSessionFactory{TSession}"/> to already be
    /// present in <paramref name="services"/>.
    /// </summary>
    /// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
    /// <param name="services">The service collection to mutate.</param>
    /// <param name="configure">Optional options configuration delegate.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddExecutionWorker<TSession>(
        this IServiceCollection services,
        Action<ExecutionWorkerOptions>? configure = null)
        where TSession : class
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        // Use named options keyed by the closed TSession type so that multiple
        // AddExecutionWorker<T>(...) calls with different TSession types do not
        // stack their configuration delegates onto the single global
        // IOptions<ExecutionWorkerOptions> instance (which would make the
        // last-registered delegate win for every worker).
        var optionsName = typeof(TSession).FullName ?? typeof(TSession).Name;

        if (configure is not null)
        {
            services.Configure(optionsName, configure);
        }

        services.TryAddSingleton<IExecutionWorker<TSession>>(sp =>
        {
            var sessionFactory = sp.GetRequiredService<IExecutionSessionFactory<TSession>>();
            var options = ResolveWorkerOptions(sp, optionsName, configure is not null);
            return new ExecutionWorker<TSession>(sessionFactory, options);
        });

        return services;
    }

    /// <summary>
    /// Registers <see cref="IExecutionWorkerPool{TSession}"/> as a singleton.
    /// Requires <see cref="IExecutionSessionFactory{TSession}"/> to already be
    /// present in <paramref name="services"/>, and either a
    /// <paramref name="configure"/> delegate or a pre-registered
    /// <c>IOptions&lt;ExecutionWorkerPoolOptions&gt;</c>.
    /// </summary>
    /// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
    /// <param name="services">The service collection to mutate.</param>
    /// <param name="configure">Optional options configuration delegate.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddExecutionWorkerPool<TSession>(
        this IServiceCollection services,
        Action<ExecutionWorkerPoolOptions>? configure = null)
        where TSession : class
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        // Use named options keyed by the closed TSession type so that multiple
        // AddExecutionWorkerPool<T>(...) calls with different TSession types do not
        // stack their configuration delegates onto the single global
        // IOptions<ExecutionWorkerPoolOptions> instance.
        var optionsName = typeof(TSession).FullName ?? typeof(TSession).Name;

        if (configure is not null)
        {
            services.Configure(optionsName, configure);
        }

        services.TryAddSingleton<IExecutionWorkerPool<TSession>>(sp =>
        {
            var options = ResolvePoolOptions(sp, optionsName, configure is not null);

            return new ExecutionWorkerPool<TSession>(
                workerIndex => sp.GetRequiredService<IExecutionSessionFactory<TSession>>(),
                options);
        });

        return services;
    }

    private static ExecutionWorkerOptions ResolveWorkerOptions(
        IServiceProvider sp,
        string optionsName,
        bool configureProvided)
    {
        if (configureProvided)
        {
            // Named pipeline: when the caller supplied a configure delegate we registered
            // it as a named ExecutionWorkerOptions under typeof(TSession).FullName, so the
            // delegate never collides with other TSession registrations.
            var monitor = sp.GetService<IOptionsMonitor<ExecutionWorkerOptions>>();
            if (monitor is not null)
            {
                return monitor.Get(optionsName);
            }

            var factory = sp.GetService<IOptionsFactory<ExecutionWorkerOptions>>();
            if (factory is not null)
            {
                return factory.Create(optionsName);
            }
        }

        // No per-TSession configure delegate — fall back to the classic unnamed
        // IOptions<ExecutionWorkerOptions> pipeline so that consumers who pre-bind
        // configuration globally (e.g. services.Configure<ExecutionWorkerOptions>(cfg))
        // keep their existing behaviour. When even that is absent, use the defaults.
        return sp.GetService<IOptions<ExecutionWorkerOptions>>()?.Value ?? ExecutionWorkerOptions.Default;
    }

    private static ExecutionWorkerPoolOptions ResolvePoolOptions(
        IServiceProvider sp,
        string optionsName,
        bool configureProvided)
    {
        if (configureProvided)
        {
            var monitor = sp.GetService<IOptionsMonitor<ExecutionWorkerPoolOptions>>();
            if (monitor is not null)
            {
                return monitor.Get(optionsName);
            }

            var factory = sp.GetService<IOptionsFactory<ExecutionWorkerPoolOptions>>();
            if (factory is not null)
            {
                return factory.Create(optionsName);
            }
        }

        return sp.GetService<IOptions<ExecutionWorkerPoolOptions>>()?.Value
            ?? throw new InvalidOperationException(
                $"ExecutionWorkerPoolOptions is required. Call {nameof(AddExecutionWorkerPool)}"
                + " with a configuration delegate or register IOptions<ExecutionWorkerPoolOptions>.");
    }
}
