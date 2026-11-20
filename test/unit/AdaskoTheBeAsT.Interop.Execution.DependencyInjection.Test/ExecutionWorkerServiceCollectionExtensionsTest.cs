using AdaskoTheBeAsT.Interop.Execution;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Execution.DependencyInjection.Test;

public sealed class ExecutionWorkerServiceCollectionExtensionsTest
{
    [Fact]
    public void AddExecutionWorker_ShouldThrowWhenServicesIsNull()
    {
        const IServiceCollection? services = null;
        var action = () => services!.AddExecutionWorker<DiTestSession>();

        action.Should().Throw<ArgumentNullException>().WithParameterName(nameof(services));
    }

    [Fact]
    public void AddExecutionWorkerPool_ShouldThrowWhenServicesIsNull()
    {
        const IServiceCollection? services = null;
        var action = () => services!.AddExecutionWorkerPool<DiTestSession>();

        action.Should().Throw<ArgumentNullException>().WithParameterName(nameof(services));
    }

    [Fact]
    public async Task AddExecutionWorker_ShouldResolveAndExecuteAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IExecutionSessionFactory<DiTestSession>, DiTestSessionFactory>();
        services.AddExecutionWorker<DiTestSession>(options => options.Name = "DI Worker");

        await using var provider = services.BuildServiceProvider();
        var worker = provider.GetRequiredService<IExecutionWorker<DiTestSession>>();

        var result = await worker.ExecuteAsync(
            static (session, _) => session.SessionId,
            cancellationToken: CancellationToken.None);

