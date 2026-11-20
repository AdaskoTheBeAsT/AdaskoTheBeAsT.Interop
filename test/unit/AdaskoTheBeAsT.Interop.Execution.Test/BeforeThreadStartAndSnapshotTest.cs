using System.Collections.Concurrent;
using AwesomeAssertions;
using Xunit;

#pragma warning disable IDISP013, VSTHRD003, AsyncFixer04

namespace AdaskoTheBeAsT.Interop.Execution.Test;

public sealed class BeforeThreadStartAndSnapshotTest
{
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(10);

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public void WorkerAndPoolOptions_ShouldSnapshotBeforeThreadStart()
    {
        var count = 0;
        void Callback() => Interlocked.Increment(ref count);

        var workerOptions = new ExecutionWorkerOptions { BeforeThreadStart = Callback };
        var poolOptions = new ExecutionWorkerPoolOptions(2) { BeforeThreadStart = Callback };

        workerOptions.Snapshot().BeforeThreadStart!.Invoke();
        poolOptions.Snapshot().BeforeThreadStart!.Invoke();
        count.Should().Be(2);
    }

    [Fact]
    public void WorkerAndPoolSnapshots_ShouldExposeReadinessDisposedAndCounts()
    {
        var legacyWorker = new ExecutionWorkerSnapshot("legacy", 1, isFaulted: false, fault: null);
        legacyWorker.IsInitialized.Should().BeFalse();
        legacyWorker.IsDisposed.Should().BeFalse();

        var readyWorker = new ExecutionWorkerSnapshot("w0", 2, isFaulted: false, fault: null, isInitialized: true, isDisposed: false);
        var faultedWorker = new ExecutionWorkerSnapshot("w1", 0, isFaulted: true, new InvalidOperationException("err"), isInitialized: false, isDisposed: true);

        var legacyPool = new ExecutionWorkerPoolSnapshot("legacy-pool", [readyWorker]);
        legacyPool.IsDisposed.Should().BeFalse();
        legacyPool.IsInitialized.Should().BeTrue();
        legacyPool.InitializedWorkerCount.Should().Be(1);
        legacyPool.FaultedWorkerCount.Should().Be(0);

        var mixedPool = new ExecutionWorkerPoolSnapshot("pool", [readyWorker, faultedWorker], isDisposed: true);
        mixedPool.IsDisposed.Should().BeTrue();
        mixedPool.IsInitialized.Should().BeFalse();
        mixedPool.InitializedWorkerCount.Should().Be(1);
        mixedPool.FaultedWorkerCount.Should().Be(1);
        mixedPool.QueueDepth.Should().Be(2);

        var emptyPool = new ExecutionWorkerPoolSnapshot("empty", null!, isDisposed: false);
        emptyPool.Workers.Should().BeEmpty();
        emptyPool.IsInitialized.Should().BeFalse();
    }

    [Fact]
    public async Task Worker_BeforeThreadStart_ShouldRunOnCallerBeforeStaCreateSessionAsync()
    {
        var events = new ConcurrentQueue<(string Stage, int ThreadId)>();
        var callerThreadId = Environment.CurrentManagedThreadId;
        var callbackCount = 0;

        var factory = new DelegateSessionFactory(onCreate: () =>
        {
            events.Enqueue(("create", Environment.CurrentManagedThreadId));
        });
        var options = new ExecutionWorkerOptions(name: "OrderedWorker", useStaThread: true)
        {
            BeforeThreadStart = () =>
            {
                Interlocked.Increment(ref callbackCount);
                events.Enqueue(("before", Environment.CurrentManagedThreadId));
            },
        };

        await using var worker = new ExecutionWorker<DummySession>(factory, options);
        worker.GetSnapshot().IsInitialized.Should().BeFalse();

        await worker.InitializeAsync(TestToken);
        await worker.InitializeAsync(TestToken);
        _ = await worker.ExecuteAsync(static (s, _) => s.Id, cancellationToken: TestToken);

        callbackCount.Should().Be(1);
        var recorded = events.ToArray();
        recorded.Should().HaveCount(2);
        recorded[0].Stage.Should().Be("before");
        recorded[0].ThreadId.Should().Be(callerThreadId);
        recorded[1].Stage.Should().Be("create");
        recorded[1].ThreadId.Should().NotBe(callerThreadId);
        worker.GetSnapshot().IsInitialized.Should().BeTrue();
    }

    [Fact]
    public async Task Worker_BeforeThreadStartFailure_ShouldFaultWithoutCreatingSessionAsync()
    {
        var factory = new DelegateSessionFactory();
        var options = new ExecutionWorkerOptions
        {
            BeforeThreadStart = static () => throw new InvalidOperationException("com-prep-failed"),
        };

        await using var worker = new ExecutionWorker<DummySession>(factory, options);
        Func<Task> init = () => worker.InitializeAsync(TestToken);

        await init.Should().ThrowAsync<InvalidOperationException>().WithMessage("com-prep-failed");
        factory.CreateCount.Should().Be(0);
        worker.IsFaulted.Should().BeTrue();
        worker.GetSnapshot().IsInitialized.Should().BeFalse();
    }

