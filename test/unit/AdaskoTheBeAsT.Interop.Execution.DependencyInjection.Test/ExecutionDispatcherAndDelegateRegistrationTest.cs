using AdaskoTheBeAsT.Interop.Execution;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

#pragma warning disable IDISP013, VSTHRD003, AsyncFixer04

namespace AdaskoTheBeAsT.Interop.Execution.DependencyInjection.Test;

public sealed class ExecutionDispatcherAndDelegateRegistrationTest
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public void NewOverloads_ShouldValidateNullArguments()
    {
        const IServiceCollection? nullServices = null;
        var services = new ServiceCollection();

        Action workerNullServices = () => nullServices!.AddExecutionWorker<DiTestSession>(
            static (_, _) => { });
        Action workerNullConfigure = () => services.AddExecutionWorker<DiTestSession>(
            configure: (Action<IServiceProvider, ExecutionWorkerOptions>)null!);
        Action workerDelegateNullServices = () => nullServices!.AddExecutionWorker<DiTestSession>(
            static (_, _, _) => new DiTestSession(1));
        Action workerDelegateNullCreate = () => services.AddExecutionWorker<DiTestSession>(
            createSession: null!);

        Action poolNullServices = () => nullServices!.AddExecutionWorkerPool<DiTestSession>(
            static (_, _) => { });
        Action poolNullConfigure = () => services.AddExecutionWorkerPool<DiTestSession>(
            configure: (Action<IServiceProvider, ExecutionWorkerPoolOptions>)null!);
        Action poolIndexedNullServices = () => nullServices!.AddExecutionWorkerPool<DiTestSession>(
            static (sp, _) => sp.GetRequiredService<IExecutionSessionFactory<DiTestSession>>(),
            static (_, _) => { });
        Action poolIndexedNullResolver = () => services.AddExecutionWorkerPool<DiTestSession>(
            sessionFactoryResolver: null!,
            configure: static (_, _) => { });
        Action poolIndexedNullConfigure = () => services.AddExecutionWorkerPool<DiTestSession>(
            static (sp, _) => sp.GetRequiredService<IExecutionSessionFactory<DiTestSession>>(),
            configure: (Action<IServiceProvider, ExecutionWorkerPoolOptions>)null!);
        Action poolDelegateNullServices = () => nullServices!.AddExecutionWorkerPool<DiTestSession>(
            static (_, _, _) => new DiTestSession(1));
        Action poolDelegateNullCreate = () => services.AddExecutionWorkerPool<DiTestSession>(
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

    [Fact]
    public async Task AddExecutionWorker_And_Pool_ShouldForwardIExecutionDispatcherAndRespectPreRegisteredDecoratorAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IExecutionSessionFactory<DiTestSession>, DiTestSessionFactory>();
        services.AddSingleton<IExecutionSessionFactory<DiSecondTestSession>, DiSecondTestSessionFactory>();

        services.AddExecutionWorker<DiTestSession>(options => options.Name = "ForwardedWorker");
        services.AddExecutionWorker<DiTestSession>(options => options.Name = "IgnoredDuplicateWorker");
        services.AddExecutionWorkerPool<DiSecondTestSession>(options =>
        {
            options.WorkerCount = 2;
            options.Name = "ForwardedPool";
        });
        services.AddExecutionWorkerPool<DiSecondTestSession>(options => options.WorkerCount = 5);

        await using var provider = services.BuildServiceProvider();

        var worker = provider.GetRequiredService<IExecutionWorker<DiTestSession>>();
        var workerDispatcher = provider.GetRequiredService<IExecutionDispatcher<DiTestSession>>();
        workerDispatcher.Should().BeSameAs(worker);
        worker.Name.Should().Be("ForwardedWorker");

        var pool = provider.GetRequiredService<IExecutionWorkerPool<DiSecondTestSession>>();
        var poolDispatcher = provider.GetRequiredService<IExecutionDispatcher<DiSecondTestSession>>();
        poolDispatcher.Should().BeSameAs(pool);
        pool.WorkerCount.Should().Be(2);

        var customDispatcher = new Mock<IExecutionDispatcher<DiTestSession>>(MockBehavior.Strict).Object;
        var decoratedServices = new ServiceCollection();
        decoratedServices.AddSingleton(customDispatcher);
        decoratedServices.AddSingleton<IExecutionSessionFactory<DiTestSession>, DiTestSessionFactory>();
        decoratedServices.AddExecutionWorker<DiTestSession>();

        await using var decoratedProvider = decoratedServices.BuildServiceProvider();
        decoratedProvider.GetRequiredService<IExecutionDispatcher<DiTestSession>>().Should().BeSameAs(customDispatcher);
    }

    [Fact]
    public async Task ServiceProviderAwareConfigureOverloads_ShouldBindHostOptionsAndExecuteAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new HostPoolSettings(workerCount: 2, queueCapacity: 8, maxOps: 4, prefix: "Engine"));
        services.AddSingleton<IExecutionSessionFactory<DiTestSession>, DiTestSessionFactory>();
        services.AddSingleton<IExecutionSessionFactory<DiSecondTestSession>, DiSecondTestSessionFactory>();

        services.AddExecutionWorker<DiTestSession>((sp, options) =>
        {
            var settings = sp.GetRequiredService<HostPoolSettings>();
            options.Name = settings.Prefix + "-Worker";
            options.QueueCapacity = settings.QueueCapacity;
            options.MaxOperationsPerSession = settings.MaxOps;
        });

        services.AddExecutionWorkerPool<DiSecondTestSession>((sp, options) =>
        {
            var settings = sp.GetRequiredService<HostPoolSettings>();
            options.WorkerCount = settings.WorkerCount;
            options.Name = settings.Prefix + "-Pool";
            options.QueueCapacity = settings.QueueCapacity;
        });

        await using var provider = services.BuildServiceProvider();
        var workerDispatcher = provider.GetRequiredService<IExecutionDispatcher<DiTestSession>>();
        var poolDispatcher = provider.GetRequiredService<IExecutionDispatcher<DiSecondTestSession>>();

        workerDispatcher.Name.Should().Be("Engine-Worker");
        poolDispatcher.Name.Should().Be("Engine-Pool");
        (await workerDispatcher.ExecuteAsync(static (s, _) => s.SessionId, cancellationToken: TestToken)).Should().BePositive();
        (await poolDispatcher.ExecuteAsync(static (s, _) => s.SessionId, cancellationToken: TestToken)).Should().BePositive();
    }

    [Fact]
    public async Task IndexedPoolOverload_WithServiceProviderConfigure_ShouldResolveSlotsAndOptionsAsync()
    {
        var slots = new List<int>();
        var services = new ServiceCollection();
        services.AddSingleton(new HostPoolSettings(workerCount: 2, queueCapacity: 4, maxOps: 0, prefix: "Indexed"));
        services.AddExecutionWorkerPool<DiTestSession>(
            (_, slot) =>
            {
                slots.Add(slot);
                return new DelegateExecutionSessionFactory<DiTestSession>(_ => new DiTestSession(500 + slot));
            },
            (sp, options) =>
            {
                var settings = sp.GetRequiredService<HostPoolSettings>();
                options.WorkerCount = settings.WorkerCount;
                options.Name = settings.Prefix;
                options.SchedulingStrategy = SchedulingStrategy.RoundRobin;
            });

        await using var provider = services.BuildServiceProvider();
        var pool = provider.GetRequiredService<IExecutionWorkerPool<DiTestSession>>();
        var first = await pool.ExecuteAsync(static (s, _) => s.SessionId, cancellationToken: TestToken);
        var second = await pool.ExecuteAsync(static (s, _) => s.SessionId, cancellationToken: TestToken);

        slots.Should().Equal(0, 1);
        new[] { first, second }.Should().BeEquivalentTo([500, 501]);
    }

    [Fact]
    public async Task DelegateRegistrationOverloads_ShouldCreateRecycleAndDisposeSessionsWithoutCustomFactoryClassAsync()
    {
        var workerDisposed = 0;
        var customPoolDisposed = 0;
        var services = new ServiceCollection();
        services.AddSingleton(new SeedOffset(10));

        services.AddExecutionWorker<DisposableSession>(
            (sp, ctx, _) =>
            {
                var seed = sp.GetRequiredService<SeedOffset>();
                return new DisposableSession(seed.BaseValue + (int)ctx.SessionGeneration, () => Interlocked.Increment(ref workerDisposed));
            },
            (_, options) => options.MaxOperationsPerSession = 1);

        await using (var provider = services.BuildServiceProvider())
        {
            var workerDispatcher = provider.GetRequiredService<IExecutionDispatcher<DisposableSession>>();
            (await workerDispatcher.ExecuteAsync(static (s, _) => s.Value, cancellationToken: TestToken)).Should().Be(10);
            (await workerDispatcher.ExecuteAsync(static (s, _) => s.Value, cancellationToken: TestToken)).Should().Be(11);
        }

        var unconfiguredPoolServices = new ServiceCollection();
        unconfiguredPoolServices.AddSingleton(new SeedOffset(10));
        unconfiguredPoolServices.AddExecutionWorkerPool<SecondDisposableSession>(
            (sp, ctx, _) =>
            {
                var seed = sp.GetRequiredService<SeedOffset>();
                return new SecondDisposableSession(seed.BaseValue + (ctx.WorkerIndex * 100) + (int)ctx.SessionGeneration);
            },
            disposeSession: (_, _) => Interlocked.Increment(ref customPoolDisposed));

        await using (var poolProvider = unconfiguredPoolServices.BuildServiceProvider())
        {
            var poolDispatcher = poolProvider.GetRequiredService<IExecutionDispatcher<SecondDisposableSession>>();
            (await poolDispatcher.ExecuteAsync(static (s, _) => s.Value, cancellationToken: TestToken)).Should().Be(10);
        }

        workerDisposed.Should().Be(2);
        customPoolDisposed.Should().Be(1);
    }

    [Fact]
    public async Task Validators_ShouldThrowOptionsValidationExceptionForInvalidWorkerAndPoolOptionsAsync()
    {
        var badWorkerServices = new ServiceCollection();
        badWorkerServices.AddSingleton<IExecutionSessionFactory<DiTestSession>, DiTestSessionFactory>();
        badWorkerServices.AddExecutionWorker<DiTestSession>((_, options) => options.QueueCapacity = -1);

        await using (var badWorkerProvider = badWorkerServices.BuildServiceProvider())
        {
            Action resolveWorker = () => _ = badWorkerProvider.GetRequiredService<IExecutionWorker<DiTestSession>>();
            resolveWorker.Should().Throw<OptionsValidationException>();

            var workerValidators = badWorkerProvider.GetServices<IValidateOptions<ExecutionWorkerOptions>>().ToArray();
            workerValidators.Should().ContainSingle();
            workerValidators[0].Validate(null, null!).Failed.Should().BeTrue();
        }

        var badPoolServices = new ServiceCollection();
        badPoolServices.AddSingleton<IExecutionSessionFactory<DiTestSession>, DiTestSessionFactory>();
        badPoolServices.AddExecutionWorkerPool<DiTestSession>((_, options) => options.MaxStackSize = -1);

        await using (var badPoolProvider = badPoolServices.BuildServiceProvider())
        {
            Action resolvePool = () => _ = badPoolProvider.GetRequiredService<IExecutionWorkerPool<DiTestSession>>();
            resolvePool.Should().Throw<OptionsValidationException>();

            var poolValidators = badPoolProvider.GetServices<IValidateOptions<ExecutionWorkerPoolOptions>>().ToArray();
            poolValidators.Should().ContainSingle();
            poolValidators[0].Validate(null, null!).Failed.Should().BeTrue();
        }
    }

    private sealed class SeedOffset(int baseValue)
    {
        public int BaseValue { get; } = baseValue;
    }

    private sealed class HostPoolSettings(int workerCount, int queueCapacity, int maxOps, string prefix)
    {
        public int WorkerCount { get; } = workerCount;

        public int QueueCapacity { get; } = queueCapacity;

        public int MaxOps { get; } = maxOps;

        public string Prefix { get; } = prefix;
    }

    private sealed class DisposableSession(int value, Action onDispose) : IDisposable
    {
        public int Value { get; } = value;

        public void Dispose() => onDispose?.Invoke();
    }

    private sealed class SecondDisposableSession(int value)
    {
        public int Value { get; } = value;
    }
}
