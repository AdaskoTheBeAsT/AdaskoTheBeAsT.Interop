using AwesomeAssertions;
using Xunit;

#pragma warning disable CS0067, IDISP013, IDISP016, VSTHRD003, AsyncFixer04

namespace AdaskoTheBeAsT.Interop.Execution.Test;

public sealed class ExecutionDispatcherExtensionsTest
{
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan GenerousTimeout = TimeSpan.FromSeconds(5);

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public void Extensions_ShouldValidateNullDispatcherAndNullActionAcrossAllOverloads()
    {
        const IExecutionDispatcher<DeadlineSession>? nullDispatcher = null;
        using var dispatcher = new CustomSynchronousDispatcher();

        Action nullVoidTask = () => _ = nullDispatcher!.ExecuteAsync(static (_, _) => { }, GenerousTimeout, CancellationToken.None);
        Action nullValueTask = () => _ = nullDispatcher!.ExecuteAsync(static (_, _) => 1, GenerousTimeout, CancellationToken.None);
        Action nullPooledVoid = () => _ = ConsumeAsync(nullDispatcher!.ExecuteValueAsync(static (_, _) => { }, GenerousTimeout, CancellationToken.None));
        Action nullPooledVal = () => _ = nullDispatcher!.ExecuteValueAsync(static (_, _) => 1, GenerousTimeout, CancellationToken.None).AsTask();

        nullVoidTask.Should().Throw<ArgumentNullException>().WithParameterName(nameof(dispatcher));
        nullValueTask.Should().Throw<ArgumentNullException>().WithParameterName(nameof(dispatcher));
        nullPooledVoid.Should().Throw<ArgumentNullException>().WithParameterName(nameof(dispatcher));
        nullPooledVal.Should().Throw<ArgumentNullException>().WithParameterName(nameof(dispatcher));

        Action nullAct1 = () => _ = dispatcher.ExecuteAsync((Action<DeadlineSession, CancellationToken>)null!, GenerousTimeout, CancellationToken.None);
        Action nullAct2 = () => _ = dispatcher.ExecuteAsync<DeadlineSession, int>(null!, GenerousTimeout, CancellationToken.None);
        Action nullAct3 = () => _ = ConsumeAsync(dispatcher.ExecuteValueAsync((Action<DeadlineSession, CancellationToken>)null!, GenerousTimeout, CancellationToken.None));
        Action nullAct4 = () => _ = dispatcher.ExecuteValueAsync<DeadlineSession, int>(null!, GenerousTimeout, CancellationToken.None).AsTask();

        nullAct1.Should().Throw<ArgumentNullException>().WithParameterName("action");
        nullAct2.Should().Throw<ArgumentNullException>().WithParameterName("action");
        nullAct3.Should().Throw<ArgumentNullException>().WithParameterName("action");
        nullAct4.Should().Throw<ArgumentNullException>().WithParameterName("action");
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(-1000)]
    [InlineData((double)int.MaxValue + 1d)]
    public void Extensions_ShouldRejectInvalidTimeoutsAcrossOverloads(double milliseconds)
    {
        using var dispatcher = new CustomSynchronousDispatcher();
        var invalid = TimeSpan.FromMilliseconds(milliseconds);

        Action call1 = () => _ = dispatcher.ExecuteAsync(static (_, _) => { }, invalid, CancellationToken.None);
        Action call2 = () => _ = dispatcher.ExecuteAsync(static (_, _) => 1, invalid, CancellationToken.None);
        Action call3 = () => _ = ConsumeAsync(dispatcher.ExecuteValueAsync(static (_, _) => { }, invalid, CancellationToken.None));
        Action call4 = () => _ = dispatcher.ExecuteValueAsync(static (_, _) => 1, invalid, CancellationToken.None).AsTask();

        call1.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("timeout");
        call2.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("timeout");
        call3.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("timeout");
        call4.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("timeout");
    }

