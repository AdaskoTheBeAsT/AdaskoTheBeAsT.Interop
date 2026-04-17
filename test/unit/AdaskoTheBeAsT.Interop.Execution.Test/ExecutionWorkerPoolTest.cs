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

        using var workerPool = new ExecutionWorkerPool<PoolSession>(
            workerIndex => new IndexedTrackingSessionFactory(workerIndex, tracker, failOnCreate: workerIndex == 1),
            new ExecutionWorkerPoolOptions(3, "Execution Worker Pool"));

        var action = workerPool.Initialize;

        action.Should().Throw<InvalidOperationException>().WithMessage("boom");
        tracker.GetCreateCount(0).Should().Be(1);
        tracker.GetDisposeCount(0).Should().Be(1);
        tracker.GetCreateCount(1).Should().Be(1);
        tracker.GetDisposeCount(1).Should().Be(0);
        tracker.GetCreateCount(2).Should().Be(0);
        tracker.GetDisposeCount(2).Should().Be(0);
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
    public void TryIgnore_ShouldThrowWhenActionIsNull()
    {
        const Action? action = null;
        var assertion = () => ExecutionHelpers.TryIgnore(action!);

        assertion.Should().Throw<ArgumentNullException>().WithParameterName(nameof(action));
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
}
