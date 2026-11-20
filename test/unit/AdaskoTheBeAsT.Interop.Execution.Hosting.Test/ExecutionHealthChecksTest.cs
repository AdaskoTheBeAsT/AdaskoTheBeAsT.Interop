using AdaskoTheBeAsT.Interop.Execution;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

#pragma warning disable CA2000, IDISP013, IDISP016, VSTHRD003, AsyncFixer04

namespace AdaskoTheBeAsT.Interop.Execution.Hosting.Test;

public sealed class ExecutionHealthChecksTest
{
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(10);

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OptionsAndConstructors_ShouldValidateArgumentsAndSnapshotIsolationAsync()
    {
        var defaultOptions = ExecutionHealthCheckOptions.Default;
        defaultOptions.QueueDepthWarningThreshold.Should().BeNull();

        await using var validWorker = new ExecutionWorker<HostedTestSession>(new HostedTestSessionFactory());
        var invalidOptions = new ExecutionHealthCheckOptions { QueueDepthWarningThreshold = -1 };
        Action invalidCtor = () => _ = new ExecutionWorkerHealthCheck<HostedTestSession>(validWorker, invalidOptions);
        invalidCtor.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("QueueDepthWarningThreshold");

        Action nullWorker = () => _ = new ExecutionWorkerHealthCheck<HostedTestSession>(null!);
        Action nullPool = () => _ = new ExecutionWorkerPoolHealthCheck<HostedTestSession>(null!);
        nullWorker.Should().Throw<ArgumentNullException>().WithParameterName("worker");
        nullPool.Should().Throw<ArgumentNullException>().WithParameterName("pool");

        const IHealthChecksBuilder? nullBuilder = null;
        Action nullBuilderWorker = () => nullBuilder!.AddExecutionWorkerHealthCheck<HostedTestSession>();
        Action nullBuilderPool = () => nullBuilder!.AddExecutionWorkerPoolHealthCheck<HostedTestSession>();
        nullBuilderWorker.Should().Throw<ArgumentNullException>().WithParameterName("builder");
        nullBuilderPool.Should().Throw<ArgumentNullException>().WithParameterName("builder");

        var services = new ServiceCollection();
        var builder = services.AddHealthChecks();
        Action whitespaceWorkerName = () => builder.AddExecutionWorkerHealthCheck<HostedTestSession>("  ");
        Action whitespacePoolName = () => builder.AddExecutionWorkerPoolHealthCheck<HostedTestSession>(string.Empty);
        whitespaceWorkerName.Should().Throw<ArgumentException>().WithParameterName("name");
        whitespacePoolName.Should().Throw<ArgumentException>().WithParameterName("name");
    }

