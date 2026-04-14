using System.Collections.Concurrent;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Execution.Test;

public sealed class ExecutionWorkerPoolTest
{
    [Fact]
    public async Task ExecuteAsync_ShouldDistributeWorkAcrossMultipleDedicatedThreads()
    {
        var tracker = new PoolSessionTracker();

        using (var workerPool = CreateWorkerPool(3, tracker))
        {
            var results = await Task.WhenAll(
                Enumerable.Range(0, 9)
                    .Select(
                        _ => workerPool.ExecuteAsync(
                            (session, cancellationToken) =>
                                (session.WorkerIndex, session.SessionSequence, Thread.CurrentThread.ManagedThreadId, session.OwnerThreadId),
                            cancellationToken: CancellationToken.None)));

            Assert.Equal([0, 1, 2], results.Select(result => result.WorkerIndex).Distinct().OrderBy(index => index));
            Assert.Equal(3, results.Select(result => result.ManagedThreadId).Distinct().Count());
            Assert.All(results, result => Assert.Equal(result.OwnerThreadId, result.ManagedThreadId));
            Assert.Equal(1, tracker.GetCreateCount(0));
            Assert.Equal(1, tracker.GetCreateCount(1));
            Assert.Equal(1, tracker.GetCreateCount(2));
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRecycleOnlyTheFailedWorkerSession()
    {
        var tracker = new PoolSessionTracker();

        using (var workerPool = CreateWorkerPool(2, tracker))
        {
            var firstWorker = await workerPool.ExecuteAsync(
                (session, cancellationToken) => (session.WorkerIndex, session.SessionSequence),
                cancellationToken: CancellationToken.None);
            var secondWorker = await workerPool.ExecuteAsync(
                (session, cancellationToken) => (session.WorkerIndex, session.SessionSequence),
                cancellationToken: CancellationToken.None);

            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await workerPool.ExecuteAsync<int>(
                    (session, cancellationToken) =>
                    {
                        Assert.Equal(0, session.WorkerIndex);
                        throw new InvalidOperationException("boom");
                    },
                    new ExecutionRequestOptions(recycleSessionOnFailure: true),
                    CancellationToken.None));

            var thirdWorker = await workerPool.ExecuteAsync(
                (session, cancellationToken) => (session.WorkerIndex, session.SessionSequence),
                cancellationToken: CancellationToken.None);
            var fourthWorker = await workerPool.ExecuteAsync(
                (session, cancellationToken) => (session.WorkerIndex, session.SessionSequence),
                cancellationToken: CancellationToken.None);

            Assert.Equal((0, 1), firstWorker);
            Assert.Equal((1, 1), secondWorker);
            Assert.Equal((1, 1), thirdWorker);
            Assert.Equal((0, 2), fourthWorker);
        }

        Assert.Equal(2, tracker.GetCreateCount(0));
        Assert.Equal(1, tracker.GetCreateCount(1));
        Assert.Equal(2, tracker.GetDisposeCount(0));
        Assert.Equal(1, tracker.GetDisposeCount(1));
    }

    [Fact]
    public void Initialize_ShouldInitializeAllWorkers()
    {
        var tracker = new PoolSessionTracker();

        using (var workerPool = CreateWorkerPool(4, tracker))
        {
            workerPool.Initialize();

            Assert.Equal(4, workerPool.WorkerCount);
            Assert.Equal(1, tracker.GetCreateCount(0));
            Assert.Equal(1, tracker.GetCreateCount(1));
            Assert.Equal(1, tracker.GetCreateCount(2));
            Assert.Equal(1, tracker.GetCreateCount(3));
        }

        Assert.Equal(1, tracker.GetDisposeCount(0));
        Assert.Equal(1, tracker.GetDisposeCount(1));
        Assert.Equal(1, tracker.GetDisposeCount(2));
        Assert.Equal(1, tracker.GetDisposeCount(3));
    }

    private static ExecutionWorkerPool<PoolSession> CreateWorkerPool(
        int workerCount,
        PoolSessionTracker tracker)
    {
        return new ExecutionWorkerPool<PoolSession>(
            workerIndex => new IndexedTrackingSessionFactory(workerIndex, tracker),
            new ExecutionWorkerPoolOptions(workerCount, "Execution Worker Pool"));
    }

    private sealed class IndexedTrackingSessionFactory : IExecutionSessionFactory<PoolSession>
    {
        private readonly int _workerIndex;
        private readonly PoolSessionTracker _tracker;

        public IndexedTrackingSessionFactory(int workerIndex, PoolSessionTracker tracker)
        {
            _workerIndex = workerIndex;
            _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        }

        public PoolSession CreateSession(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sessionSequence = _tracker.IncrementCreateCount(_workerIndex);
            return new PoolSession(_workerIndex, sessionSequence, Thread.CurrentThread.ManagedThreadId);
        }

        public void DisposeSession(PoolSession session)
        {
            _ = session ?? throw new ArgumentNullException(nameof(session));
            _tracker.IncrementDisposeCount(_workerIndex);
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

    private sealed class PoolSession
    {
        public PoolSession(int workerIndex, int sessionSequence, int ownerThreadId)
        {
            WorkerIndex = workerIndex;
            SessionSequence = sessionSequence;
            OwnerThreadId = ownerThreadId;
        }

        public int WorkerIndex { get; }

        public int SessionSequence { get; }

        public int OwnerThreadId { get; }
    }
}
