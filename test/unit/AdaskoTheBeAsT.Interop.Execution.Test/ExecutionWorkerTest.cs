using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Execution.Test;

public sealed class ExecutionWorkerTest
{
    [Fact]
    public void Constructor_ShouldThrowWhenSessionFactoryIsNull()
    {
        const IExecutionSessionFactory<TestSession>? sessionFactory = null;
        var action = () =>
        {
            using var ignored = new ExecutionWorker<TestSession>(sessionFactory!);
        };

        action.Should().Throw<ArgumentNullException>().WithParameterName(nameof(sessionFactory));
    }

    [Fact]
    public void ExecuteAsync_ShouldThrowWhenActionDelegateIsNull()
    {
        var sessionFactory = new TrackingSessionFactory();
        using var worker = new ExecutionWorker<TestSession>(sessionFactory);

        Action action = () => _ = worker.ExecuteAsync((Action<TestSession, CancellationToken>)null!, cancellationToken: CancellationToken.None);

        action.Should().Throw<ArgumentNullException>().WithParameterName(nameof(action));
    }

    [Fact]
    public void ExecuteAsyncOfTResult_ShouldThrowWhenActionDelegateIsNull()
    {
        var sessionFactory = new TrackingSessionFactory();
        using var worker = new ExecutionWorker<TestSession>(sessionFactory);

        Action action = () => _ = worker.ExecuteAsync<int>((Func<TestSession, CancellationToken, int>)null!, cancellationToken: CancellationToken.None);

        action.Should().Throw<ArgumentNullException>().WithParameterName(nameof(action));
    }

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
    public async Task ExecuteAsync_ShouldReturnFaultedTaskWhenTheChannelRejectsWorkAsync()
    {
        var sessionFactory = new TrackingSessionFactory();
        using var worker = new ExecutionWorker<TestSession>(sessionFactory);
        worker.TryCompleteChannelForTesting().Should().BeTrue();

        Func<Task> action = async () => await worker.ExecuteAsync(
            static (session, _) => session.SessionId,
            cancellationToken: CancellationToken.None);

        await action.Should().ThrowAsync<ObjectDisposedException>();
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
    public async Task ExecuteAsync_ShouldThrowWhenSessionFactoryReturnsNullAsync()
    {
        using var worker = new ExecutionWorker<TestSession>(new NullSessionFactory());
        Func<Task> action = async () => await worker.ExecuteAsync(
            static (session, _) => session.SessionId,
            cancellationToken: CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("The session factory returned null.");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRunOnStaThreadWhenRequestedOnWindowsAsync()
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            return;
        }

        var sessionFactory = new TrackingSessionFactory();
        var options = new ExecutionWorkerOptions(useStaThread: true);

        using var worker = new ExecutionWorker<TestSession>(sessionFactory, options);
        var apartmentState = await worker.ExecuteAsync(
            static (_, _) => Thread.CurrentThread.GetApartmentState(),
            cancellationToken: CancellationToken.None);

        apartmentState.Should().Be(ApartmentState.STA);
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
        var initialize = worker.Initialize;
        initialize.Should().Throw<InvalidOperationException>().WithMessage("boom");
        initialize.Should().Throw<InvalidOperationException>().WithMessage("boom");

        Func<Task> action = async () => await worker.ExecuteAsync(
            static (session, _) => session.SessionId,
            cancellationToken: CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>();
        sessionFactory.CreateCount.Should().Be(1);
    }

    [Fact]
    public void Process_ShouldThrowForInvalidStartupState()
    {
        var sessionFactory = new TrackingSessionFactory();
        using var worker = new ExecutionWorker<TestSession>(sessionFactory);
        var state = new object();

        var action = () => worker.Process(state);

        action.Should().Throw<ArgumentException>()
            .WithMessage("Invalid worker startup state.*")
            .WithParameterName(nameof(state));
    }

    [Fact]
    public void ThrowIfFaulted_ShouldNotThrowWhenTheWorkerIsHealthy()
    {
        var sessionFactory = new TrackingSessionFactory();
        using var worker = new ExecutionWorker<TestSession>(sessionFactory);

        var action = worker.ThrowIfFaulted;

        action.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfFaulted_ShouldThrowRecordedFatalFailure()
    {
        var sessionFactory = new TrackingSessionFactory();
        using var worker = new ExecutionWorker<TestSession>(sessionFactory);
        SetFatalFailure(worker, new InvalidOperationException("fatal boom"));

        var action = worker.ThrowIfFaulted;

        action.Should().Throw<InvalidOperationException>().WithMessage("fatal boom");
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

    [Fact]
    public async Task Dispose_ShouldReturnWhenCalledFromTheWorkerThreadAsync()
    {
        var sessionFactory = new TrackingSessionFactory();
        using var worker = new ExecutionWorker<TestSession>(sessionFactory);
        var task = worker.ExecuteAsync(
            (session, _) =>
            {
                worker.Dispose();
                return session.SessionId;
            },
            cancellationToken: CancellationToken.None);

        (await task).Should().Be(1);
        await WaitUntilAsync(() => sessionFactory.DisposeCount == 1);

        Action action = () => _ = worker.ExecuteAsync(
            static (session, _) => session.SessionId,
            cancellationToken: CancellationToken.None);

        action.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task Dispose_ShouldIgnoreSessionDisposeFailuresDuringWorkerShutdownAsync()
    {
        var sessionFactory = new TrackingSessionFactory(new InvalidOperationException("dispose boom"));
        using var worker = new ExecutionWorker<TestSession>(sessionFactory);
        _ = await worker.ExecuteAsync(
            static (session, _) => session.SessionId,
            cancellationToken: CancellationToken.None);

        var action = worker.Dispose;

        action.Should().NotThrow();
        sessionFactory.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailPendingRequestsWhenWorkerShutdownFaultsAsync()
    {
        var sessionFactory = new TrackingSessionFactory(new InvalidOperationException("dispose boom"));
        var enteredFirstAction = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var queuedCancellationTokenSource = new CancellationTokenSource();
        using var releaseFirstAction = new ManualResetEventSlim();
        using var worker = new ExecutionWorker<TestSession>(sessionFactory);

        var firstTask = worker.ExecuteAsync<int>(
            (_, cancellationToken) =>
            {
                enteredFirstAction.TrySetResult(null);
                releaseFirstAction.Wait(cancellationToken);
                throw new InvalidOperationException("boom");
            },
            new ExecutionRequestOptions(recycleSessionOnFailure: true),
            CancellationToken.None);

        await enteredFirstAction.Task;

        var secondTask = worker.ExecuteAsync(
            static (session, _) => session.SessionId,
            cancellationToken: CancellationToken.None);
        var thirdTask = worker.ExecuteAsync(
            static (session, _) => session.SessionId,
            cancellationToken: queuedCancellationTokenSource.Token);

        await CancelAsync(queuedCancellationTokenSource);
        releaseFirstAction.Set();

        await WaitForCompletionAsync(firstTask);
        await WaitForCompletionAsync(secondTask);
        await WaitForCompletionAsync(thirdTask);

        firstTask.IsFaulted.Should().BeTrue();
        firstTask.Exception.Should().NotBeNull();
        firstTask.Exception!.GetBaseException().Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("boom");
        secondTask.IsFaulted.Should().BeTrue();
        secondTask.Exception.Should().NotBeNull();
        secondTask.Exception!.GetBaseException().Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("dispose boom");
        thirdTask.IsCanceled.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRethrowFatalFailureAfterTheWorkerFaultsAsync()
    {
        var sessionFactory = new TrackingSessionFactory(new InvalidOperationException("dispose boom"));
        using var worker = new ExecutionWorker<TestSession>(sessionFactory);
        Func<Task> action = async () => await worker.ExecuteAsync<int>(
            static (_, _) => throw new InvalidOperationException("boom"),
            new ExecutionRequestOptions(recycleSessionOnFailure: true),
            CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        await WaitUntilAsync(() => sessionFactory.DisposeCount == 1);

        Action followUpAction = () => _ = worker.ExecuteAsync(
            static (session, _) => session.SessionId,
            cancellationToken: CancellationToken.None);

        followUpAction.Should().Throw<InvalidOperationException>().WithMessage("dispose boom");
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

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var condition = predicate ?? throw new ArgumentNullException(nameof(predicate));

        for (var attempt = 0; attempt < 500 && !condition(); attempt++)
        {
            await Task.Delay(10, CancellationToken.None);
        }

        condition().Should().BeTrue();
    }

    private static void SetFatalFailure(ExecutionWorker<TestSession> worker, Exception exception)
    {
        worker.SetFatalFailure(ExceptionDispatchInfo.Capture(exception));
    }

    private sealed class TrackingSessionFactory(Exception? disposeException = null) : IExecutionSessionFactory<TestSession>
    {
        private readonly Exception? _disposeException = disposeException;
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

            if (_disposeException is not null)
            {
                throw _disposeException;
            }
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

    private sealed class NullSessionFactory : IExecutionSessionFactory<TestSession>
    {
        public TestSession CreateSession(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null!;
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
