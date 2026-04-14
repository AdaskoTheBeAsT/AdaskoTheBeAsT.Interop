using System.Collections.Concurrent;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Execution.Test;

public sealed class ExecutionWorkerTest
{
    [Fact]
    public async Task ExecuteAsync_ShouldExecuteActionDelegateAsync()
    {
        var sessionFactory = new TrackingSessionFactory();
        var executedSessionIds = new ConcurrentBag<int>();

        using var worker = new ExecutionWorker<TestSession>(sessionFactory);
        await worker.ExecuteAsync(
            (session, _) => executedSessionIds.Add(session.SessionId),
            cancellationToken: CancellationToken.None);

        executedSessionIds.Should().ContainSingle();
        executedSessionIds.Single().Should().Be(1);
        sessionFactory.CreateCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSerializeWorkOnTheSameDedicatedThreadAsync()
    {
        var sessionFactory = new TrackingSessionFactory();

        using var worker = new ExecutionWorker<TestSession>(sessionFactory);
        var callerThreadId = Environment.CurrentManagedThreadId;
        var submissions = new ConcurrentBag<Task<(int SessionId, int ManagedThreadId, int OwnerThreadId)>>();
        var writers = Enumerable.Range(0, 16)
            .Select(
                _ => new Thread(
                    () => submissions.Add(
                        worker.ExecuteAsync(
                            static (session, _) =>
                                (session.SessionId, Environment.CurrentManagedThreadId, session.OwnerThreadId),
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

        results.Should().OnlyContain(static result => result.SessionId == 1);
        results.Should().OnlyContain(static result => result.OwnerThreadId == result.ManagedThreadId);
        results.Select(static result => result.ManagedThreadId).Distinct().Should().ContainSingle();
        results[0].ManagedThreadId.Should().NotBe(callerThreadId);
        sessionFactory.CreateCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnCanceledTaskWithoutStartingTheWorkerWhenTokenIsAlreadyCanceledAsync()
    {
        var sessionFactory = new TrackingSessionFactory();
        using var cancellationTokenSource = new CancellationTokenSource();
        using var worker = new ExecutionWorker<TestSession>(sessionFactory);

        await CancelAsync(cancellationTokenSource);

        var task = worker.ExecuteAsync(
            static (session, _) => session.SessionId,
            cancellationToken: cancellationTokenSource.Token);

        await WaitForCompletionAsync(task);
        task.IsCanceled.Should().BeTrue();
        sessionFactory.CreateCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRecycleTheSessionAfterAnOptedInFailureAsync()
    {
        var sessionFactory = new TrackingSessionFactory();

        await ExecuteScenarioAsync();
        sessionFactory.DisposeCount.Should().Be(2);

        async Task ExecuteScenarioAsync()
        {
            using var worker = new ExecutionWorker<TestSession>(sessionFactory);
            var firstSessionId = await worker.ExecuteAsync(
                static (session, _) => session.SessionId,
                cancellationToken: CancellationToken.None);

            var requestOptions = new ExecutionRequestOptions(recycleSessionOnFailure: true);
            Func<Task> action = async () => await worker.ExecuteAsync<int>(
                static (_, _) => throw new InvalidOperationException("boom"),
                requestOptions,
                CancellationToken.None);
            await action.Should().ThrowAsync<InvalidOperationException>();

            var secondSessionId = await worker.ExecuteAsync(
                static (session, _) => session.SessionId,
                cancellationToken: CancellationToken.None);

            firstSessionId.Should().Be(1);
            secondSessionId.Should().Be(2);
            sessionFactory.CreateCount.Should().Be(2);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldKeepTheCurrentSessionAfterFailureWhenRecycleSessionOnFailureIsDisabledAsync()
    {
        var sessionFactory = new TrackingSessionFactory();

        using var worker = new ExecutionWorker<TestSession>(sessionFactory);
        var firstSessionId = await worker.ExecuteAsync(
            static (session, _) => session.SessionId,
            cancellationToken: CancellationToken.None);

        Func<Task> action = async () => await worker.ExecuteAsync<int>(
            static (_, _) => throw new InvalidOperationException("boom"),
            cancellationToken: CancellationToken.None);
        await action.Should().ThrowAsync<InvalidOperationException>();

        var secondSessionId = await worker.ExecuteAsync(
            static (session, _) => session.SessionId,
            cancellationToken: CancellationToken.None);

        firstSessionId.Should().Be(1);
        secondSessionId.Should().Be(1);
        sessionFactory.CreateCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRecycleTheSessionAfterTheConfiguredOperationLimitAsync()
    {
        var sessionFactory = new TrackingSessionFactory();
        var options = new ExecutionWorkerOptions(maxOperationsPerSession: 2);

        await ExecuteScenarioAsync();
        sessionFactory.DisposeCount.Should().Be(2);

        async Task ExecuteScenarioAsync()
        {
            using var worker = new ExecutionWorker<TestSession>(sessionFactory, options);
            var firstSessionId = await worker.ExecuteAsync(
                static (session, _) => session.SessionId,
                cancellationToken: CancellationToken.None);
            var secondSessionId = await worker.ExecuteAsync(
                static (session, _) => session.SessionId,
                cancellationToken: CancellationToken.None);
            var thirdSessionId = await worker.ExecuteAsync(
                static (session, _) => session.SessionId,
                cancellationToken: CancellationToken.None);

            firstSessionId.Should().Be(1);
            secondSessionId.Should().Be(1);
            thirdSessionId.Should().Be(2);
            sessionFactory.CreateCount.Should().Be(2);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldCancelQueuedRequestWithoutInvokingItsDelegateAsync()
    {
        var sessionFactory = new TrackingSessionFactory();
        var enteredFirstAction = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var queuedCancellationTokenSource = new CancellationTokenSource();
        using var releaseFirstAction = new ManualResetEventSlim();
        using var worker = new ExecutionWorker<TestSession>(sessionFactory);
        var secondActionCallCount = 0;

        var firstTask = worker.ExecuteAsync(
            (session, cancellationToken) =>
            {
                enteredFirstAction.TrySetResult(null);
                releaseFirstAction.Wait(cancellationToken);
                return session.SessionId;
            },
            cancellationToken: CancellationToken.None);

        await enteredFirstAction.Task;

        var secondTask = worker.ExecuteAsync(
            (_, _) =>
            {
                Interlocked.Increment(ref secondActionCallCount);
                return 2;
            },
            cancellationToken: queuedCancellationTokenSource.Token);

        await CancelAsync(queuedCancellationTokenSource);
        releaseFirstAction.Set();

        (await firstTask).Should().Be(1);

        await WaitForCompletionAsync(secondTask);
        secondTask.IsCanceled.Should().BeTrue();
        Volatile.Read(ref secondActionCallCount).Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldCancelRunningRequestWhenTokenIsCanceledAsync()
    {
        var sessionFactory = new TrackingSessionFactory();
        var enteredAction = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellationTokenSource = new CancellationTokenSource();
        using var worker = new ExecutionWorker<TestSession>(sessionFactory);

        var task = worker.ExecuteAsync(
            (_, cancellationToken) =>
            {
                enteredAction.TrySetResult(null);
                cancellationToken.WaitHandle.WaitOne();
                cancellationToken.ThrowIfCancellationRequested();
                return 1;
            },
            cancellationToken: cancellationTokenSource.Token);

        await enteredAction.Task;
        await CancelAsync(cancellationTokenSource);

        await WaitForCompletionAsync(task);
        task.IsCanceled.Should().BeTrue();
    }

    [Fact]
    public async Task Initialize_ShouldCacheStartupFailureAndRejectSubsequentRequestsAsync()
    {
        var sessionFactory = new FaultingSessionFactory(new InvalidOperationException("boom"));

        using var worker = new ExecutionWorker<TestSession>(sessionFactory);
        Action initialize = worker.Initialize;
        initialize.Should().Throw<InvalidOperationException>().WithMessage("boom");
        initialize.Should().Throw<InvalidOperationException>().WithMessage("boom");

        Func<Task> action = async () => await worker.ExecuteAsync(
            static (session, _) => session.SessionId,
            cancellationToken: CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>();
        sessionFactory.CreateCount.Should().Be(1);
    }

    [Fact]
    public async Task Dispose_ShouldDrainQueuedRequestsBeforeShutdownAsync()
    {
        var sessionFactory = new TrackingSessionFactory();
        var enteredFirstAction = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseFirstAction = new ManualResetEventSlim();
        var worker = new ExecutionWorker<TestSession>(sessionFactory);

        try
        {
            var firstTask = worker.ExecuteAsync(
                (session, cancellationToken) =>
                {
                    enteredFirstAction.TrySetResult(null);
                    releaseFirstAction.Wait(cancellationToken);
                    return session.SessionId;
                },
                cancellationToken: CancellationToken.None);

            await enteredFirstAction.Task;

            var secondTask = worker.ExecuteAsync(
                static (session, _) => session.SessionId + 1,
                cancellationToken: CancellationToken.None);

            var disposeThread = new Thread(worker.Dispose);
            disposeThread.Start();
            releaseFirstAction.Set();

            (await firstTask).Should().Be(1);
            (await secondTask).Should().Be(2);
            disposeThread.Join();
            sessionFactory.DisposeCount.Should().Be(1);
        }
        finally
        {
            releaseFirstAction.Set();
            worker.Dispose();
        }
    }

    private static Task CancelAsync(CancellationTokenSource cancellationTokenSource)
    {
#if NET8_0_OR_GREATER
        return cancellationTokenSource.CancelAsync();
#else
        cancellationTokenSource.Cancel();
        return Task.CompletedTask;
#endif
    }

    private static async Task WaitForCompletionAsync(Task task)
    {
        for (var attempt = 0; attempt < 500 && !task.IsCompleted; attempt++)
        {
            await Task.Delay(10, CancellationToken.None);
        }

        task.IsCompleted.Should().BeTrue();
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
            return new TestSession(sessionId, Environment.CurrentManagedThreadId);
        }

        public void DisposeSession(TestSession session)
        {
            _ = session ?? throw new ArgumentNullException(nameof(session));
            Interlocked.Increment(ref _disposeCount);
        }
    }

    private sealed class FaultingSessionFactory(Exception exception) : IExecutionSessionFactory<TestSession>
    {
        private int _createCount;

        public int CreateCount => Volatile.Read(ref _createCount);

        public TestSession CreateSession(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _createCount);
            throw exception;
        }

        public void DisposeSession(TestSession session)
        {
            _ = session ?? throw new ArgumentNullException(nameof(session));
        }
    }

    private sealed class TestSession(int sessionId, int ownerThreadId)
    {
        public int SessionId { get; } = sessionId;

        public int OwnerThreadId { get; } = ownerThreadId;
    }
}
