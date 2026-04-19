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

        result.Should().BeGreaterThan(0);
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

        result.Should().BeGreaterThan(0);
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
}