    [Fact]
    public async Task Pool_BeforeThreadStart_ShouldRunOnceOnCallerBeforeAllParallelWorkerSessionsAsync()
    {
        const int workerCount = 3;
        var callbackCount = 0;
        var callerThreadId = Environment.CurrentManagedThreadId;
        var beforeCompleted = 0;
        using var parallelBarrier = new Barrier(workerCount);

        var factory = new DelegateSessionFactory(onCreate: () =>
        {
            Volatile.Read(ref beforeCompleted).Should().Be(1);
            parallelBarrier.SignalAndWait(GateTimeout, TestToken).Should().BeTrue();
        });
        var options = new ExecutionWorkerPoolOptions(workerCount, name: "PrepPool", useStaThread: true)
        {
            BeforeThreadStart = () =>
            {
                Environment.CurrentManagedThreadId.Should().Be(callerThreadId);
                Interlocked.Increment(ref callbackCount);
                Volatile.Write(ref beforeCompleted, 1);
            },
        };

        await using var pool = new ExecutionWorkerPool<DummySession>(factory, options);
        pool.GetSnapshot().IsInitialized.Should().BeFalse();

        await pool.InitializeAsync(TestToken);

        callbackCount.Should().Be(1);
        factory.CreateCount.Should().Be(workerCount);
        var snapshot = pool.GetSnapshot();
        snapshot.IsInitialized.Should().BeTrue();
        snapshot.InitializedWorkerCount.Should().Be(workerCount);
        snapshot.IsDisposed.Should().BeFalse();
    }

    [Fact]
    public async Task Pool_BeforeThreadStartFailure_ShouldFaultPoolStartupWithoutStartingWorkersAsync()
    {
        var factory = new DelegateSessionFactory();
        var options = new ExecutionWorkerPoolOptions(3)
        {
            BeforeThreadStart = static () => throw new InvalidOperationException("nano-prep-failed"),
        };

        await using var pool = new ExecutionWorkerPool<DummySession>(factory, options);
        Func<Task> init = () => pool.InitializeAsync(TestToken);

        await init.Should().ThrowAsync<InvalidOperationException>().WithMessage("nano-prep-failed");
        factory.CreateCount.Should().Be(0);
        pool.IsAnyFaulted.Should().BeTrue();
        var snapshot = pool.GetSnapshot();
        snapshot.FaultedWorkerCount.Should().Be(3);
        snapshot.InitializedWorkerCount.Should().Be(0);
    }

    [Fact]
    public async Task Regression_Ex02_And_Ex05_StartupReadinessAndQueueCapacityDuringStartupAsync()
    {
        using var enteringCreate = new ManualResetEventSlim();
        using var releaseCreate = new ManualResetEventSlim();
        var factory = new DelegateSessionFactory(onCreate: () =>
        {
            enteringCreate.Set();
            releaseCreate.Wait(GateTimeout, TestToken).Should().BeTrue();
        });

        await using var worker = new ExecutionWorker<DummySession>(
            factory,
            new ExecutionWorkerOptions(name: "BoundedStartup") { QueueCapacity = 2 });

        var initTask = worker.InitializeAsync(TestToken);
        Task<int>? firstTask = null;
        Task<int>? secondTask = null;
        try
        {
            enteringCreate.Wait(GateTimeout, TestToken).Should().BeTrue();
            worker.GetSnapshot().IsInitialized.Should().BeFalse();
            initTask.IsCompleted.Should().BeFalse();

            firstTask = worker.ExecuteAsync(static (s, _) => s.Id, cancellationToken: TestToken);
            secondTask = worker.ExecuteAsync(static (s, _) => s.Id + 10, cancellationToken: TestToken);
            var overflowTask = worker.ExecuteAsync(static (s, _) => s.Id, cancellationToken: TestToken);

            Func<Task<int>> awaitOverflow = () => overflowTask;
            await awaitOverflow.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*admission capacity is exhausted*");
        }
        finally
        {
            releaseCreate.Set();
        }

        await initTask;
        worker.GetSnapshot().IsInitialized.Should().BeTrue();
        (await firstTask).Should().Be(1);
        (await secondTask).Should().Be(11);
        worker.QueueDepth.Should().Be(0);
    }

    private sealed class DummySession(int id)
    {
        public int Id { get; } = id;
    }

    private sealed class DelegateSessionFactory(Action? onCreate = null) : IExecutionSessionFactory<DummySession>
    {
        private int _createCount;

        public int CreateCount => Volatile.Read(ref _createCount);

        public DummySession CreateSession(CancellationToken cancellationToken)
        {
            var id = Interlocked.Increment(ref _createCount);
            onCreate?.Invoke();
            return new DummySession(id);
        }

        public void DisposeSession(DummySession session)
        {
        }
    }
}