    [Fact]
    public async Task HealthChecks_ShouldHonorPreCanceledTokenAsync()
    {
        await using var worker = new ExecutionWorker<HostedTestSession>(new HostedTestSessionFactory());
        await using var pool = new ExecutionWorkerPool<HostedTestSession>(
            new HostedTestSessionFactory(),
            new ExecutionWorkerPoolOptions(1));
        var workerCheck = new ExecutionWorkerHealthCheck<HostedTestSession>(worker);
        var poolCheck = new ExecutionWorkerPoolHealthCheck<HostedTestSession>(pool);

        using var cts = new CancellationTokenSource();
        await CancelAsync(cts);

        Func<Task<HealthCheckResult>> cancelWorker = () => workerCheck.CheckHealthAsync(new HealthCheckContext(), cts.Token);
        Func<Task<HealthCheckResult>> cancelPool = () => poolCheck.CheckHealthAsync(new HealthCheckContext(), cts.Token);

        await cancelWorker.Should().ThrowAsync<OperationCanceledException>();
        await cancelPool.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task WorkerHealthCheck_ShouldReportNotReadyDuringStartupAndHealthyAfterwardAsync()
    {
        using var enterCreate = new ManualResetEventSlim();
        using var releaseCreate = new ManualResetEventSlim();

        var factory = new ControllableHostedFactory(onCreate: () =>
        {
            enterCreate.Set();
            releaseCreate.Wait(GateTimeout, TestToken).Should().BeTrue();
        });

        await using var worker = new ExecutionWorker<HostedTestSession>(
            factory,
            new ExecutionWorkerOptions(name: "CalcWorker"));
        var check = new ExecutionWorkerHealthCheck<HostedTestSession>(worker);

        var beforeStart = await check.CheckHealthAsync(context: null!, TestToken);
        beforeStart.Status.Should().Be(HealthStatus.Unhealthy);
        beforeStart.Data["isInitialized"].Should().Be(false);
        beforeStart.Data["name"].Should().Be("CalcWorker");

        var initTask = worker.InitializeAsync(TestToken);
        try
        {
            enterCreate.Wait(GateTimeout, TestToken).Should().BeTrue();
            var duringStart = await check.CheckHealthAsync(new HealthCheckContext(), TestToken);
            duringStart.Status.Should().Be(HealthStatus.Unhealthy);
        }
        finally
        {
            releaseCreate.Set();
        }

        await initTask;
        var healthy = await check.CheckHealthAsync(new HealthCheckContext(), TestToken);
        healthy.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task WorkerHealthCheck_ShouldReportQueueWarningRuntimeFaultAndDisposedAsync()
    {
        using var enterWork = new ManualResetEventSlim();
        using var releaseWork = new ManualResetEventSlim();
        var worker = new ExecutionWorker<HostedTestSession>(
            new ControllableHostedFactory(throwOnRecycleDispose: true));
        var check = new ExecutionWorkerHealthCheck<HostedTestSession>(
            worker,
            new ExecutionHealthCheckOptions { QueueDepthWarningThreshold = 0 });

        await worker.InitializeAsync(TestToken);

        var runningWork = worker.ExecuteAsync(
            (_, _) =>
            {
                enterWork.Set();
                releaseWork.Wait(GateTimeout, TestToken).Should().BeTrue();
            },
            cancellationToken: TestToken);
        Task? queuedWork = null;
        try
        {
            enterWork.Wait(GateTimeout, TestToken).Should().BeTrue();
            queuedWork = worker.ExecuteAsync(static (_, _) => { }, cancellationToken: TestToken);

            var degraded = await check.CheckHealthAsync(new HealthCheckContext(), TestToken);
            degraded.Status.Should().Be(HealthStatus.Degraded);
            degraded.Data.ContainsKey("name").Should().BeFalse();
        }
        finally
        {
            releaseWork.Set();
        }

        await runningWork;
        await queuedWork;

        await TriggerWorkerRecycleFaultAsync(worker);
        var runtimeFault = await check.CheckHealthAsync(new HealthCheckContext(), TestToken);
        runtimeFault.Status.Should().Be(HealthStatus.Unhealthy);

        await worker.DisposeAsync();
        var unnamedDisposed = new ExecutionWorker<HostedTestSession>(new HostedTestSessionFactory());
        await unnamedDisposed.DisposeAsync();
        var disposed = await new ExecutionWorkerHealthCheck<HostedTestSession>(unnamedDisposed)
            .CheckHealthAsync(new HealthCheckContext(), TestToken);
        disposed.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task WorkerAndPoolHealthCheck_ShouldDetectStartupFaultsAsync()
    {
        var faultFactory = new ControllableHostedFactory(
            onCreate: static () => throw new InvalidOperationException("startup-boom"));

        await using var worker = new ExecutionWorker<HostedTestSession>(faultFactory);
        await using var pool = new ExecutionWorkerPool<HostedTestSession>(
            faultFactory,
            new ExecutionWorkerPoolOptions(2));

        Func<Task> initWorker = () => worker.InitializeAsync(TestToken);
        Func<Task> initPool = () => pool.InitializeAsync(TestToken);
        await initWorker.Should().ThrowAsync<InvalidOperationException>();
        await initPool.Should().ThrowAsync<InvalidOperationException>();

        var customContext = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration(
                "custom",
                new ExecutionWorkerHealthCheck<HostedTestSession>(worker),
                failureStatus: HealthStatus.Degraded,
                tags: null),
        };

        var workerResult = await new ExecutionWorkerHealthCheck<HostedTestSession>(worker)
            .CheckHealthAsync(customContext, TestToken);
        var poolResult = await new ExecutionWorkerPoolHealthCheck<HostedTestSession>(pool)
            .CheckHealthAsync(new HealthCheckContext(), TestToken);

        workerResult.Status.Should().Be(HealthStatus.Degraded);
        workerResult.Exception.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be("startup-boom");

        poolResult.Status.Should().Be(HealthStatus.Unhealthy);
        poolResult.Exception.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be("startup-boom");
    }

    [Fact]
    public async Task PoolHealthCheck_ShouldDetectNotReadyQueueWarningPartialLossTotalLossAndDisposedAsync()
    {
        using var enterWork = new ManualResetEventSlim();
        using var releaseWork = new ManualResetEventSlim();
        var slot0Factory = new ControllableHostedFactory(throwOnRecycleDispose: true);
        var slot1Factory = new ControllableHostedFactory(throwOnRecycleDispose: true);

        var pool = new ExecutionWorkerPool<HostedTestSession>(
            slot => slot == 0 ? slot0Factory : slot1Factory,
            new ExecutionWorkerPoolOptions(
                workerCount: 2,
                name: "CalcPool",
                schedulingStrategy: SchedulingStrategy.RoundRobin));
        var check = new ExecutionWorkerPoolHealthCheck<HostedTestSession>(
            pool,
            new ExecutionHealthCheckOptions { QueueDepthWarningThreshold = 0 });

        var notReady = await check.CheckHealthAsync(context: null!, TestToken);
        notReady.Status.Should().Be(HealthStatus.Unhealthy);
        notReady.Data["initializedWorkerCount"].Should().Be(0);

        await pool.InitializeAsync(TestToken);
        (await check.CheckHealthAsync(new HealthCheckContext(), TestToken)).Status.Should().Be(HealthStatus.Healthy);

        await VerifyPoolQueueDepthDegradedAsync(pool, check, enterWork, releaseWork);

        await TriggerPoolRecycleFaultAsync(pool);
        var partialLoss = await check.CheckHealthAsync(new HealthCheckContext(), TestToken);
        partialLoss.Status.Should().Be(HealthStatus.Degraded);
        partialLoss.Data["faultedWorkerCount"].Should().Be(1);
        partialLoss.Exception.Should().NotBeNull();

        await TriggerPoolRecycleFaultAsync(pool);
        var totalLoss = await check.CheckHealthAsync(new HealthCheckContext(), TestToken);
        totalLoss.Status.Should().Be(HealthStatus.Unhealthy);
        totalLoss.Data["faultedWorkerCount"].Should().Be(2);

        await pool.DisposeAsync();
        var disposed = await check.CheckHealthAsync(new HealthCheckContext(), TestToken);
        disposed.Status.Should().Be(HealthStatus.Unhealthy);
        disposed.Data["isDisposed"].Should().Be(true);
    }

    [Fact]
    public async Task AddHealthChecksIntegration_ShouldWireHostedServicesAndHealthCheckServiceEndToEndAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IExecutionSessionFactory<HostedTestSession>, HostedTestSessionFactory>();
        services.AddExecutionWorkerHostedService<HostedTestSession>();
        services.AddExecutionWorkerPoolHostedService<HostedTestSession>(options => options.WorkerCount = 2);
        services.AddHealthChecks()
            .AddExecutionWorkerHealthCheck<HostedTestSession>(
                configure: options => options.QueueDepthWarningThreshold = 10,
                tags: ["ready"])
            .AddExecutionWorkerPoolHealthCheck<HostedTestSession>(
                name: "custom_pool_ready",
                configure: options => options.QueueDepthWarningThreshold = 10,
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready"],
                timeout: TimeSpan.FromSeconds(5));

        await using var provider = services.BuildServiceProvider();
        var healthService = provider.GetRequiredService<HealthCheckService>();
        var hostedServices = provider.GetServices<IHostedService>().ToArray();

        var preStartReport = await healthService.CheckHealthAsync(TestToken);
        preStartReport.Status.Should().Be(HealthStatus.Unhealthy);
        preStartReport.Entries.Should().ContainKey("execution_worker_HostedTestSession");
        preStartReport.Entries.Should().ContainKey("custom_pool_ready");

        foreach (var hostedService in hostedServices)
        {
            await hostedService.StartAsync(TestToken);
        }

        var readyReport = await healthService.CheckHealthAsync(TestToken);
        readyReport.Status.Should().Be(HealthStatus.Healthy);

        foreach (var hostedService in hostedServices)
        {
            await hostedService.StopAsync(TestToken);
        }

        var postStopReport = await healthService.CheckHealthAsync(TestToken);
        postStopReport.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task PoolHealthCheck_ShouldHandleEmptyWorkerSnapshotWithoutFaultExceptionAsync()
    {
        var mockPool = new Mock<IExecutionWorkerPool<HostedTestSession>>(MockBehavior.Strict);
        mockPool.Setup(static p => p.GetSnapshot())
            .Returns(new ExecutionWorkerPoolSnapshot(name: null, workers: []));

        var check = new ExecutionWorkerPoolHealthCheck<HostedTestSession>(mockPool.Object);
        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestToken);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().BeNull();
        result.Data.ContainsKey("name").Should().BeFalse();
    }

    private static async Task VerifyPoolQueueDepthDegradedAsync(
        ExecutionWorkerPool<HostedTestSession> pool,
        ExecutionWorkerPoolHealthCheck<HostedTestSession> check,
        ManualResetEventSlim enterWork,
        ManualResetEventSlim releaseWork)
    {
        var blocker1 = pool.ExecuteAsync(
            (_, _) =>
            {
                enterWork.Set();
                releaseWork.Wait(GateTimeout, TestToken).Should().BeTrue();
            },
            cancellationToken: TestToken);
        var blocker2 = pool.ExecuteAsync(
            (_, _) => releaseWork.Wait(GateTimeout, TestToken).Should().BeTrue(),
            cancellationToken: TestToken);
        Task? queued = null;
        try
        {
            enterWork.Wait(GateTimeout, TestToken).Should().BeTrue();
            queued = pool.ExecuteAsync(static (_, _) => { }, cancellationToken: TestToken);
            var degraded = await check.CheckHealthAsync(new HealthCheckContext(), TestToken);
            degraded.Status.Should().Be(HealthStatus.Degraded);
        }
        finally
        {
            releaseWork.Set();
        }

        await Task.WhenAll(blocker1, blocker2, queued);
    }

    private static async Task TriggerWorkerRecycleFaultAsync(IExecutionWorker<HostedTestSession> worker)
    {
        Func<Task> fault = () => worker.ExecuteAsync(
            static (_, _) => throw new InvalidOperationException("op-fault"),
            new ExecutionRequestOptions(recycleSessionOnFailure: true),
            TestToken);
        var assertion = await fault.Should().ThrowAsync<InvalidOperationException>();
        assertion.Which.Message.Should().Be("op-fault");
    }

    private static async Task TriggerPoolRecycleFaultAsync(IExecutionWorkerPool<HostedTestSession> pool)
    {
        Func<Task> fault = () => pool.ExecuteAsync(
            static (_, _) => throw new InvalidOperationException("op-fault"),
            new ExecutionRequestOptions(recycleSessionOnFailure: true),
            TestToken);
        var assertion = await fault.Should().ThrowAsync<InvalidOperationException>();
        assertion.Which.Message.Should().Be("op-fault");
    }

    private static Task CancelAsync(CancellationTokenSource cts)
    {
#if NET8_0_OR_GREATER
        return cts.CancelAsync();
#else
        cts.Cancel();
        return Task.CompletedTask;
#endif
    }

    private sealed class ControllableHostedFactory(
        Action? onCreate = null,
        bool throwOnRecycleDispose = false) : IExecutionSessionFactory<HostedTestSession>
    {
        private int _count;

        public HostedTestSession CreateSession(CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref _count);
            onCreate?.Invoke();
            return new HostedTestSession(current);
        }

        public void DisposeSession(HostedTestSession session)
        {
            if (throwOnRecycleDispose)
            {
                throw new InvalidOperationException("recycle-dispose-fault");
            }
        }
    }
}
