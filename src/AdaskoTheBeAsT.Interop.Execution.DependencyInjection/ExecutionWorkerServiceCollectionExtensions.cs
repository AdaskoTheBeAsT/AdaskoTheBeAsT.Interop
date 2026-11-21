using AdaskoTheBeAsT.Interop.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace AdaskoTheBeAsT.Interop.Execution.DependencyInjection;

/// <summary>
/// <see cref="IServiceCollection"/> extensions that register
/// <see cref="IExecutionWorker{TSession}"/>,
/// <see cref="IExecutionWorkerPool{TSession}"/>, and forwarded
/// <see cref="IExecutionDispatcher{TSession}"/> singletons with
/// <see cref="Microsoft.Extensions.Options"/> configuration and startup validation.
/// </summary>
public static class ExecutionWorkerServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IExecutionWorker{TSession}"/> and forwards
    /// <see cref="IExecutionDispatcher{TSession}"/> as singletons.
    /// Requires <see cref="IExecutionSessionFactory{TSession}"/> (or
    /// <see cref="IContextualExecutionSessionFactory{TSession}"/>) to be present in
    /// <paramref name="services"/>.
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
        ThrowIfNull(services, nameof(services));

        return AddExecutionWorkerCore<TSession>(
            services,
            static sp => ResolveSessionFactory<TSession>(sp),
            configure,
            configureWithProvider: null);
    }

    /// <summary>
    /// Registers <see cref="IExecutionWorker{TSession}"/> and forwards
    /// <see cref="IExecutionDispatcher{TSession}"/> as singletons using an
    /// <see cref="IServiceProvider"/>-aware options configuration callback.
    /// </summary>
    /// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
    /// <param name="services">The service collection to mutate.</param>
    /// <param name="configure">Options configuration delegate receiving the root <see cref="IServiceProvider"/>.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    public static IServiceCollection AddExecutionWorker<TSession>(
        this IServiceCollection services,
        Action<IServiceProvider, ExecutionWorkerOptions> configure)
        where TSession : class
    {
        ThrowIfNull(services, nameof(services));
        ThrowIfNull(configure, nameof(configure));

        return AddExecutionWorkerCore<TSession>(
            services,
            static sp => ResolveSessionFactory<TSession>(sp),
            configure: null,
            configureWithProvider: configure);
    }

    /// <summary>
    /// Registers <see cref="IExecutionWorker{TSession}"/> and forwards
    /// <see cref="IExecutionDispatcher{TSession}"/> as singletons using inline
    /// contextual session creation and optional disposal delegates backed by
    /// <see cref="DelegateExecutionSessionFactory{TSession}"/>.
    /// </summary>
    /// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
    /// <param name="services">The service collection to mutate.</param>
    /// <param name="createSession">Delegate invoked on the dedicated worker thread to create a session.</param>
    /// <param name="configure">Optional <see cref="IServiceProvider"/>-aware options configuration delegate.</param>
    /// <param name="disposeSession">
    /// Optional delegate invoked on the dedicated worker thread to dispose a session.
    /// When <see langword="null"/>, <see cref="IDisposable.Dispose"/> is called if
    /// <typeparamref name="TSession"/> implements <see cref="IDisposable"/>.
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="createSession"/> is <see langword="null"/>.
    /// </exception>
    public static IServiceCollection AddExecutionWorker<TSession>(
        this IServiceCollection services,
        Func<IServiceProvider, SessionLifecycleContext, CancellationToken, TSession> createSession,
        Action<IServiceProvider, ExecutionWorkerOptions>? configure = null,
        Action<TSession, SessionLifecycleContext>? disposeSession = null)
        where TSession : class
    {
        ThrowIfNull(services, nameof(services));
        ThrowIfNull(createSession, nameof(createSession));

        var create = createSession;
        var dispose = disposeSession;
        return AddExecutionWorkerCore<TSession>(
            services,
            sp => new DelegateExecutionSessionFactory<TSession>(
                (context, cancellationToken) => create(sp, context, cancellationToken),
                dispose),
            configure: null,
            configureWithProvider: configure);
    }

    /// <summary>
    /// Registers <see cref="IExecutionWorkerPool{TSession}"/> and forwards
    /// <see cref="IExecutionDispatcher{TSession}"/> as singletons.
    /// Requires <see cref="IExecutionSessionFactory{TSession}"/> (or
    /// <see cref="IContextualExecutionSessionFactory{TSession}"/>) to already be
    /// present in <paramref name="services"/>, and either a
    /// <paramref name="configure"/> delegate or a pre-registered
    /// <c>IOptions&lt;ExecutionWorkerPoolOptions&gt;</c>. If an
    /// <see cref="IWorkerScheduler{TSession}"/> is registered in
    /// <paramref name="services"/>, it is used instead of the built-in
    /// <see cref="ExecutionWorkerPoolOptions.SchedulingStrategy"/>.
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
        ThrowIfNull(services, nameof(services));

        return AddExecutionWorkerPoolCore<TSession>(
            services,
            static sp =>
            {
                var shared = ResolveSessionFactory<TSession>(sp);
                return _ => shared;
            },
            configure,
            configureWithProvider: null,
            allowDefaultUnconfiguredOptions: false);
    }

    /// <summary>
    /// Registers <see cref="IExecutionWorkerPool{TSession}"/> and forwards
    /// <see cref="IExecutionDispatcher{TSession}"/> as singletons using an
    /// <see cref="IServiceProvider"/>-aware options configuration callback.
    /// </summary>
    /// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
    /// <param name="services">The service collection to mutate.</param>
    /// <param name="configure">Options configuration delegate receiving the root <see cref="IServiceProvider"/>.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    public static IServiceCollection AddExecutionWorkerPool<TSession>(
        this IServiceCollection services,
        Action<IServiceProvider, ExecutionWorkerPoolOptions> configure)
        where TSession : class
    {
        ThrowIfNull(services, nameof(services));
        ThrowIfNull(configure, nameof(configure));

        return AddExecutionWorkerPoolCore<TSession>(
            services,
            static sp =>
            {
                var shared = ResolveSessionFactory<TSession>(sp);
                return _ => shared;
            },
            configure: null,
            configureWithProvider: configure,
            allowDefaultUnconfiguredOptions: false);
    }

    /// <summary>
    /// Registers <see cref="IExecutionWorkerPool{TSession}"/> and forwards
    /// <see cref="IExecutionDispatcher{TSession}"/> as singletons using a
    /// worker-indexed session factory resolver.
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
    public static IServiceCollection AddExecutionWorkerPool<TSession>(
        this IServiceCollection services,
        Func<IServiceProvider, int, IExecutionSessionFactory<TSession>> sessionFactoryResolver,
        Action<ExecutionWorkerPoolOptions>? configure = null)
        where TSession : class
    {
        ThrowIfNull(services, nameof(services));
        ThrowIfNull(sessionFactoryResolver, nameof(sessionFactoryResolver));

        var resolver = sessionFactoryResolver;
        return AddExecutionWorkerPoolCore<TSession>(
            services,
            sp => workerIndex => resolver(sp, workerIndex),
            configure,
            configureWithProvider: null,
            allowDefaultUnconfiguredOptions: false);
    }

    /// <summary>
    /// Registers <see cref="IExecutionWorkerPool{TSession}"/> and forwards
    /// <see cref="IExecutionDispatcher{TSession}"/> as singletons using a
    /// worker-indexed session factory resolver and an <see cref="IServiceProvider"/>-aware
    /// options configuration callback.
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
    public static IServiceCollection AddExecutionWorkerPool<TSession>(
        this IServiceCollection services,
        Func<IServiceProvider, int, IExecutionSessionFactory<TSession>> sessionFactoryResolver,
        Action<IServiceProvider, ExecutionWorkerPoolOptions> configure)
        where TSession : class
    {
        ThrowIfNull(services, nameof(services));
        ThrowIfNull(sessionFactoryResolver, nameof(sessionFactoryResolver));
        ThrowIfNull(configure, nameof(configure));

        var resolver = sessionFactoryResolver;
        return AddExecutionWorkerPoolCore<TSession>(
            services,
            sp => workerIndex => resolver(sp, workerIndex),
            configure: null,
            configureWithProvider: configure,
            allowDefaultUnconfiguredOptions: false);
    }

    /// <summary>
    /// Registers <see cref="IExecutionWorkerPool{TSession}"/> and forwards
    /// <see cref="IExecutionDispatcher{TSession}"/> as singletons using inline
    /// contextual session creation and optional disposal delegates backed by
    /// <see cref="DelegateExecutionSessionFactory{TSession}"/>.
    /// </summary>
    /// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
    /// <param name="services">The service collection to mutate.</param>
    /// <param name="createSession">
    /// Delegate invoked on each dedicated worker thread with its <see cref="SessionLifecycleContext"/>
    /// to create a session.
    /// </param>
    /// <param name="configure">Optional <see cref="IServiceProvider"/>-aware options configuration delegate.</param>
    /// <param name="disposeSession">
    /// Optional delegate invoked on the dedicated worker thread to dispose a session.
    /// When <see langword="null"/>, <see cref="IDisposable.Dispose"/> is called if
    /// <typeparamref name="TSession"/> implements <see cref="IDisposable"/>.
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="createSession"/> is <see langword="null"/>.
    /// </exception>
    public static IServiceCollection AddExecutionWorkerPool<TSession>(
        this IServiceCollection services,
        Func<IServiceProvider, SessionLifecycleContext, CancellationToken, TSession> createSession,
        Action<IServiceProvider, ExecutionWorkerPoolOptions>? configure = null,
        Action<TSession, SessionLifecycleContext>? disposeSession = null)
        where TSession : class
    {
        ThrowIfNull(services, nameof(services));
        ThrowIfNull(createSession, nameof(createSession));

        var create = createSession;
        var dispose = disposeSession;
        return AddExecutionWorkerPoolCore<TSession>(
            services,
            sp =>
            {
                var sharedFactory = new DelegateExecutionSessionFactory<TSession>(
                    (context, cancellationToken) => create(sp, context, cancellationToken),
                    dispose);
                return _ => sharedFactory;
            },
            configure: null,
            configureWithProvider: configure,
            allowDefaultUnconfiguredOptions: true);
    }

    private static IServiceCollection AddExecutionWorkerCore<TSession>(
        IServiceCollection services,
        Func<IServiceProvider, IExecutionSessionFactory<TSession>> sessionFactoryProvider,
        Action<ExecutionWorkerOptions>? configure,
        Action<IServiceProvider, ExecutionWorkerOptions>? configureWithProvider)
        where TSession : class
    {
        if (HasServiceDescriptor(services, typeof(IExecutionWorker<TSession>)))
        {
            services.TryAddSingleton<IExecutionDispatcher<TSession>>(
                static sp => sp.GetRequiredService<IExecutionWorker<TSession>>());
            return services;
        }

        var optionsName = typeof(TSession).FullName ?? typeof(TSession).Name;
        var configureProvided = configure is not null || configureWithProvider is not null;

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<ExecutionWorkerOptions>, ExecutionWorkerOptionsValidator>());

        if (configure is not null)
        {
            services.AddOptions<ExecutionWorkerOptions>(optionsName)
                .Configure(configure)
                .ValidateOnStart();
        }
        else if (configureWithProvider is not null)
        {
            var providerCallback = configureWithProvider;
            services.AddOptions<ExecutionWorkerOptions>(optionsName)
                .Configure<IServiceProvider>((options, sp) => providerCallback(sp, options))
                .ValidateOnStart();
        }

        var factoryProvider = sessionFactoryProvider;
        services.TryAddSingleton<IExecutionWorker<TSession>>(sp =>
        {
            var sessionFactory = factoryProvider(sp);
            var options = ResolveWorkerOptions(sp, optionsName, configureProvided);
            return new ExecutionWorker<TSession>(sessionFactory, options);
        });
        services.TryAddSingleton<IExecutionDispatcher<TSession>>(
            static sp => sp.GetRequiredService<IExecutionWorker<TSession>>());

        return services;
    }

    private static IServiceCollection AddExecutionWorkerPoolCore<TSession>(
        IServiceCollection services,
        Func<IServiceProvider, Func<int, IExecutionSessionFactory<TSession>>> sessionFactoryProviderBuilder,
        Action<ExecutionWorkerPoolOptions>? configure,
        Action<IServiceProvider, ExecutionWorkerPoolOptions>? configureWithProvider,
        bool allowDefaultUnconfiguredOptions)
        where TSession : class
    {
        if (HasServiceDescriptor(services, typeof(IExecutionWorkerPool<TSession>)))
        {
            services.TryAddSingleton<IExecutionDispatcher<TSession>>(
                static sp => sp.GetRequiredService<IExecutionWorkerPool<TSession>>());
            return services;
        }

        var optionsName = typeof(TSession).FullName ?? typeof(TSession).Name;
        var configureProvided = configure is not null || configureWithProvider is not null;

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<ExecutionWorkerPoolOptions>, ExecutionWorkerPoolOptionsValidator>());

        if (configure is not null)
        {
            services.AddOptions<ExecutionWorkerPoolOptions>(optionsName)
                .Configure(configure)
                .ValidateOnStart();
        }
        else if (configureWithProvider is not null)
        {
            var providerCallback = configureWithProvider;
            services.AddOptions<ExecutionWorkerPoolOptions>(optionsName)
                .Configure<IServiceProvider>((options, sp) => providerCallback(sp, options))
                .ValidateOnStart();
        }

        var builder = sessionFactoryProviderBuilder;
        services.TryAddSingleton<IExecutionWorkerPool<TSession>>(sp =>
        {
            var options = ResolvePoolOptions(sp, optionsName, configureProvided, allowDefaultUnconfiguredOptions);
            var scheduler = sp.GetService<IWorkerScheduler<TSession>>();
            var perWorkerFactory = builder(sp);

            return new ExecutionWorkerPool<TSession>(
                perWorkerFactory,
                options,
                scheduler);
        });
        services.TryAddSingleton<IExecutionDispatcher<TSession>>(
            static sp => sp.GetRequiredService<IExecutionWorkerPool<TSession>>());

        return services;
    }

    private static IExecutionSessionFactory<TSession> ResolveSessionFactory<TSession>(IServiceProvider sp)
        where TSession : class
    {
        return sp.GetService<IContextualExecutionSessionFactory<TSession>>()
            ?? sp.GetRequiredService<IExecutionSessionFactory<TSession>>();
    }

    private static ExecutionWorkerOptions ResolveWorkerOptions(
        IServiceProvider sp,
        string optionsName,
        bool configureProvided)
    {
        if (configureProvided)
        {
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

        return sp.GetService<IOptions<ExecutionWorkerOptions>>()?.Value ?? ExecutionWorkerOptions.Default;
    }

    private static ExecutionWorkerPoolOptions ResolvePoolOptions(
        IServiceProvider sp,
        string optionsName,
        bool configureProvided,
        bool allowDefaultUnconfiguredOptions)
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

        var unnamedOptions = sp.GetService<IOptions<ExecutionWorkerPoolOptions>>()?.Value;
        if (unnamedOptions is not null)
        {
            return unnamedOptions;
        }

        if (allowDefaultUnconfiguredOptions)
        {
            return new ExecutionWorkerPoolOptions();
        }

        throw new InvalidOperationException(
            $"ExecutionWorkerPoolOptions is required. Call {nameof(AddExecutionWorkerPool)}"
            + " with a configuration delegate or register IOptions<ExecutionWorkerPoolOptions>.");
    }

    private static bool HasServiceDescriptor(IServiceCollection services, Type serviceType)
    {
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == serviceType)
            {
                return true;
            }
        }

        return false;
    }

    private static void ThrowIfNull<T>(T? argument, string paramName)
        where T : class
    {
        _ = argument ?? throw new ArgumentNullException(paramName);
    }

    private sealed class ExecutionWorkerOptionsValidator : IValidateOptions<ExecutionWorkerOptions>
    {
        public ValidateOptionsResult Validate(string? name, ExecutionWorkerOptions options)
        {
            if (options is null)
            {
                return ValidateOptionsResult.Fail("ExecutionWorkerOptions instance must not be null.");
            }

            try
            {
                options.Validate();
                return ValidateOptionsResult.Success;
            }
            catch (ArgumentOutOfRangeException exception)
            {
                return ValidateOptionsResult.Fail(exception.Message);
            }
        }
    }

    private sealed class ExecutionWorkerPoolOptionsValidator : IValidateOptions<ExecutionWorkerPoolOptions>
    {
        public ValidateOptionsResult Validate(string? name, ExecutionWorkerPoolOptions options)
        {
            if (options is null)
            {
                return ValidateOptionsResult.Fail("ExecutionWorkerPoolOptions instance must not be null.");
            }

            try
            {
                options.Validate();
                return ValidateOptionsResult.Success;
            }
            catch (ArgumentOutOfRangeException exception)
            {
                return ValidateOptionsResult.Fail(exception.Message);
            }
        }
    }
}
