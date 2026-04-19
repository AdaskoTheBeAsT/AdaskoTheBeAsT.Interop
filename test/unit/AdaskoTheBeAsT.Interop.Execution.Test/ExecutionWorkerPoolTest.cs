using System.Collections.Concurrent;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Execution.Test;

public sealed class ExecutionWorkerPoolTest
{
    [Fact]
    public void Constructor_ShouldThrowWhenSessionFactoryFactoryIsNull()
    {
        const Func<int, IExecutionSessionFactory<PoolSession>>? sessionFactoryFactory = null;
        var action = () =>
        {
            using var ignored = new ExecutionWorkerPool<PoolSession>(sessionFactoryFactory!, new ExecutionWorkerPoolOptions(1));
        };

        action.Should().Throw<ArgumentNullException>().WithParameterName(nameof(sessionFactoryFactory));
    }

    [Fact]
    public void Constructor_ShouldThrowWhenOptionsAreNull()
    {
        const ExecutionWorkerPoolOptions? options = null;
        var action = () =>
        {
            using var ignored = new ExecutionWorkerPool<PoolSession>(
                static workerIndex => throw new InvalidOperationException(workerIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                options!);
        };

        action.Should().Throw<ArgumentNullException>().WithParameterName(nameof(options));
    }

    [Fact]
    public void Constructor_ShouldThrowWhenSessionFactoryFactoryReturnsNull()
    {
#pragma warning disable SA1313 // Parameter names should begin with lower-case letter
        static IExecutionSessionFactory<PoolSession> SessionFactoryFactory(int _) => null!;
#pragma warning restore SA1313 // Parameter names should begin with lower-case letter
        var action = () =>
        {
            using var ignored = new ExecutionWorkerPool<PoolSession>(SessionFactoryFactory, new ExecutionWorkerPoolOptions(1));
        };

        action.Should().Throw<InvalidOperationException>().WithMessage("The session factory factory returned null.");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldExecuteActionDelegateAsync()
    {
        var tracker = new PoolSessionTracker();
        var executedWorkerIndices = new ConcurrentBag<int>();

        using var workerPool = CreateWorkerPool(1, tracker);
        await workerPool.ExecuteAsync(
            (session, _) => executedWorkerIndices.Add(session.WorkerIndex),
            cancellationToken: CancellationToken.None);

        executedWorkerIndices.Should().ContainSingle();
        executedWorkerIndices.Single().Should().Be(0);
        tracker.GetCreateCount(0).Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldDistributeWorkAcrossMultipleDedicatedThreadsAsync()
    {
        var tracker = new PoolSessionTracker();

        using var workerPool = CreateWorkerPool(3, tracker);
        var results = await Task.WhenAll(
            Enumerable.Range(0, 9)
                .Select(
                    _ => workerPool.ExecuteAsync(
                        static (session, _) =>
                            (session.WorkerIndex, session.SessionSequence, ManagedThreadId: Environment.CurrentManagedThreadId, session.OwnerThreadId),
                        cancellationToken: CancellationToken.None)));

        var workerIndices = results.Select(static result => result.WorkerIndex).Distinct().ToArray();
        Array.Sort(workerIndices);

        workerIndices.Should().Equal(0, 1, 2);
        results.Select(static result => result.ManagedThreadId).Distinct().Should().HaveCount(3);
        results.Should().OnlyContain(static result => result.OwnerThreadId == result.ManagedThreadId);
        tracker.GetCreateCount(0).Should().Be(1);
        tracker.GetCreateCount(1).Should().Be(1);
        tracker.GetCreateCount(2).Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRecycleOnlyTheFailedWorkerSessionAsync()
    {
        var tracker = new PoolSessionTracker();

        await ExecuteScenarioAsync();
        tracker.GetCreateCount(0).Should().Be(2);
        tracker.GetCreateCount(1).Should().Be(1);
        tracker.GetDisposeCount(0).Should().Be(2);
        tracker.GetDisposeCount(1).Should().Be(1);

        async Task ExecuteScenarioAsync()
        {
            using var workerPool = CreateWorkerPool(2, tracker);
            var firstWorker = await workerPool.ExecuteAsync(
                static (session, _) => (session.WorkerIndex, session.SessionSequence),
                cancellationToken: CancellationToken.None);
            var secondWorker = await workerPool.ExecuteAsync(
                static (session, _) => (session.WorkerIndex, session.SessionSequence),
                cancellationToken: CancellationToken.None);

            Func<Task> action = async () => await workerPool.ExecuteAsync<int>(
                static (session, _) =>
                {
                    session.WorkerIndex.Should().Be(0);
                    throw new InvalidOperationException("boom");
                },
                new ExecutionRequestOptions(recycleSessionOnFailure: true),
                CancellationToken.None);
            await action.Should().ThrowAsync<InvalidOperationException>();

            var thirdWorker = await workerPool.ExecuteAsync(
                static (session, _) => (session.WorkerIndex, session.SessionSequence),
                cancellationToken: CancellationToken.None);
            var fourthWorker = await workerPool.ExecuteAsync(
                static (session, _) => (session.WorkerIndex, session.SessionSequence),
                cancellationToken: CancellationToken.None);

            firstWorker.Should().Be((0, 1));
            secondWorker.Should().Be((1, 1));
            thirdWorker.Should().Be((1, 1));
            fourthWorker.Should().Be((0, 2));
        }
    }

    [Fact]
    public void Initialize_ShouldDisposeAlreadyStartedWorkersWhenOneWorkerFailsToStart()
    {
        var tracker = new PoolSessionTracker();
        using var worker0Started = new ManualResetEventSlim(initialState: false);
        using var worker2Started = new ManualResetEventSlim(initialState: false);

        // With parallel pool initialization every worker's CreateSession runs on its own dedicated
        // thread concurrently. Gate worker 1's failure behind worker 0 and worker 2 both having
        // entered CreateSession, so the resulting CreateCount/DisposeCount observations are
        // deterministic regardless of OS thread scheduling.
        using var workerPool = new ExecutionWorkerPool<PoolSession>(
            workerIndex => new IndexedTrackingSessionFactory(
                workerIndex,
                tracker,
                failOnCreate: workerIndex == 1,
                onCreate: () =>
                {
                    if (workerIndex == 0)
                    {
                        worker0Started.Set();
                    }
                    else if (workerIndex == 2)
                    {
                        worker2Started.Set();
                    }
                    else if (workerIndex == 1)
                    {
                        worker0Started.Wait(TimeSpan.FromSeconds(5));
                        worker2Started.Wait(TimeSpan.FromSeconds(5));
                    }
                }),
            new ExecutionWorkerPoolOptions(3, "Execution Worker Pool"));

        var action = workerPool.Initialize;

        action.Should().Throw<InvalidOperationException>().WithMessage("boom");
        tracker.GetCreateCount(0).Should().Be(1);
        tracker.GetDisposeCount(0).Should().Be(1);
        tracker.GetCreateCount(1).Should().Be(1);
        tracker.GetDisposeCount(1).Should().Be(0);
        tracker.GetCreateCount(2).Should().Be(1);
        tracker.GetDisposeCount(2).Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldUseDefaultWorkerNamingWhenPoolNameIsWhitespaceAsync()
    {
        var tracker = new PoolSessionTracker();

        using var workerPool = new ExecutionWorkerPool<PoolSession>(
            workerIndex => new IndexedTrackingSessionFactory(workerIndex, tracker),
            new ExecutionWorkerPoolOptions(1, " "));

        var workerIndex = await workerPool.ExecuteAsync(
            static (session, _) => session.WorkerIndex,
            cancellationToken: CancellationToken.None);

        workerIndex.Should().Be(0);
        tracker.GetCreateCount(0).Should().Be(1);
    }

    [Fact]
    public void Initialize_ShouldInitializeAllWorkers()
    {
        var tracker = new PoolSessionTracker();

        InitializeWorkerPool();
        tracker.GetDisposeCount(0).Should().Be(1);
        tracker.GetDisposeCount(1).Should().Be(1);
        tracker.GetDisposeCount(2).Should().Be(1);
        tracker.GetDisposeCount(3).Should().Be(1);

        void InitializeWorkerPool()
        {
            using var workerPool = CreateWorkerPool(4, tracker);
            workerPool.Initialize();

            workerPool.WorkerCount.Should().Be(4);
            tracker.GetCreateCount(0).Should().Be(1);
            tracker.GetCreateCount(1).Should().Be(1);
            tracker.GetCreateCount(2).Should().Be(1);
            tracker.GetCreateCount(3).Should().Be(1);
        }
    }

    [Fact]
    public void Initialize_ShouldIgnoreWorkerDisposeFailuresDuringCleanup()
    {
        var tracker = new PoolSessionTracker();
        ExecutionWorkerPool<PoolSession>? workerPool = null;

        workerPool = new ExecutionWorkerPool<PoolSession>(
            workerIndex => new IndexedTrackingSessionFactory(
                workerIndex,
                tracker,
                failOnCreate: workerIndex == 1,
                onCreate: workerIndex == 1 ? () => PoisonFirstWorkerThread(workerPool!) : null),
            new ExecutionWorkerPoolOptions(2, "Execution Worker Pool"));

        var action = workerPool.Initialize;

        action.Should().Throw<InvalidOperationException>().WithMessage("boom");
        tracker.GetCreateCount(0).Should().Be(1);
        tracker.GetCreateCount(1).Should().Be(1);
        workerPool.Dispose();
    }

    [Fact]
    public void ExecuteAsync_ShouldThrowWhenPoolIsDisposed()
    {
        var tracker = new PoolSessionTracker();
        using var workerPool = CreateWorkerPool(1, tracker);
        workerPool.MarkDisposedForTesting();

        Action action = () => _ = workerPool.ExecuteAsync(
            static (session, _) => session.WorkerIndex,
            cancellationToken: CancellationToken.None);

        action.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void ExecuteAsync_ShouldThrowObjectDisposedExceptionAfterDispose()
    {
        // IDISP016/IDISP017 disabled: this test is specifically verifying the
        // post-Dispose() behavioural contract of ExecuteAsync, so we deliberately
        // hold and invoke the pool after disposal instead of using a
        // using-declaration.
#pragma warning disable IDISP016, IDISP017
        var tracker = new PoolSessionTracker();
        var workerPool = CreateWorkerPool(2, tracker);
        workerPool.Dispose();

        Action action = () => _ = workerPool.ExecuteAsync(
            static (session, _) => session.WorkerIndex,
            cancellationToken: CancellationToken.None);
#pragma warning restore IDISP016, IDISP017

        action.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldThrowObjectDisposedExceptionAfterDisposeAsyncAsync()
    {
        // IDISP016/IDISP017 disabled: this test is specifically verifying the
        // post-DisposeAsync behavioural contract of ExecuteAsync, so the pool
        // is intentionally constructed without a using-declaration and used
        // after disposal.
#pragma warning disable IDISP016, IDISP017
        var tracker = new PoolSessionTracker();
        var workerPool = CreateWorkerPool(2, tracker);
        await workerPool.DisposeAsync();

        Action action = () => _ = workerPool.ExecuteAsync(
            static (session, _) => session.WorkerIndex,
            cancellationToken: CancellationToken.None);
#pragma warning restore IDISP016, IDISP017

        action.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldHandleHighConcurrencyStressSubmissionsAsync()
    {
        const int SubmissionCount = 1_000;
        const int WorkerCount = 4;
        var tracker = new PoolSessionTracker();
        await using var workerPool = CreateWorkerPool(WorkerCount, tracker);

        var submissionTasks = new Task<int>[SubmissionCount];
        Parallel.For(
            0,
            SubmissionCount,
            index => submissionTasks[index] = workerPool.ExecuteAsync(
                static (session, _) => session.WorkerIndex,
                cancellationToken: CancellationToken.None));

        var allTask = Task.WhenAll(submissionTasks);
        var completed = await Task.WhenAny(allTask, Task.Delay(TimeSpan.FromSeconds(30), CancellationToken.None));
        completed.Should().BeSameAs(allTask);

        var results = await allTask;
        results.Should().HaveCount(SubmissionCount);
        results.Should().OnlyContain(static workerIndex => workerIndex >= 0 && workerIndex < WorkerCount);

        var totalCreates = 0;
        for (var workerIndex = 0; workerIndex < WorkerCount; workerIndex++)
        {
            totalCreates += tracker.GetCreateCount(workerIndex);
        }

        totalCreates.Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(WorkerCount);
    }

    [Fact]
    public async Task QueueDepth_ShouldAggregateAcrossWorkersAsync()
    {
        var tracker = new PoolSessionTracker();
        var enteredActions = new ConcurrentBag<TaskCompletionSource<object?>>();
        using var releaseWorkers = new ManualResetEventSlim();

        await using var workerPool = new ExecutionWorkerPool<PoolSession>(
            workerIndex => new IndexedTrackingSessionFactory(workerIndex, tracker),
            new ExecutionWorkerPoolOptions(2, "Execution Worker Pool"));

        workerPool.QueueDepth.Should().Be(0);

        // AsyncFixer04 disabled: these tasks are stored in local arrays and awaited
        // together via Task.WhenAll further down before the 'await using' disposes
        // the pool. The analyzer does not track the lifetime through array indexing.
#pragma warning disable AsyncFixer04
        var blockingTasks = new Task[2];
        for (var i = 0; i < blockingTasks.Length; i++)
        {
            var enteredGate = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            enteredActions.Add(enteredGate);
            blockingTasks[i] = workerPool.ExecuteAsync(
                (_, cancellationToken) =>
                {
                    enteredGate.TrySetResult(null);
                    releaseWorkers.Wait(cancellationToken);
                },
                cancellationToken: CancellationToken.None);
        }

        await Task.WhenAll(enteredActions.Select(static gate => gate.Task));

        var queued = new Task[4];
        for (var i = 0; i < queued.Length; i++)
        {
            queued[i] = workerPool.ExecuteAsync(
                static (_, _) => { },
                cancellationToken: CancellationToken.None);
        }
#pragma warning restore AsyncFixer04

        await WaitUntilAsync(() => workerPool.QueueDepth == 4);
        workerPool.QueueDepth.Should().Be(4);

        releaseWorkers.Set();
        await Task.WhenAll(blockingTasks);
        await Task.WhenAll(queued);

        await WaitUntilAsync(() => workerPool.QueueDepth == 0);
        workerPool.QueueDepth.Should().Be(0);
    }

    [Fact]
    public async Task WorkerFaulted_PoolShouldForwardPerWorkerEventAsync()
    {
        var tracker = new PoolSessionTracker();
        await using var workerPool = new ExecutionWorkerPool<PoolSession>(
            workerIndex => new IndexedTrackingSessionFactory(
                workerIndex,
                tracker,
                failOnDispose: true),
            new ExecutionWorkerPoolOptions(2, "Pool"));

        var raised = new ConcurrentBag<WorkerFaultedEventArgs>();
        workerPool.WorkerFaulted += (_, args) => raised.Add(args);

        Func<Task> action = async () => await workerPool.ExecuteAsync<int>(
            static (_, _) => throw new InvalidOperationException("boom"),
            new ExecutionRequestOptions(recycleSessionOnFailure: true),
            CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        await WaitUntilAsync(() => workerPool.IsAnyFaulted);

        workerPool.IsAnyFaulted.Should().BeTrue();
        raised.Should().ContainSingle();
        raised.Single().Exception.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("dispose boom");
        workerPool.WorkerFaults.Should().Contain(static fault => fault is InvalidOperationException);
    }

    [Fact]
    public async Task LeastQueued_ShouldRouteToWorkerWithSmallestQueueAsync()
    {
        var tracker = new PoolSessionTracker();
        using var releaseWorker0 = new ManualResetEventSlim();
        var worker0Entered = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var workerPool = new ExecutionWorkerPool<PoolSession>(
            workerIndex => new IndexedTrackingSessionFactory(workerIndex, tracker),
            new ExecutionWorkerPoolOptions(2, "Pool", schedulingStrategy: SchedulingStrategy.LeastQueued));

        // Park worker 0 on a blocking task so its queue stays non-empty.
#pragma warning disable AsyncFixer04
        var blocker = workerPool.ExecuteAsync(
            (session, cancellationToken) =>
            {
                session.WorkerIndex.Should().Be(0);
                worker0Entered.TrySetResult(null);
                releaseWorker0.Wait(cancellationToken);
            },
            cancellationToken: CancellationToken.None);
#pragma warning restore AsyncFixer04

        await worker0Entered.Task;

        // Submit a follow-up: least-queued should pick worker 1 (queue depth 0).
        var pickedWorker = await workerPool.ExecuteAsync(
            static (session, _) => session.WorkerIndex,
            cancellationToken: CancellationToken.None);

        pickedWorker.Should().Be(1);

        releaseWorker0.Set();
        await blocker;
    }

    [Fact]
    public async Task LeastQueued_TiesShouldFallBackToRoundRobinAsync()
    {
        var tracker = new PoolSessionTracker();

        await using var workerPool = new ExecutionWorkerPool<PoolSession>(
            workerIndex => new IndexedTrackingSessionFactory(workerIndex, tracker),
            new ExecutionWorkerPoolOptions(3, "Pool", schedulingStrategy: SchedulingStrategy.LeastQueued));

        // Prime all workers so their sessions are created and steady-state queue depth is 0.
        for (var i = 0; i < 3; i++)
        {
            _ = await workerPool.ExecuteAsync(
                static (session, _) => session.WorkerIndex,
                cancellationToken: CancellationToken.None);
        }

        // All queues now idle (depth 0). Submit N * 3 sequential requests and assert
        // each worker gets exactly N of them.
        const int CycleCount = 10;
        var counts = new int[3];
        for (var i = 0; i < CycleCount * 3; i++)
        {
            var workerIndex = await workerPool.ExecuteAsync(
                static (session, _) => session.WorkerIndex,
                cancellationToken: CancellationToken.None);
            counts[workerIndex]++;
        }

        counts[0].Should().Be(CycleCount);
        counts[1].Should().Be(CycleCount);
        counts[2].Should().Be(CycleCount);
    }

    [Fact]
    public async Task LeastQueued_ShouldSkipFaultedWorkerAsync()
    {
        var tracker = new PoolSessionTracker();

        await using var workerPool = new ExecutionWorkerPool<PoolSession>(
            workerIndex => new IndexedTrackingSessionFactory(
                workerIndex,
                tracker,
                failOnDispose: workerIndex == 0),
            new ExecutionWorkerPoolOptions(2, "Pool", schedulingStrategy: SchedulingStrategy.LeastQueued));

        // Poison worker 0: force it into the faulted state via recycle-on-failure.
        Func<Task> faultAction = async () => await workerPool.ExecuteAsync<int>(
            static (session, _) =>
            {
                session.WorkerIndex.Should().Be(0);
                throw new InvalidOperationException("boom");
            },
            new ExecutionRequestOptions(recycleSessionOnFailure: true),
            CancellationToken.None);

        await faultAction.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        await WaitUntilAsync(() => workerPool.WorkerFaults[0] is not null);

        // Every subsequent submission must route to worker 1.
        for (var i = 0; i < 5; i++)
        {
            var workerIndex = await workerPool.ExecuteAsync(
                static (session, _) => session.WorkerIndex,
                cancellationToken: CancellationToken.None);
            workerIndex.Should().Be(1);
        }
    }

    [Fact]
    public async Task RoundRobin_ShouldSkipFaultedWorkerAsync()
    {
        var tracker = new PoolSessionTracker();

        await using var workerPool = new ExecutionWorkerPool<PoolSession>(
            workerIndex => new IndexedTrackingSessionFactory(
                workerIndex,
                tracker,
                failOnDispose: workerIndex == 0),
            new ExecutionWorkerPoolOptions(2, "Pool", schedulingStrategy: SchedulingStrategy.RoundRobin));

        Func<Task> faultAction = async () => await workerPool.ExecuteAsync<int>(
            static (session, _) =>
            {
                session.WorkerIndex.Should().Be(0);
                throw new InvalidOperationException("boom");
            },
            new ExecutionRequestOptions(recycleSessionOnFailure: true),
            CancellationToken.None);

        await faultAction.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        await WaitUntilAsync(() => workerPool.WorkerFaults[0] is not null);

        for (var i = 0; i < 5; i++)
        {
            var workerIndex = await workerPool.ExecuteAsync(
                static (session, _) => session.WorkerIndex,
                cancellationToken: CancellationToken.None);
            workerIndex.Should().Be(1);
        }
    }

    [Fact]
    public async Task RoundRobin_ShouldDistributeEvenlyAsync()
    {
        var tracker = new PoolSessionTracker();

        await using var workerPool = new ExecutionWorkerPool<PoolSession>(
            workerIndex => new IndexedTrackingSessionFactory(workerIndex, tracker),
            new ExecutionWorkerPoolOptions(3, "Pool", schedulingStrategy: SchedulingStrategy.RoundRobin));

        const int CycleCount = 8;
        var counts = new int[3];
        for (var i = 0; i < CycleCount * 3; i++)
        {
            var workerIndex = await workerPool.ExecuteAsync(
                static (session, _) => session.WorkerIndex,
                cancellationToken: CancellationToken.None);
            counts[workerIndex]++;
        }

        counts[0].Should().Be(CycleCount);
        counts[1].Should().Be(CycleCount);
        counts[2].Should().Be(CycleCount);
    }

    [Fact]
    public void Constructor_ShouldThrowWhenSchedulingStrategyIsOutOfRange()
    {
        const SchedulingStrategy schedulingStrategy = (SchedulingStrategy)99;
        var action = () => new ExecutionWorkerPoolOptions(1, schedulingStrategy: schedulingStrategy);

        action.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be(nameof(ExecutionWorkerPoolOptions.SchedulingStrategy));
    }

    [Fact]
    public async Task CustomScheduler_ShouldReceiveWorkerSnapshotAndRouteSubmissionAsync()
    {
        var tracker = new PoolSessionTracker();
        var scheduler = new RecordingScheduler();

        await using var workerPool = new ExecutionWorkerPool<PoolSession>(
            workerIndex => new IndexedTrackingSessionFactory(workerIndex, tracker),
            new ExecutionWorkerPoolOptions(3, "Pool"),
            scheduler);

        _ = await workerPool.ExecuteAsync(
            static (session, _) => session.WorkerIndex,
            cancellationToken: CancellationToken.None);

        scheduler.InvocationCount.Should().Be(1);
        scheduler.LastSnapshotCount.Should().Be(3);
        scheduler.LastSelected.Should().NotBeNull();
    }

    [Fact]
    public async Task CustomScheduler_ShouldRouteAllSubmissionsToTargetedWorkerAsync()
    {
        var tracker = new PoolSessionTracker();
        var scheduler = new FixedIndexScheduler(targetIndex: 2);

        await using var workerPool = new ExecutionWorkerPool<PoolSession>(
            workerIndex => new IndexedTrackingSessionFactory(workerIndex, tracker),
            new ExecutionWorkerPoolOptions(3, "Pool"),
            scheduler);

        for (var i = 0; i < 4; i++)
        {
            var workerIndex = await workerPool.ExecuteAsync(
                static (session, _) => session.WorkerIndex,
                cancellationToken: CancellationToken.None);
            workerIndex.Should().Be(2);
        }

        tracker.GetCreateCount(0).Should().Be(0);
        tracker.GetCreateCount(1).Should().Be(0);
        tracker.GetCreateCount(2).Should().Be(1);
    }

    [Fact]
    public void RoundRobinWorkerScheduler_ShouldThrowWhenWorkersListIsNull()
    {
        var scheduler = new RoundRobinWorkerScheduler<PoolSession>();

        var action = () => scheduler.SelectWorker(null!);

        action.Should().Throw<ArgumentNullException>().WithParameterName("workers");
    }

    [Fact]
    public void LeastQueuedWorkerScheduler_ShouldThrowWhenWorkersListIsNull()
    {
        var scheduler = new LeastQueuedWorkerScheduler<PoolSession>();

        var action = () => scheduler.SelectWorker(null!);

        action.Should().Throw<ArgumentNullException>().WithParameterName("workers");
    }

    [Fact]
    public void RoundRobinWorkerScheduler_ShouldThrowWhenWorkersListIsEmpty()
    {
        var scheduler = new RoundRobinWorkerScheduler<PoolSession>();

        var action = () => scheduler.SelectWorker(Array.Empty<IExecutionWorker<PoolSession>>());

        action.Should().Throw<ArgumentException>().WithParameterName("workers");
    }

    [Fact]
    public void LeastQueuedWorkerScheduler_ShouldThrowWhenWorkersListIsEmpty()
    {
        var scheduler = new LeastQueuedWorkerScheduler<PoolSession>();

        var action = () => scheduler.SelectWorker(Array.Empty<IExecutionWorker<PoolSession>>());

        action.Should().Throw<ArgumentException>().WithParameterName("workers");
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var condition = predicate ?? throw new ArgumentNullException(nameof(predicate));

        for (var attempt = 0; attempt < 500 && !condition(); attempt++)
        {
            await Task.Delay(10, CancellationToken.None);
        }

        condition().Should().BeTrue();
    }

    private static ExecutionWorkerPool<PoolSession> CreateWorkerPool(
        int workerCount,
        PoolSessionTracker tracker)
    {
        return new ExecutionWorkerPool<PoolSession>(
            workerIndex => new IndexedTrackingSessionFactory(workerIndex, tracker),
            new ExecutionWorkerPoolOptions(workerCount, "Execution Worker Pool"));
    }

    private static void PoisonFirstWorkerThread(ExecutionWorkerPool<PoolSession> workerPool)
    {
        workerPool.SetWorkerThreadForTesting(0, new Thread(static () => { }));
    }

    private sealed class IndexedTrackingSessionFactory(
        int workerIndex,
        PoolSessionTracker tracker,
        bool failOnCreate = false,
        bool failOnDispose = false,
        Action? onCreate = null) : IExecutionSessionFactory<PoolSession>
    {
        private readonly PoolSessionTracker _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));

        public PoolSession CreateSession(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sessionSequence = _tracker.IncrementCreateCount(workerIndex);
            onCreate?.Invoke();
            if (failOnCreate)
            {
                throw new InvalidOperationException("boom");
            }

            return new PoolSession(workerIndex, sessionSequence, Environment.CurrentManagedThreadId);
        }

        public void DisposeSession(PoolSession session)
        {
            _ = session ?? throw new ArgumentNullException(nameof(session));
            _tracker.IncrementDisposeCount(workerIndex);

            if (failOnDispose)
            {
                throw new InvalidOperationException("dispose boom");
            }
        }
    }

    private sealed class PoolSessionTracker
    {
        private readonly ConcurrentDictionary<int, int> _createCounts = new();
        private readonly ConcurrentDictionary<int, int> _disposeCounts = new();

        public int GetCreateCount(int workerIndex)
        {
            return _createCounts.TryGetValue(workerIndex, out var count) ? count : 0;
        }

        public int GetDisposeCount(int workerIndex)
        {
            return _disposeCounts.TryGetValue(workerIndex, out var count) ? count : 0;
        }

        public int IncrementCreateCount(int workerIndex)
        {
            return _createCounts.AddOrUpdate(workerIndex, 1, static (_, count) => count + 1);
        }

        public void IncrementDisposeCount(int workerIndex)
        {
            _ = _disposeCounts.AddOrUpdate(workerIndex, 1, static (_, count) => count + 1);
        }
    }

    private sealed class PoolSession(int workerIndex, int sessionSequence, int ownerThreadId)
    {
        public int WorkerIndex { get; } = workerIndex;

        public int SessionSequence { get; } = sessionSequence;

        public int OwnerThreadId { get; } = ownerThreadId;
    }

    private sealed class RecordingScheduler : IWorkerScheduler<PoolSession>
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<int> _snapshotCounts = new();
        private readonly System.Collections.Concurrent.ConcurrentQueue<IExecutionWorker<PoolSession>> _selectedWorkers = new();
        private int _invocationCount;

        public int InvocationCount => Volatile.Read(ref _invocationCount);

        public int LastSnapshotCount => _snapshotCounts.TryPeek(out var value) ? value : 0;

        public IExecutionWorker<PoolSession>? LastSelected =>
            _selectedWorkers.TryPeek(out var value) ? value : null;

        public IExecutionWorker<PoolSession> SelectWorker(IReadOnlyList<IExecutionWorker<PoolSession>> workers)
        {
            Interlocked.Increment(ref _invocationCount);
            var selected = workers[0];
            _snapshotCounts.Enqueue(workers.Count);
            _selectedWorkers.Enqueue(selected);
            return selected;
        }
    }

    private sealed class FixedIndexScheduler(int targetIndex) : IWorkerScheduler<PoolSession>
    {
        public IExecutionWorker<PoolSession> SelectWorker(IReadOnlyList<IExecutionWorker<PoolSession>> workers)
        {
            return workers[targetIndex];
        }
    }
}