    [Fact]
    public async Task Extensions_ShouldSupportInfiniteAndFiniteTimeoutsAcrossAllEightOverloadsAsync()
    {
        await using var worker = new ExecutionWorker<DeadlineSession>(new DeadlineSessionFactory());

        await worker.ExecuteAsync(static (_, _) => { }, Timeout.InfiniteTimeSpan, TestToken);
        await worker.ExecuteAsync(static (_, _) => { }, Timeout.InfiniteTimeSpan, ExecutionRequestOptions.Default, TestToken);
        (await worker.ExecuteAsync(static (s, _) => s.Id, Timeout.InfiniteTimeSpan, TestToken)).Should().Be(1);
        (await worker.ExecuteAsync(static (s, _) => s.Id, Timeout.InfiniteTimeSpan, ExecutionRequestOptions.Default, TestToken)).Should().Be(1);
        await worker.ExecuteValueAsync(static (_, _) => { }, Timeout.InfiniteTimeSpan, TestToken);
        await worker.ExecuteValueAsync(static (_, _) => { }, Timeout.InfiniteTimeSpan, ExecutionRequestOptions.Default, TestToken);
        (await worker.ExecuteValueAsync(static (s, _) => s.Id, Timeout.InfiniteTimeSpan, TestToken)).Should().Be(1);
        (await worker.ExecuteValueAsync(static (s, _) => s.Id, Timeout.InfiniteTimeSpan, ExecutionRequestOptions.Default, TestToken)).Should().Be(1);

        await worker.ExecuteAsync(static (_, _) => { }, GenerousTimeout, TestToken);
        (await worker.ExecuteAsync(static (s, _) => s.Id + 1, GenerousTimeout, TestToken)).Should().Be(2);
        await worker.ExecuteValueAsync(static (_, _) => { }, GenerousTimeout, TestToken);
        (await worker.ExecuteValueAsync(static (s, _) => s.Id + 2, GenerousTimeout, TestToken)).Should().Be(3);
    }

