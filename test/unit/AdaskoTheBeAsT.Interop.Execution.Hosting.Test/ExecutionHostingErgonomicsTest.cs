using AdaskoTheBeAsT.Interop.Execution;
using AdaskoTheBeAsT.Interop.Execution.DependencyInjection;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

#pragma warning disable IDISP013, VSTHRD003, AsyncFixer04

namespace AdaskoTheBeAsT.Interop.Execution.Hosting.Test;

public sealed class ExecutionHostingErgonomicsTest
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public void NewHostedServiceOverloads_ShouldValidateNullArguments()
    {
        const IServiceCollection? nullServices = null;
        var services = new ServiceCollection();

        Action workerNullServices = () => nullServices!.AddExecutionWorkerHostedService<HostedTestSession>(
            static (_, _) => { });
        Action workerNullConfigure = () => services.AddExecutionWorkerHostedService<HostedTestSession>(
            configure: (Action<IServiceProvider, ExecutionWorkerOptions>)null!);
        Action workerDelegateNullServices = () => nullServices!.AddExecutionWorkerHostedService<HostedTestSession>(
            static (_, _, _) => new HostedTestSession(1));
        Action workerDelegateNullCreate = () => services.AddExecutionWorkerHostedService<HostedTestSession>(
            createSession: null!);

        Action poolNullServices = () => nullServices!.AddExecutionWorkerPoolHostedService<HostedTestSession>(
            static (_, _) => { });
        Action poolNullConfigure = () => services.AddExecutionWorkerPoolHostedService<HostedTestSession>(
            configure: (Action<IServiceProvider, ExecutionWorkerPoolOptions>)null!);
        Action poolIndexedNullServices = () => nullServices!.AddExecutionWorkerPoolHostedService<HostedTestSession>(
            static (sp, _) => sp.GetRequiredService<IExecutionSessionFactory<HostedTestSession>>(),
            static (_, _) => { });
        Action poolIndexedNullResolver = () => services.AddExecutionWorkerPoolHostedService<HostedTestSession>(
            sessionFactoryResolver: null!,
            configure: static (_, _) => { });
        Action poolIndexedNullConfigure = () => services.AddExecutionWorkerPoolHostedService<HostedTestSession>(
            static (sp, _) => sp.GetRequiredService<IExecutionSessionFactory<HostedTestSession>>(),
            configure: (Action<IServiceProvider, ExecutionWorkerPoolOptions>)null!);
        Action poolDelegateNullServices = () => nullServices!.AddExecutionWorkerPoolHostedService<HostedTestSession>(
            static (_, _, _) => new HostedTestSession(1));
        Action poolDelegateNullCreate = () => services.AddExecutionWorkerPoolHostedService<HostedTestSession>(
            createSession: null!);

        workerNullServices.Should().Throw<ArgumentNullException>().WithParameterName(nameof(services));
        workerNullConfigure.Should().Throw<ArgumentNullException>().WithParameterName("configure");
        workerDelegateNullServices.Should().Throw<ArgumentNullException>().WithParameterName(nameof(services));
        workerDelegateNullCreate.Should().Throw<ArgumentNullException>().WithParameterName("createSession");
        poolNullServices.Should().Throw<ArgumentNullException>().WithParameterName(nameof(services));
        poolNullConfigure.Should().Throw<ArgumentNullException>().WithParameterName("configure");
        poolIndexedNullServices.Should().Throw<ArgumentNullException>().WithParameterName(nameof(services));
        poolIndexedNullResolver.Should().Throw<ArgumentNullException>().WithParameterName("sessionFactoryResolver");
        poolIndexedNullConfigure.Should().Throw<ArgumentNullException>().WithParameterName("configure");
        poolDelegateNullServices.Should().Throw<ArgumentNullException>().WithParameterName(nameof(services));
        poolDelegateNullCreate.Should().Throw<ArgumentNullException>().WithParameterName("createSession");
    }

    [Theory]
    [InlineData(0, 0, 5)]
    [InlineData(2, -1, 5)]
    [InlineData(2, 0, -5)]
    public async Task HostStartAsync_ShouldFailFastWithOptionsValidationExceptionBeforeFirstResolutionAsync(
        int workerCount,
        int queueCapacity,
        int disposeTimeoutSeconds)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(new HostPoolConfig(workerCount, queueCapacity, disposeTimeoutSeconds));
        builder.Services.AddSingleton<IExecutionSessionFactory<HostedTestSession>, HostedTestSessionFactory>();

        // Register only AddExecutionWorkerPool (no hosted service resolving it eagerly)
        // so ValidateOnStart alone triggers OptionsValidationException during IHost.StartAsync.
        builder.Services.AddExecutionWorkerPool<HostedTestSession>((sp, options) =>
        {
            var cfg = sp.GetRequiredService<HostPoolConfig>();
            options.WorkerCount = cfg.WorkerCount;
            options.QueueCapacity = cfg.QueueCapacity;
            options.DisposeTimeout = TimeSpan.FromSeconds(cfg.DisposeTimeoutSeconds);
        });

        using var host = builder.Build();
        Func<Task> start = () => host.StartAsync(TestToken);

        await start.Should().ThrowAsync<OptionsValidationException>();
    }

    [Fact]
    public async Task AddExecutionWorkerPoolHostedService_CalledTwice_ShouldBeIdempotentAndForwardDispatcherAsync()
    {
        var disposedCount = 0;
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(new HostPoolConfig(workerCount: 2, queueCapacity: 0, disposeTimeoutSeconds: 5));

        builder.Services.AddExecutionWorkerPoolHostedService<DisposableHostedSession>(
            (sp, ctx, _) => new DisposableHostedSession(
                (sp.GetRequiredService<HostPoolConfig>().WorkerCount * 100) + ctx.WorkerIndex,
                () => Interlocked.Increment(ref disposedCount)),
            (sp, options) =>
            {
                options.WorkerCount = sp.GetRequiredService<HostPoolConfig>().WorkerCount;
                options.SchedulingStrategy = SchedulingStrategy.RoundRobin;
            });

        builder.Services.AddExecutionWorkerPoolHostedService<DisposableHostedSession>(
            (_, _, _) => new DisposableHostedSession(999, static () => { }),
            (_, options) => options.WorkerCount = 4);

        using (var host = builder.Build())
        {
            var hostedServices = host.Services.GetServices<IHostedService>()
                .OfType<ExecutionWorkerPoolHostedService<DisposableHostedSession>>()
                .ToArray();
            hostedServices.Should().ContainSingle();

            await host.StartAsync(TestToken);

            var pool = host.Services.GetRequiredService<IExecutionWorkerPool<DisposableHostedSession>>();
            var dispatcher = host.Services.GetRequiredService<IExecutionDispatcher<DisposableHostedSession>>();
            dispatcher.Should().BeSameAs(pool);
            pool.WorkerCount.Should().Be(2);

            var first = await dispatcher.ExecuteAsync(static (s, _) => s.Id, cancellationToken: TestToken);
            var second = await dispatcher.ExecuteAsync(static (s, _) => s.Id, cancellationToken: TestToken);
            new[] { first, second }.Should().BeEquivalentTo([200, 201]);

            await host.StopAsync(TestToken);
        }

        disposedCount.Should().Be(2);
    }

    [Fact]
    public async Task AddExecutionWorkerHostedService_And_IndexedPoolProviderOverloads_ShouldRunThroughHostLifecycleAsync()
    {
        var workerDisposed = 0;
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton("host-prefix");
        builder.Services.AddSingleton<IExecutionSessionFactory<HostedTestSession>, HostedTestSessionFactory>();
        builder.Services.AddSingleton<IExecutionSessionFactory<ThirdHostedSession>, ThirdHostedSessionFactory>();

        builder.Services.AddExecutionWorkerHostedService<DisposableHostedSession>(
            (_, _, _) => new DisposableHostedSession(42, () => Interlocked.Increment(ref workerDisposed)),
            (sp, options) => options.Name = sp.GetRequiredService<string>() + "-worker");

        builder.Services.AddExecutionWorkerHostedService<HostedTestSession>(
            (sp, options) => options.Name = sp.GetRequiredService<string>() + "-typed");

        builder.Services.AddExecutionWorkerPoolHostedService<ThirdHostedSession>(
            (sp, options) =>
            {
                options.WorkerCount = 1;
                options.Name = sp.GetRequiredService<string>() + "-typed-pool";
            });

        builder.Services.AddExecutionWorkerPoolHostedService<SecondHostedSession>(
            (_, slot) => new DelegateExecutionSessionFactory<SecondHostedSession>(_ => new SecondHostedSession(10 + slot)),
            (sp, options) =>
            {
                options.WorkerCount = 2;
                options.Name = sp.GetRequiredService<string>() + "-pool";
            });

        using (var host = builder.Build())
        {
            await host.StartAsync(TestToken);

            var workerDispatcher = host.Services.GetRequiredService<IExecutionDispatcher<DisposableHostedSession>>();
            var typedWorker = host.Services.GetRequiredService<IExecutionDispatcher<HostedTestSession>>();
            var typedPool = host.Services.GetRequiredService<IExecutionDispatcher<ThirdHostedSession>>();
            var poolDispatcher = host.Services.GetRequiredService<IExecutionDispatcher<SecondHostedSession>>();

            workerDispatcher.Name.Should().Be("host-prefix-worker");
            typedWorker.Name.Should().Be("host-prefix-typed");
            typedPool.Name.Should().Be("host-prefix-typed-pool");
            poolDispatcher.Name.Should().Be("host-prefix-pool");
            (await workerDispatcher.ExecuteAsync(static (s, _) => s.Id, cancellationToken: TestToken)).Should().Be(42);
            (await typedWorker.ExecuteAsync(static (s, _) => s.SessionId, cancellationToken: TestToken)).Should().BePositive();
            (await typedPool.ExecuteAsync(static (s, _) => s.Id, cancellationToken: TestToken)).Should().Be(77);
            (await poolDispatcher.ExecuteAsync(static (s, _) => s.Id, cancellationToken: TestToken)).Should().BeOneOf(10, 11);

            await host.StopAsync(TestToken);
        }

        workerDisposed.Should().Be(1);
    }

    private sealed class HostPoolConfig(int workerCount, int queueCapacity, int disposeTimeoutSeconds)
    {
        public int WorkerCount { get; } = workerCount;

        public int QueueCapacity { get; } = queueCapacity;

        public int DisposeTimeoutSeconds { get; } = disposeTimeoutSeconds;
    }

    private sealed class DisposableHostedSession(int id, Action onDispose) : IDisposable
    {
        public int Id { get; } = id;

        public void Dispose() => onDispose?.Invoke();
    }

    private sealed class SecondHostedSession(int id)
    {
        public int Id { get; } = id;
    }

    private sealed class ThirdHostedSession(int id)
    {
        public int Id { get; } = id;
    }

    private sealed class ThirdHostedSessionFactory : IExecutionSessionFactory<ThirdHostedSession>
    {
        public ThirdHostedSession CreateSession(CancellationToken cancellationToken) => new(77);

        public void DisposeSession(ThirdHostedSession session)
        {
        }
    }
}
