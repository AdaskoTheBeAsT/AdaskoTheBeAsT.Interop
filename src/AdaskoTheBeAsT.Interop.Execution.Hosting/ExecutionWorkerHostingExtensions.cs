using AdaskoTheBeAsT.Interop.Execution;
using AdaskoTheBeAsT.Interop.Execution.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace AdaskoTheBeAsT.Interop.Execution.Hosting;

/// <summary>
/// <see cref="IServiceCollection"/> extensions that register the execution
/// worker / pool together with a matching
/// <see cref="Microsoft.Extensions.Hosting.IHostedService"/> wrapper for
/// automatic start/stop in the generic host.
/// </summary>
public static class ExecutionWorkerHostingExtensions
{
    /// <summary>
    /// Registers <see cref="IExecutionWorker{TSession}"/> and
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
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(services);
#else
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }
#endif

        services.AddExecutionWorker<TSession>(configure);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, ExecutionWorkerHostedService<TSession>>());

        return services;
    }

    /// <summary>
    /// Registers <see cref="IExecutionWorkerPool{TSession}"/> and
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
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(services);
#else
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }
#endif

        services.AddExecutionWorkerPool<TSession>(configure);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, ExecutionWorkerPoolHostedService<TSession>>());

        return services;
    }
}