    [Fact]
    public async Task Extensions_ZeroTimeout_ShouldRespectCanceledPrecedenceDisposedStateAndTimeoutAsync()
    {
        var executed = false;
        var worker = new ExecutionWorker<DeadlineSession>(new DeadlineSessionFactory());
        await using var pool = new ExecutionWorkerPool<DeadlineSession>(
            new DeadlineSessionFactory(),
            new ExecutionWorkerPoolOptions(1));

        using var preCanceled = new CancellationTokenSource();
        await CancelAsync(preCanceled);

        Func<Task> canceledTask = () => worker.ExecuteAsync(static (_, _) => { }, TimeSpan.Zero, preCanceled.Token);
        var canceledEx = await canceledTask.Should().ThrowAsync<OperationCanceledException>();
        canceledEx.Which.CancellationToken.Should().Be(preCanceled.Token);

        void MarkExecuted() => executed = true;
        Func<Task<int>> zeroWorker = () => ExecuteZeroHelperAsync(worker, _ => MarkExecuted(), TimeSpan.Zero);
        Func<Task> zeroVoid = () => worker.ExecuteAsync((_, _) => MarkExecuted(), TimeSpan.Zero, TestToken);
        Func<Task> zeroPool = async () => await pool.ExecuteValueAsync((_, _) => MarkExecuted(), TimeSpan.Zero, TestToken);
        Func<Task<int>> zeroPoolVal = async () => await pool.ExecuteValueAsync(static (s, _) => s.Id, TimeSpan.Zero, TestToken);

        await zeroWorker.Should().ThrowAsync<TimeoutException>();
        await zeroVoid.Should().ThrowAsync<TimeoutException>();
        await zeroPool.Should().ThrowAsync<TimeoutException>();
        await zeroPoolVal.Should().ThrowAsync<TimeoutException>();
        executed.Should().BeFalse();

        await worker.DisposeAsync();
        Func<Task<int>> disposedZero = () => worker.ExecuteAsync(static (s, _) => s.Id, TimeSpan.Zero, TestToken);
        await disposedZero.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task Extensions_PreCanceledToken_ShouldPreserveExactCallerTokenAcrossAllOverloadsAsync()
    {
        await using var worker = new ExecutionWorker<DeadlineSession>(new DeadlineSessionFactory());
        using var cts = new CancellationTokenSource();
        await CancelAsync(cts);

        var ex1 = await ((Func<Task>)(() => worker.ExecuteAsync(static (_, _) => { }, GenerousTimeout, cts.Token)))
            .Should().ThrowAsync<OperationCanceledException>();
        var ex2 = await ((Func<Task<int>>)(() => worker.ExecuteAsync(static (s, _) => s.Id, GenerousTimeout, cts.Token)))
            .Should().ThrowAsync<OperationCanceledException>();
        var ex3 = await ((Func<Task>)(async () => await worker.ExecuteValueAsync(static (_, _) => { }, GenerousTimeout, cts.Token)))
            .Should().ThrowAsync<OperationCanceledException>();
        var ex4 = await ((Func<Task<int>>)(async () => await worker.ExecuteValueAsync(static (s, _) => s.Id, GenerousTimeout, cts.Token)))
            .Should().ThrowAsync<OperationCanceledException>();

        ex1.Which.CancellationToken.Should().Be(cts.Token);
        ex2.Which.CancellationToken.Should().Be(cts.Token);
        ex3.Which.CancellationToken.Should().Be(cts.Token);
        ex4.Which.CancellationToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task Extensions_ShouldBoundStartupWaitAndRecoverOnceStartupFinishesAsync()
    {
        using var createEntered = new ManualResetEventSlim();
        using var releaseCreate = new ManualResetEventSlim();
        var factory = new DeadlineSessionFactory(onCreate: () =>
        {
            createEntered.Set();
            releaseCreate.Wait(GateTimeout, TestToken).Should().BeTrue();
        });

        await using var worker = new ExecutionWorker<DeadlineSession>(factory);
        try
        {
            Func<Task<int>> timedOutStartup = () => worker.ExecuteAsync(
                static (s, _) => s.Id,
                TimeSpan.FromMilliseconds(60),
                TestToken);
            await timedOutStartup.Should().ThrowAsync<TimeoutException>();
            createEntered.IsSet.Should().BeTrue();
        }
        finally
        {
            releaseCreate.Set();
        }

        var id = await worker.ExecuteAsync(static (s, _) => s.Id, GenerousTimeout, TestToken);
        id.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Extensions_QueuedExpiredItem_ShouldNeverRunAndAllowSubsequentWorkAsync(bool pooledValueTask)
    {
        using var firstEntered = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        var expiredRan = 0;

        await using var worker = new ExecutionWorker<DeadlineSession>(new DeadlineSessionFactory());
        await worker.InitializeAsync(TestToken);

        var blocker = worker.ExecuteAsync(
            (_, _) =>
            {
                firstEntered.Set();
                releaseFirst.Wait(GateTimeout, TestToken).Should().BeTrue();
            },
            cancellationToken: TestToken);

        try
        {
            firstEntered.Wait(GateTimeout, TestToken).Should().BeTrue();

            void MarkExpiredRan() => Interlocked.Increment(ref expiredRan);
            Func<Task> expiredCall = pooledValueTask
                ? async () => await worker.ExecuteValueAsync((_, _) => MarkExpiredRan(), TimeSpan.FromMilliseconds(40), TestToken)
                : () => worker.ExecuteAsync((_, _) => MarkExpiredRan(), TimeSpan.FromMilliseconds(40), TestToken);

            await expiredCall.Should().ThrowAsync<TimeoutException>();
        }
        finally
        {
            releaseFirst.Set();
        }

        await blocker;
        var nextResult = await worker.ExecuteValueAsync(static (s, _) => s.Id, GenerousTimeout, TestToken);
        nextResult.Should().Be(1);
        Volatile.Read(ref expiredRan).Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Extensions_InFlightTimeout_ShouldReturnPromptlyKeepWorkerOccupiedAndPreserveLiveTokenAsync(bool pooledValueTask)
    {
        using var inFlightEntered = new ManualResetEventSlim();
        using var releaseInFlight = new ManualResetEventSlim();
        using var tokenObservedAfterTimeout = new ManualResetEventSlim();
        var tokenStillValidAfterCallerTimeout = false;
        var secondStartedBeforeRelease = false;
        var firstFinished = 0;

        await using var worker = new ExecutionWorker<DeadlineSession>(new DeadlineSessionFactory());
        await worker.InitializeAsync(TestToken);

        int SlowNativeWork(DeadlineSession session, CancellationToken token)
        {
            _ = session;
            inFlightEntered.Set();
            releaseInFlight.Wait(GateTimeout, TestToken).Should().BeTrue();

            var waitHandleSet = token.WaitHandle.WaitOne(0);
            tokenStillValidAfterCallerTimeout = token.IsCancellationRequested && waitHandleSet;
            Volatile.Write(ref firstFinished, 1);
            tokenObservedAfterTimeout.Set();
            return 99;
        }

        Func<Task<int>> call = pooledValueTask
            ? async () => await worker.ExecuteValueAsync(SlowNativeWork, TimeSpan.FromMilliseconds(50), TestToken)
            : () => worker.ExecuteAsync(SlowNativeWork, TimeSpan.FromMilliseconds(50), TestToken);

        Task<int>? followUp = null;
        try
        {
            await call.Should().ThrowAsync<TimeoutException>();
            inFlightEntered.IsSet.Should().BeTrue();

            followUp = worker.ExecuteAsync(
                (_, _) =>
                {
                    secondStartedBeforeRelease = Volatile.Read(ref firstFinished) == 0;
                    return 42;
                },
                GenerousTimeout,
                TestToken);

            followUp.IsCompleted.Should().BeFalse();
        }
        finally
        {
            releaseInFlight.Set();
        }

        tokenObservedAfterTimeout.Wait(GateTimeout, TestToken).Should().BeTrue();
        (await followUp).Should().Be(42);
        tokenStillValidAfterCallerTimeout.Should().BeTrue();
        secondStartedBeforeRelease.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Extensions_CallerCancellationDuringWait_ShouldPreserveExactCallerTokenAsync(bool pooledValueTask)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var callerCts = new CancellationTokenSource();

        await using var worker = new ExecutionWorker<DeadlineSession>(new DeadlineSessionFactory());
        await worker.InitializeAsync(TestToken);

        int BlockingWork(DeadlineSession session, CancellationToken token)
        {
            _ = session;
            _ = token;
            entered.Set();
            release.Wait(GateTimeout, TestToken).Should().BeTrue();
            return 7;
        }

        var runningTask = pooledValueTask
            ? worker.ExecuteValueAsync(BlockingWork, GenerousTimeout, callerCts.Token).AsTask()
            : worker.ExecuteAsync(BlockingWork, GenerousTimeout, callerCts.Token);

        try
        {
            entered.Wait(GateTimeout, TestToken).Should().BeTrue();
            await CancelAsync(callerCts);

            Func<Task<int>> awaitRunning = () => runningTask;
            var ex = await awaitRunning.Should().ThrowAsync<OperationCanceledException>();
            ex.Which.CancellationToken.Should().Be(callerCts.Token);
        }
        finally
        {
            release.Set();
        }

        _ = await worker.ExecuteAsync(static (s, _) => s.Id, GenerousTimeout, TestToken);
    }

    [Fact]
    public async Task Extensions_CustomDispatcherAndPoolBurst_ShouldCoverSyncPathsErrorsAndCtsPoolOverflowAsync()
    {
        await using var custom = new CustomSynchronousDispatcher();

        (await custom.ExecuteAsync(static (s, _) => s.Id + 10, GenerousTimeout, TestToken)).Should().Be(11);
        (await custom.ExecuteValueAsync(static (s, _) => s.Id + 20, GenerousTimeout, TestToken)).Should().Be(21);
        await custom.ExecuteValueAsync(static (_, _) => { }, GenerousTimeout, TestToken);

        Func<Task<int>> appFault = () => custom.ExecuteAsync<DeadlineSession, int>(
            static (_, _) => throw new InvalidOperationException("app-error"),
            GenerousTimeout,
            TestToken);
        await appFault.Should().ThrowAsync<InvalidOperationException>().WithMessage("app-error");

        Func<Task> voidAppFault = async () => await custom.ExecuteValueAsync(
            static (_, _) => throw new InvalidOperationException("void-app-error"),
            GenerousTimeout,
            TestToken);
        await voidAppFault.Should().ThrowAsync<InvalidOperationException>().WithMessage("void-app-error");

        custom.ThrowSynchronouslyOnSubmit = true;
        Func<Task<int>> submitFault = () => custom.ExecuteAsync(static (s, _) => s.Id, GenerousTimeout, TestToken);
        Func<Task<int>> submitValFault = async () => await custom.ExecuteValueAsync(static (s, _) => s.Id, GenerousTimeout, TestToken);
        Func<Task> submitVoidFault = async () => await custom.ExecuteValueAsync(static (_, _) => { }, GenerousTimeout, TestToken);
        await submitFault.Should().ThrowAsync<InvalidOperationException>().WithMessage("submit-rejected");
        await submitValFault.Should().ThrowAsync<InvalidOperationException>().WithMessage("submit-rejected");
        await submitVoidFault.Should().ThrowAsync<InvalidOperationException>().WithMessage("submit-rejected");

        await using var pool = new ExecutionWorkerPool<DeadlineSession>(
            new DeadlineSessionFactory(),
            new ExecutionWorkerPoolOptions(4));
        await pool.InitializeAsync(TestToken);

        var tasks = new Task<int>[300];
        for (var i = 0; i < tasks.Length; i++)
        {
            var index = i;
            tasks[i] = pool.ExecuteValueAsync(
                (_, _) => index,
                GenerousTimeout,
                CancellationToken.None).AsTask();
        }

        var results = await Task.WhenAll(tasks);
        results.Should().HaveCount(300);
    }

    [Fact]
    public async Task Extensions_NonCancelableDisposedTokenAndPooledFaults_ShouldBeHandledCleanlyAsync()
    {
        await using var worker = new ExecutionWorker<DeadlineSession>(new DeadlineSessionFactory());
        await worker.InitializeAsync(TestToken);

        // Non-cancelable token (CanBeCanceled == false) fast-path:
        (await worker.ExecuteAsync(static (s, _) => s.Id, GenerousTimeout, CancellationToken.None)).Should().Be(1);
        await worker.ExecuteValueAsync(static (_, _) => { }, GenerousTimeout, CancellationToken.None);

        // Pooled ValueTask<T> fault on worker:
        Func<Task<int>> pooledValFault = async () => await worker.ExecuteValueAsync<DeadlineSession, int>(
            static (_, _) => throw new InvalidOperationException("pooled-val-fault"),
            GenerousTimeout,
            CancellationToken.None);
        await pooledValFault.Should().ThrowAsync<InvalidOperationException>().WithMessage("pooled-val-fault");

        // Disposed caller CancellationTokenSource token still executes cleanly without leaking:
        CancellationToken disposedToken;
        using (var disposedCts = new CancellationTokenSource())
        {
            disposedToken = disposedCts.Token;
        }

        (await worker.ExecuteAsync(static (s, _) => s.Id, GenerousTimeout, disposedToken)).Should().Be(1);

        // Void ExecuteValueAsync caller cancellation while running:
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var voidCts = new CancellationTokenSource();
        var voidRunning = worker.ExecuteValueAsync(
            (_, _) =>
            {
                entered.Set();
                release.Wait(GateTimeout, TestToken).Should().BeTrue();
            },
            GenerousTimeout,
            voidCts.Token).AsTask();
        try
        {
            entered.Wait(GateTimeout, TestToken).Should().BeTrue();
            await CancelAsync(voidCts);
            Func<Task> awaitVoid = () => voidRunning;
            (await awaitVoid.Should().ThrowAsync<OperationCanceledException>())
                .Which.CancellationToken.Should().Be(voidCts.Token);
        }
        finally
        {
            release.Set();
        }
    }

    private static Task ConsumeAsync(ValueTask task) => task.AsTask();

    private static Task<int> ExecuteZeroHelperAsync(
        IExecutionDispatcher<DeadlineSession> dispatcher,
        Action<DeadlineSession>? callback,
        TimeSpan timeout) =>
        dispatcher.ExecuteAsync(
            (s, _) =>
            {
                callback?.Invoke(s);
                return s.Id;
            },
            timeout,
            CancellationToken.None);

    private static Task CancelAsync(CancellationTokenSource cts)
    {
#if NET8_0_OR_GREATER
        return cts.CancelAsync();
#else
        cts.Cancel();
        return Task.CompletedTask;
#endif
    }

    private sealed class DeadlineSession(int id)
    {
        public int Id { get; } = id;
    }

    private sealed class DeadlineSessionFactory(Action? onCreate = null) : IExecutionSessionFactory<DeadlineSession>
    {
        private int _nextId;

        public DeadlineSession CreateSession(CancellationToken cancellationToken)
        {
            onCreate?.Invoke();
            return new DeadlineSession(Interlocked.Increment(ref _nextId));
        }

        public void DisposeSession(DeadlineSession session)
        {
        }
    }

    private sealed class CustomSynchronousDispatcher : IExecutionDispatcher<DeadlineSession>
    {
        private readonly DeadlineSession _session = new(1);

        public event EventHandler<WorkerFaultedEventArgs>? WorkerFaulted;

        public int QueueDepth => 0;

        public string? Name => "CustomSync";

        public bool ThrowSynchronouslyOnSubmit { get; set; }

        public void Initialize()
        {
        }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => default;

        public Task ExecuteAsync(
            Action<DeadlineSession, CancellationToken> action,
            ExecutionRequestOptions? options = null,
            CancellationToken cancellationToken = default)
        {
#if NET8_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(action);
#else
            if (action is null)
            {
                throw new ArgumentNullException(nameof(action));
            }
#endif

            if (ThrowSynchronouslyOnSubmit)
            {
                throw new InvalidOperationException("submit-rejected");
            }

            try
            {
                action(_session, cancellationToken);
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }
        }

        public Task<TResult> ExecuteAsync<TResult>(
            Func<DeadlineSession, CancellationToken, TResult> action,
            ExecutionRequestOptions? options = null,
            CancellationToken cancellationToken = default)
        {
#if NET8_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(action);
#else
            if (action is null)
            {
                throw new ArgumentNullException(nameof(action));
            }
#endif

            if (ThrowSynchronouslyOnSubmit)
            {
                throw new InvalidOperationException("submit-rejected");
            }

            try
            {
                return Task.FromResult(action(_session, cancellationToken));
            }
            catch (Exception ex)
            {
                return Task.FromException<TResult>(ex);
            }
        }

        public ValueTask ExecuteValueAsync(
            Action<DeadlineSession, CancellationToken> action,
            ExecutionRequestOptions? options = null,
            CancellationToken cancellationToken = default) =>
            new(ExecuteAsync(action, options, cancellationToken));

        public ValueTask<TResult> ExecuteValueAsync<TResult>(
            Func<DeadlineSession, CancellationToken, TResult> action,
            ExecutionRequestOptions? options = null,
            CancellationToken cancellationToken = default) =>
            new(ExecuteAsync(action, options, cancellationToken));
    }
}