        result.Should().BePositive();
    }

    [Fact]
    public async Task AddExecutionWorker_ShouldBeSingletonAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IExecutionSessionFactory<DiTestSession>, DiTestSessionFactory>();
        services.AddExecutionWorker<DiTestSession>();

        await using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<IExecutionWorker<DiTestSession>>();
        var second = provider.GetRequiredService<IExecutionWorker<DiTestSession>>();

        second.Should().BeSameAs(first);
    }

    [Fact]
    public async Task AddExecutionWorker_ShouldApplyConfigureDelegateAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IExecutionSessionFactory<DiTestSession>, DiTestSessionFactory>();
        services.AddExecutionWorker<DiTestSession>(options =>
        {
            options.Name = "Configured Worker";
            options.MaxOperationsPerSession = 5;
        });

        await using var provider = services.BuildServiceProvider();
        var worker = provider.GetRequiredService<IExecutionWorker<DiTestSession>>();

        // Execute once to ensure the worker is actually wired with the configured options.
        _ = await worker.ExecuteAsync(
            static (session, _) => session.SessionId,
            cancellationToken: CancellationToken.None);

        worker.Should().NotBeNull();
    }

    [Fact]
    public async Task AddExecutionWorkerPool_ShouldResolveAndExecuteAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IExecutionSessionFactory<DiTestSession>, DiTestSessionFactory>();
        services.AddExecutionWorkerPool<DiTestSession>(options =>
        {
            options.WorkerCount = 2;
            options.Name = "DI Pool";
        });

        await using var provider = services.BuildServiceProvider();
        var pool = provider.GetRequiredService<IExecutionWorkerPool<DiTestSession>>();

        var result = await pool.ExecuteAsync(
            static (session, _) => session.SessionId,
            cancellationToken: CancellationToken.None);

        result.Should().BePositive();
        pool.WorkerCount.Should().Be(2);
    }

    [Fact]
    public async Task AddExecutionWorkerPool_ShouldThrowWhenOptionsNotConfiguredAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IExecutionSessionFactory<DiTestSession>, DiTestSessionFactory>();
        services.AddExecutionWorkerPool<DiTestSession>();

        await using var provider = services.BuildServiceProvider();

        Action action = () => _ = provider.GetRequiredService<IExecutionWorkerPool<DiTestSession>>();

        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task AddExecutionWorker_ShouldIsolateOptionsAcrossDifferentSessionTypesAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IExecutionSessionFactory<DiTestSession>, DiTestSessionFactory>();
        services.AddSingleton<IExecutionSessionFactory<DiSecondTestSession>, DiSecondTestSessionFactory>();

        services.AddExecutionWorker<DiTestSession>(options =>
        {
            options.Name = "first-tsession";
            options.MaxOperationsPerSession = 42;
        });
        services.AddExecutionWorker<DiSecondTestSession>(options =>
        {
            options.Name = "second-tsession";
            options.MaxOperationsPerSession = 7;
        });

        await using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IExecutionWorker<DiTestSession>>();
        var second = provider.GetRequiredService<IExecutionWorker<DiSecondTestSession>>();

        first.Name.Should().Be("first-tsession");
        second.Name.Should().Be("second-tsession");
    }

    [Fact]
    public async Task AddExecutionWorkerPool_ShouldIsolateOptionsAcrossDifferentSessionTypesAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IExecutionSessionFactory<DiTestSession>, DiTestSessionFactory>();
        services.AddSingleton<IExecutionSessionFactory<DiSecondTestSession>, DiSecondTestSessionFactory>();

        services.AddExecutionWorkerPool<DiTestSession>(options =>
        {
            options.Name = "first-pool";
            options.WorkerCount = 3;
        });
        services.AddExecutionWorkerPool<DiSecondTestSession>(options =>
        {
            options.Name = "second-pool";
            options.WorkerCount = 1;
        });

        await using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IExecutionWorkerPool<DiTestSession>>();
        var second = provider.GetRequiredService<IExecutionWorkerPool<DiSecondTestSession>>();

        first.Name.Should().Be("first-pool");
        first.WorkerCount.Should().Be(3);
        second.Name.Should().Be("second-pool");
        second.WorkerCount.Should().Be(1);
    }

    [Fact]
    public async Task AddExecutionWorker_ShouldFallBackToUnnamedIOptionsWhenNoConfigureDelegateAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IExecutionSessionFactory<DiTestSession>, DiTestSessionFactory>();
        services.Configure<ExecutionWorkerOptions>(options => options.Name = "unnamed-pipeline");

        // No configure delegate on AddExecutionWorker → ResolveWorkerOptions must skip the
        // named IOptionsMonitor / IOptionsFactory branches and fall back to the unnamed
        // IOptions<ExecutionWorkerOptions> pipeline.
        services.AddExecutionWorker<DiTestSession>();

        await using var provider = services.BuildServiceProvider();
        var worker = provider.GetRequiredService<IExecutionWorker<DiTestSession>>();

        worker.Name.Should().Be("unnamed-pipeline");
    }

    [Fact]
    public async Task AddExecutionWorker_ShouldUseExecutionWorkerOptionsDefaultWhenNothingIsRegisteredAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IExecutionSessionFactory<DiTestSession>, DiTestSessionFactory>();

        // No configure delegate AND no unnamed IOptions<ExecutionWorkerOptions>.
        services.AddExecutionWorker<DiTestSession>();

        await using var provider = services.BuildServiceProvider();
        var worker = provider.GetRequiredService<IExecutionWorker<DiTestSession>>();

        // ExecutionWorkerOptions.Default returns a fresh instance with Name == null.
        worker.Name.Should().BeNull();
    }

    [Fact]
    public async Task AddExecutionWorker_ShouldUseIOptionsFactoryWhenMonitorIsMissingAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IExecutionSessionFactory<DiTestSession>, DiTestSessionFactory>();
        services.AddExecutionWorker<DiTestSession>(options => options.Name = "via-factory");

        // Strip IOptionsMonitor<T> out of the container so ResolveWorkerOptions must
        // fall through to the IOptionsFactory<T> branch (which services.Configure
        // registered alongside the monitor). AddOptions registers monitor as an
        // open-generic descriptor, not a closed ExecutionWorkerOptions one.
        var monitorDescriptor = services.Single(
            descriptor => descriptor.ServiceType == typeof(IOptionsMonitor<>));
        services.Remove(monitorDescriptor);

        await using var provider = services.BuildServiceProvider();
        var worker = provider.GetRequiredService<IExecutionWorker<DiTestSession>>();

        worker.Name.Should().Be("via-factory");
    }

    [Fact]
    public async Task AddExecutionWorkerPool_ShouldUseIOptionsFactoryWhenMonitorIsMissingAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IExecutionSessionFactory<DiTestSession>, DiTestSessionFactory>();
        services.AddExecutionWorkerPool<DiTestSession>(options =>
        {
            options.Name = "pool-via-factory";
            options.WorkerCount = 2;
        });

        var monitorDescriptor = services.Single(
            descriptor => descriptor.ServiceType == typeof(IOptionsMonitor<>));
        services.Remove(monitorDescriptor);

        await using var provider = services.BuildServiceProvider();
        var pool = provider.GetRequiredService<IExecutionWorkerPool<DiTestSession>>();

        pool.Name.Should().Be("pool-via-factory");
        pool.WorkerCount.Should().Be(2);
    }

    [Fact]
    public async Task AddExecutionWorkerPool_ShouldResolveFromUnnamedIOptionsWhenNoConfigureAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IExecutionSessionFactory<DiTestSession>, DiTestSessionFactory>();
        services.Configure<ExecutionWorkerPoolOptions>(options =>
        {
            options.WorkerCount = 2;
            options.Name = "unnamed-pool-pipeline";
        });

        // No configure on AddExecutionWorkerPool → ResolvePoolOptions falls back to
        // unnamed IOptions<ExecutionWorkerPoolOptions>.
        services.AddExecutionWorkerPool<DiTestSession>();

        await using var provider = services.BuildServiceProvider();
        var pool = provider.GetRequiredService<IExecutionWorkerPool<DiTestSession>>();

        pool.Name.Should().Be("unnamed-pool-pipeline");
        pool.WorkerCount.Should().Be(2);
    }

    [Fact]
    public async Task AddExecutionWorker_ShouldSupportExecuteValueAsyncThroughInterfaceAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IExecutionSessionFactory<DiTestSession>, DiTestSessionFactory>();
        services.AddExecutionWorker<DiTestSession>(options => options.Name = "DI ValueTask Worker");

        await using var provider = services.BuildServiceProvider();
        var worker = provider.GetRequiredService<IExecutionWorker<DiTestSession>>();

        var voidExecuted = false;
        await worker.ExecuteValueAsync(
            (_, _) => voidExecuted = true,
            cancellationToken: CancellationToken.None);

        var result = await worker.ExecuteValueAsync(
            static (session, _) => session.SessionId,
            cancellationToken: CancellationToken.None);

        voidExecuted.Should().BeTrue();
        result.Should().BePositive();
    }

    [Fact]
    public async Task AddExecutionWorkerPool_ShouldResolveCustomSchedulerAndSupportExecuteValueAsyncAsync()
    {
        var scheduler = new RecordingFirstWorkerScheduler();
        var services = new ServiceCollection();
        services.AddSingleton<IExecutionSessionFactory<DiTestSession>, DiTestSessionFactory>();
        services.AddSingleton<IWorkerScheduler<DiTestSession>>(scheduler);
        services.AddExecutionWorkerPool<DiTestSession>(options =>
        {
            options.WorkerCount = 2;
            options.Name = "DI Custom Scheduler Pool";
        });

        await using var provider = services.BuildServiceProvider();
        var pool = provider.GetRequiredService<IExecutionWorkerPool<DiTestSession>>();

        var voidExecuted = false;
        await pool.ExecuteValueAsync(
            (_, _) => voidExecuted = true,
            cancellationToken: CancellationToken.None);

        var result = await pool.ExecuteValueAsync(
            static (session, _) => session.SessionId,
            cancellationToken: CancellationToken.None);

        voidExecuted.Should().BeTrue();
        result.Should().BePositive();
        scheduler.SelectCount.Should().Be(2);
    }

    [Fact]
    public void AddExecutionWorkerPool_IndexedOverload_ShouldValidateArguments()
    {
        const IServiceCollection? nullServices = null;
        var services = new ServiceCollection();

        Action nullSvc = () => nullServices!.AddExecutionWorkerPool<DiTestSession>(
            static (sp, _) => sp.GetRequiredService<IExecutionSessionFactory<DiTestSession>>());
        Action nullResolver = () => services.AddExecutionWorkerPool<DiTestSession>(
            sessionFactoryResolver: null!);

        nullSvc.Should().Throw<ArgumentNullException>().WithParameterName(nameof(services));
        nullResolver.Should().Throw<ArgumentNullException>().WithParameterName("sessionFactoryResolver");
    }

    [Fact]
    public async Task AddExecutionWorker_And_Pool_ShouldResolveRegisteredContextualFactoryAsync()
    {
        var contextualFactory = new DiContextualSessionFactory();
        var services = new ServiceCollection();
        services.AddSingleton<IContextualExecutionSessionFactory<DiTestSession>>(contextualFactory);
        services.AddExecutionWorker<DiTestSession>(options => options.Name = "ContextualWorker");
        services.AddExecutionWorkerPool<DiTestSession>(options =>
        {
            options.WorkerCount = 2;
            options.Name = "ContextualPool";
        });

        await using var provider = services.BuildServiceProvider();
        var worker = provider.GetRequiredService<IExecutionWorker<DiTestSession>>();
        var pool = provider.GetRequiredService<IExecutionWorkerPool<DiTestSession>>();

        await worker.InitializeAsync(CancellationToken.None);
        await pool.InitializeAsync(CancellationToken.None);

        contextualFactory.ObservedWorkerCounts.Should().Contain(1);
        contextualFactory.ObservedWorkerCounts.Should().Contain(2);
    }

    [Fact]
    public async Task AddExecutionWorkerPool_IndexedOverload_ShouldPassWorkerSlotAndPropagateBeforeThreadStartAsync()
    {
        var resolvedSlots = new List<int>();
        var beforeStartCount = 0;
        var services = new ServiceCollection();
        services.AddExecutionWorkerPool<DiTestSession>(
            (_, workerIndex) =>
            {
                resolvedSlots.Add(workerIndex);
                return new SlotTaggedDiFactory(workerIndex);
            },
            options =>
            {
                options.WorkerCount = 3;
                options.SchedulingStrategy = SchedulingStrategy.RoundRobin;
                options.BeforeThreadStart = () => Interlocked.Increment(ref beforeStartCount);
            });

        await using var provider = services.BuildServiceProvider();
        var pool = provider.GetRequiredService<IExecutionWorkerPool<DiTestSession>>();
        await pool.InitializeAsync(CancellationToken.None);

        var observedIds = new int[3];
        for (var i = 0; i < 3; i++)
        {
            observedIds[i] = await pool.ExecuteAsync(
                static (s, _) => s.SessionId,
                cancellationToken: CancellationToken.None);
        }

        resolvedSlots.Should().Equal(0, 1, 2);
        beforeStartCount.Should().Be(1);
        observedIds.Should().BeEquivalentTo([100, 101, 102]);
    }

    private sealed class DiContextualSessionFactory : ContextualExecutionSessionFactory<DiTestSession>
    {
        private int _nextId;

        public System.Collections.Concurrent.ConcurrentBag<int> ObservedWorkerCounts { get; } = [];

        public override DiTestSession CreateSession(SessionLifecycleContext context, CancellationToken cancellationToken)
        {
            ObservedWorkerCounts.Add(context.WorkerCount);
            return new DiTestSession(Interlocked.Increment(ref _nextId));
        }

        public override void DisposeSession(DiTestSession session, SessionLifecycleContext context)
        {
        }
    }

    private sealed class SlotTaggedDiFactory(int slot) : IExecutionSessionFactory<DiTestSession>
    {
        public DiTestSession CreateSession(CancellationToken cancellationToken) => new(100 + slot);

        public void DisposeSession(DiTestSession session)
        {
        }
    }

    private sealed class RecordingFirstWorkerScheduler : IWorkerScheduler<DiTestSession>
    {
        private int _selectCount;

        public int SelectCount => Volatile.Read(ref _selectCount);

        public IExecutionWorker<DiTestSession> SelectWorker(IReadOnlyList<IExecutionWorker<DiTestSession>> workers)
        {
            Interlocked.Increment(ref _selectCount);
            return workers[0];
        }
    }
}
