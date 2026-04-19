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

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<IExecutionWorker<TSession>>(sp =>
        {
            var sessionFactory = sp.GetRequiredService<IExecutionSessionFactory<TSession>>();
            var options = sp.GetService<IOptions<ExecutionWorkerOptions>>()?.Value ?? ExecutionWorkerOptions.Default;
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

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<IExecutionWorkerPool<TSession>>(sp =>
        {
            var options = sp.GetService<IOptions<ExecutionWorkerPoolOptions>>()?.Value
                ?? throw new InvalidOperationException(
                    $"ExecutionWorkerPoolOptions is required. Call {nameof(AddExecutionWorkerPool)}"
                    + " with a configuration delegate or register IOptions<ExecutionWorkerPoolOptions>.");

            return new ExecutionWorkerPool<TSession>(
                workerIndex => sp.GetRequiredService<IExecutionSessionFactory<TSession>>(),
                options);
        });

        return services;
    }
}
