using System.Collections.Concurrent;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Execution.Test;

public sealed class ExecutionWorkerTest
{
    [Fact]
    public async Task ExecuteAsync_ShouldSerializeWorkOnTheSameDedicatedThread()
    {
        var sessionFactory = new TrackingSessionFactory();

        using (var worker = new ExecutionWorker<TestSession>(sessionFactory))
        {
            var callerThreadId = Thread.CurrentThread.ManagedThreadId;
            var submissions = new ConcurrentBag<Task<(int SessionId, int ManagedThreadId, int OwnerThreadId)>>();
            var writers = Enumerable.Range(0, 16)
                .Select(
                    _ => new Thread(
                        () => submissions.Add(
                            worker.ExecuteAsync(
                                (session, cancellationToken) =>
                                    (session.SessionId, Thread.CurrentThread.ManagedThreadId, session.OwnerThreadId),
                                cancellationToken: CancellationToken.None)))
                    {
                        IsBackground = true,
                    })
                .ToArray();

            foreach (var writer in writers)
            {
                writer.Start();
            }

            foreach (var writer in writers)
            {
                writer.Join();
            }

            var results = await Task.WhenAll(submissions);

            Assert.All(results, result => Assert.Equal(1, result.SessionId));
            Assert.All(results, result => Assert.Equal(result.OwnerThreadId, result.ManagedThreadId));
            Assert.Single(results.Select(result => result.ManagedThreadId).Distinct());
            Assert.NotEqual(callerThreadId, results[0].ManagedThreadId);
            Assert.Equal(1, sessionFactory.CreateCount);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRecycleTheSessionAfterAnOptedInFailure()
    {
        var sessionFactory = new TrackingSessionFactory();

        using (var worker = new ExecutionWorker<TestSession>(sessionFactory))
        {
            var firstSessionId = await worker.ExecuteAsync(
                (session, cancellationToken) => session.SessionId,
                cancellationToken: CancellationToken.None);

            var requestOptions = new ExecutionRequestOptions(recycleSessionOnFailure: true);
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await worker.ExecuteAsync<int>(
                    (session, cancellationToken) =>
                    {
                        throw new InvalidOperationException("boom");
                    },
                    requestOptions,
                    CancellationToken.None));

            var secondSessionId = await worker.ExecuteAsync(
                (session, cancellationToken) => session.SessionId,
                cancellationToken: CancellationToken.None);

            Assert.Equal(1, firstSessionId);
            Assert.Equal(2, secondSessionId);
            Assert.Equal(2, sessionFactory.CreateCount);
        }

        Assert.Equal(2, sessionFactory.DisposeCount);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRecycleTheSessionAfterTheConfiguredOperationLimit()
    {
        var sessionFactory = new TrackingSessionFactory();
        var options = new ExecutionWorkerOptions(maxOperationsPerSession: 2);

        using (var worker = new ExecutionWorker<TestSession>(sessionFactory, options))
        {
            var firstSessionId = await worker.ExecuteAsync(
                (session, cancellationToken) => session.SessionId,
                cancellationToken: CancellationToken.None);
            var secondSessionId = await worker.ExecuteAsync(
                (session, cancellationToken) => session.SessionId,
                cancellationToken: CancellationToken.None);
            var thirdSessionId = await worker.ExecuteAsync(
                (session, cancellationToken) => session.SessionId,
                cancellationToken: CancellationToken.None);

            Assert.Equal(1, firstSessionId);
            Assert.Equal(1, secondSessionId);
            Assert.Equal(2, thirdSessionId);
            Assert.Equal(2, sessionFactory.CreateCount);
        }

        Assert.Equal(2, sessionFactory.DisposeCount);
    }

    private sealed class TrackingSessionFactory : IExecutionSessionFactory<TestSession>
    {
        private int _createCount;
        private int _disposeCount;

        public int CreateCount => Volatile.Read(ref _createCount);

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public TestSession CreateSession(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sessionId = Interlocked.Increment(ref _createCount);
            return new TestSession(sessionId, Thread.CurrentThread.ManagedThreadId);
        }

        public void DisposeSession(TestSession session)
        {
            _ = session ?? throw new ArgumentNullException(nameof(session));
            Interlocked.Increment(ref _disposeCount);
        }
    }

    private sealed class TestSession
    {
        public TestSession(int sessionId, int ownerThreadId)
        {
            SessionId = sessionId;
            OwnerThreadId = ownerThreadId;
        }

        public int SessionId { get; }

        public int OwnerThreadId { get; }
    }
}
