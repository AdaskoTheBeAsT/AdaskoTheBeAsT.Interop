using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
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
    public void ExecuteAsync_ShouldThrowObjectDisposedExceptionAfterDispose()
    {
        // IDISP016/IDISP017 disabled: this test is specifically verifying the
        // post-Dispose() behavioural contract of ExecuteAsync, so we deliberately
        // hold and invoke the worker after disposal instead of using a
        // using-declaration.
#pragma warning disable IDISP016, IDISP017
        var sessionFactory = new TrackingSessionFactory();
        var worker = new ExecutionWorker<TestSession>(sessionFactory);
        worker.Dispose();

        Action action = () => _ = worker.ExecuteAsync(
            static (session, _) => session.SessionId,
            cancellationToken: CancellationToken.None);
#pragma warning restore IDISP016, IDISP017

        action.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldThrowObjectDisposedExceptionAfterDisposeAsync()
    {
        // IDISP016/IDISP017 disabled: this test is specifically verifying the
        // post-DisposeAsync behavioural contract of ExecuteAsync, so the worker
        // is intentionally constructed without a using-declaration and used
        // after disposal.
#pragma warning disable IDISP016, IDISP017
        var sessionFactory = new TrackingSessionFactory();
        var worker = new ExecutionWorker<TestSession>(sessionFactory);
        await worker.DisposeAsync();

        Action action = () => _ = worker.ExecuteAsync(
            static (session, _) => session.SessionId,
            cancellationToken: CancellationToken.None);
#pragma warning restore IDISP016, IDISP017

        action.Should().Throw<ObjectDisposedException>();
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
            await worker.DisposeAsync();
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

    [Fact]
    public void IsFaulted_ShouldBeFalseForHealthyWorker()
    {
        var sessionFactory = new TrackingSessionFactory();
        using var worker = new ExecutionWorker<TestSession>(sessionFactory);

        worker.IsFaulted.Should().BeFalse();
        worker.Fault.Should().BeNull();
    }

    [Fact]
    public void IsFaulted_ShouldTransitionToTrueAfterTerminalFailure()
    {
        var sessionFactory = new TrackingSessionFactory();
        using var worker = new ExecutionWorker<TestSession>(sessionFactory);
        var expected = new InvalidOperationException("fatal boom");
        worker.SetFatalFailure(ExceptionDispatchInfo.Capture(expected));

        worker.IsFaulted.Should().BeTrue();
        worker.Fault.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task WorkerFaulted_ShouldFireExactlyOnceWhenBothWorkItemAndDisposeSessionThrowAsync()
    {
        var sessionFactory = new TrackingSessionFactory(new InvalidOperationException("dispose boom"));
        var options = new ExecutionWorkerOptions(name: "Fault Worker");
        await using var worker = new ExecutionWorker<TestSession>(sessionFactory, options);
        var raised = new ConcurrentBag<WorkerFaultedEventArgs>();
        worker.WorkerFaulted += (_, args) => raised.Add(args);

        Func<Task> action = async () => await worker.ExecuteAsync<int>(
            static (_, _) => throw new InvalidOperationException("boom"),
            new ExecutionRequestOptions(recycleSessionOnFailure: true),
            CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        await WaitUntilAsync(() => worker.IsFaulted);

        raised.Should().ContainSingle();
        var single = raised.Single();
        single.Exception.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("dispose boom");
        single.WorkerName.Should().Be("Fault Worker");
    }

    [Fact]
    public async Task WorkerFaulted_ShouldNotBreakShutdownWhenSubscriberThrowsAsync()
    {
        var sessionFactory = new TrackingSessionFactory(new InvalidOperationException("dispose boom"));
        using var worker = new ExecutionWorker<TestSession>(sessionFactory);
        worker.WorkerFaulted += (_, _) => throw new InvalidOperationException("subscriber boom");

        Func<Task> action = async () => await worker.ExecuteAsync<int>(
            static (_, _) => throw new InvalidOperationException("boom"),
            new ExecutionRequestOptions(recycleSessionOnFailure: true),
            CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        await WaitUntilAsync(() => sessionFactory.DisposeCount == 1);

        var dispose = worker.Dispose;
        dispose.Should().NotThrow();
    }

    [Fact]
    public async Task QueueDepth_ShouldReportQueuedWorkAndDrainAfterProcessingAsync()
    {
        var sessionFactory = new TrackingSessionFactory();
        var enteredFirstAction = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseFirstAction = new ManualResetEventSlim();
        await using var worker = new ExecutionWorker<TestSession>(sessionFactory);

        worker.QueueDepth.Should().Be(0);

        // AsyncFixer04 disabled: these tasks are stored in local variables/arrays and
        // awaited together via Task.WhenAll before the 'await using' disposes the
        // worker. The analyzer does not track the lifetime through array indexing.
#pragma warning disable AsyncFixer04
        var firstTask = worker.ExecuteAsync(
            (session, cancellationToken) =>
            {
                enteredFirstAction.TrySetResult(null);
                releaseFirstAction.Wait(cancellationToken);
                return session.SessionId;
            },
            cancellationToken: CancellationToken.None);

        await enteredFirstAction.Task;

        var queued = new Task[3];
        for (var i = 0; i < queued.Length; i++)
        {
            queued[i] = worker.ExecuteAsync(
                static (session, _) => session.SessionId,
                cancellationToken: CancellationToken.None);
        }
#pragma warning restore AsyncFixer04

        await WaitUntilAsync(() => worker.QueueDepth == 3);
        worker.QueueDepth.Should().Be(3);

        releaseFirstAction.Set();
        await firstTask;
        await Task.WhenAll(queued);

        await WaitUntilAsync(() => worker.QueueDepth == 0);
        worker.QueueDepth.Should().Be(0);
    }

    [Fact]
    public async Task Telemetry_ShouldIncrementOperationsCounterWithSuccessOutcomeAsync()
    {
        using var diagnostics = new ExecutionDiagnostics(
            $"{ExecutionDiagnosticNames.SourceName}.Test.{Guid.NewGuid():N}");
        using var listener = new MeterSnapshot(diagnostics.SourceName);
        var sessionFactory = new TrackingSessionFactory();
        await using var worker = new ExecutionWorker<TestSession>(
            sessionFactory,
            new ExecutionWorkerOptions(name: "Telemetry Worker", diagnostics: diagnostics));

        _ = await worker.ExecuteAsync(
            static (session, _) => session.SessionId,
            cancellationToken: CancellationToken.None);

        listener.Collect();
        var success = listener.Sum(
            ExecutionDiagnosticNames.MetricOperations,
            (ExecutionDiagnosticNames.TagWorkerName, "Telemetry Worker"),
            (ExecutionDiagnosticNames.TagOutcome, ExecutionDiagnosticNames.OutcomeSuccess));

        success.Should().Be(1);
    }

    [Fact]
    public async Task Telemetry_ShouldIncrementOperationsCounterWithFaultedOutcomeAsync()
    {
        using var diagnostics = new ExecutionDiagnostics(
            $"{ExecutionDiagnosticNames.SourceName}.Test.{Guid.NewGuid():N}");
        using var listener = new MeterSnapshot(diagnostics.SourceName);
        var sessionFactory = new TrackingSessionFactory();
        await using var worker = new ExecutionWorker<TestSession>(
            sessionFactory,
            new ExecutionWorkerOptions(name: "Faulted Worker", diagnostics: diagnostics));

        Func<Task> action = async () => await worker.ExecuteAsync<int>(
            static (_, _) => throw new InvalidOperationException("boom"),
            cancellationToken: CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");

        listener.Collect();
        var faulted = listener.Sum(
            ExecutionDiagnosticNames.MetricOperations,
            (ExecutionDiagnosticNames.TagWorkerName, "Faulted Worker"),
            (ExecutionDiagnosticNames.TagOutcome, ExecutionDiagnosticNames.OutcomeFaulted));

        faulted.Should().Be(1);
    }

    [Fact]
    public async Task Telemetry_ShouldIncrementSessionRecyclesCounterForMaxOperationsAsync()
    {
        using var diagnostics = new ExecutionDiagnostics(
            $"{ExecutionDiagnosticNames.SourceName}.Test.{Guid.NewGuid():N}");
        using var listener = new MeterSnapshot(diagnostics.SourceName);
        var sessionFactory = new TrackingSessionFactory();
        await using var worker = new ExecutionWorker<TestSession>(
            sessionFactory,
            new ExecutionWorkerOptions(name: "Recycle Worker", maxOperationsPerSession: 1, diagnostics: diagnostics));

        _ = await worker.ExecuteAsync(
            static (session, _) => session.SessionId,
            cancellationToken: CancellationToken.None);
        _ = await worker.ExecuteAsync(
            static (session, _) => session.SessionId,
            cancellationToken: CancellationToken.None);

        listener.Collect();
        var recycles = listener.Sum(
            ExecutionDiagnosticNames.MetricSessionRecycles,
            (ExecutionDiagnosticNames.TagWorkerName, "Recycle Worker"),
            (ExecutionDiagnosticNames.TagRecycleReason, ExecutionDiagnosticNames.RecycleMaxOperations));

        recycles.Should().Be(2);
    }

    [Fact]
    public async Task Telemetry_ShouldIncrementSessionRecyclesCounterForFailureAsync()
    {
        using var diagnostics = new ExecutionDiagnostics(
            $"{ExecutionDiagnosticNames.SourceName}.Test.{Guid.NewGuid():N}");
        using var listener = new MeterSnapshot(diagnostics.SourceName);
        var sessionFactory = new TrackingSessionFactory();
        await using var worker = new ExecutionWorker<TestSession>(
            sessionFactory,
            new ExecutionWorkerOptions(name: "Recycle Fail Worker", diagnostics: diagnostics));

        Func<Task> action = async () => await worker.ExecuteAsync<int>(
            static (_, _) => throw new InvalidOperationException("boom"),
            new ExecutionRequestOptions(recycleSessionOnFailure: true),
            CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>();

        listener.Collect();
        var recycles = listener.Sum(
            ExecutionDiagnosticNames.MetricSessionRecycles,
            (ExecutionDiagnosticNames.TagWorkerName, "Recycle Fail Worker"),
            (ExecutionDiagnosticNames.TagRecycleReason, ExecutionDiagnosticNames.RecycleFailure));

        recycles.Should().Be(1);
    }

    [Fact]
    public async Task Telemetry_ShouldReportQueueDepthViaObservableGaugeAsync()
    {
        using var diagnostics = new ExecutionDiagnostics(
            $"{ExecutionDiagnosticNames.SourceName}.Test.{Guid.NewGuid():N}");
        using var listener = new MeterSnapshot(diagnostics.SourceName);
        var sessionFactory = new TrackingSessionFactory();
        await using var worker = new ExecutionWorker<TestSession>(
            sessionFactory,
            new ExecutionWorkerOptions(name: "Gauge Worker", diagnostics: diagnostics));

        using var releaseFirstAction = new ManualResetEventSlim();
        var enteredFirstAction = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

#pragma warning disable AsyncFixer04
        var firstTask = worker.ExecuteAsync(
            (session, cancellationToken) =>
            {
                enteredFirstAction.TrySetResult(null);
                releaseFirstAction.Wait(cancellationToken);
                return session.SessionId;
            },
            cancellationToken: CancellationToken.None);

        await enteredFirstAction.Task;

        var queued = new Task[2];
        for (var i = 0; i < queued.Length; i++)
        {
            queued[i] = worker.ExecuteAsync(
                static (session, _) => session.SessionId,
                cancellationToken: CancellationToken.None);
        }
#pragma warning restore AsyncFixer04

        await WaitUntilAsync(() => worker.QueueDepth == 2);

        listener.Collect();
        var depth = listener.Last(
            ExecutionDiagnosticNames.MetricQueueDepth,
            (ExecutionDiagnosticNames.TagWorkerName, "Gauge Worker"));
        depth.Should().Be(2);

        releaseFirstAction.Set();
        await firstTask;
        await Task.WhenAll(queued);
    }

    [Fact]
    public async Task Telemetry_ShouldStartActivityForExecutionAsync()
    {
        const string WorkerName = "Activity Worker Telemetry Test";
        using var diagnostics = new ExecutionDiagnostics(
            $"{ExecutionDiagnosticNames.SourceName}.Test.{Guid.NewGuid():N}");
        var recorded = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, diagnostics.SourceName, StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (string.Equals(activity.GetTagItem(ExecutionDiagnosticNames.TagWorkerName) as string, WorkerName, StringComparison.Ordinal))
                {
                    recorded.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        var sessionFactory = new TrackingSessionFactory();
        await using var worker = new ExecutionWorker<TestSession>(
            sessionFactory,
            new ExecutionWorkerOptions(name: WorkerName, diagnostics: diagnostics));

        _ = await worker.ExecuteAsync(
            static (session, _) => session.SessionId,
            cancellationToken: CancellationToken.None);

        await WaitUntilAsync(() => !recorded.IsEmpty);

        recorded.Should().ContainSingle();
        var activity = recorded.Single();
        activity.OperationName.Should().Be(ExecutionDiagnosticNames.ActivityExecute);
        activity.GetTagItem(ExecutionDiagnosticNames.TagWorkerName).Should().Be(WorkerName);
        activity.Status.Should().Be(ActivityStatusCode.Ok);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldHandleHighConcurrencyStressSubmissionsAsync()
    {
        const int SubmissionCount = 1_000;
        var sessionFactory = new TrackingSessionFactory();
        await using var worker = new ExecutionWorker<TestSession>(sessionFactory);

        var submissionTasks = new Task<int>[SubmissionCount];
        Parallel.For(
            0,
            SubmissionCount,
            index => submissionTasks[index] = worker.ExecuteAsync(
                static (session, _) => session.SessionId,
                cancellationToken: CancellationToken.None));

        var allTask = Task.WhenAll(submissionTasks);
        var completed = await Task.WhenAny(allTask, Task.Delay(TimeSpan.FromSeconds(30), CancellationToken.None));
        completed.Should().BeSameAs(allTask);

        var results = await allTask;
        results.Should().HaveCount(SubmissionCount);
        results.Should().OnlyContain(static sessionId => sessionId == 1);
        sessionFactory.CreateCount.Should().Be(1);
    }

    [Fact]
    public void ExecuteValueAsync_ShouldThrowWhenActionDelegateIsNull()
    {
        var sessionFactory = new TrackingSessionFactory();
        using var worker = new ExecutionWorker<TestSession>(sessionFactory);

        Action call = () => _ = worker.ExecuteValueAsync((Action<TestSession, CancellationToken>)null!);

        call.Should().Throw<ArgumentNullException>().WithParameterName("action");
    }

    [Fact]
    public void ExecuteValueAsyncOfTResult_ShouldThrowWhenActionDelegateIsNull()
    {
        var sessionFactory = new TrackingSessionFactory();
        using var worker = new ExecutionWorker<TestSession>(sessionFactory);

        Action call = () => _ = worker.ExecuteValueAsync<int>((Func<TestSession, CancellationToken, int>)null!);

        call.Should().Throw<ArgumentNullException>().WithParameterName("action");
    }

    [Fact]
#pragma warning disable AsyncFixer04
    public async Task ExecuteValueAsync_ShouldReturnCanceledWhenTokenIsAlreadyCanceledAsync()
    {
        var sessionFactory = new TrackingSessionFactory();
        using var cancellationTokenSource = new CancellationTokenSource();
        await using var worker = new ExecutionWorker<TestSession>(sessionFactory);

        await CancelAsync(cancellationTokenSource);

        var pending = worker.ExecuteValueAsync(
            static (_, _) => { },
            cancellationToken: cancellationTokenSource.Token);

        pending.IsCanceled.Should().BeTrue();
        sessionFactory.CreateCount.Should().Be(0);
    }
#pragma warning restore AsyncFixer04

    [Fact]
#pragma warning disable AsyncFixer04
    public async Task ExecuteValueAsyncOfTResult_ShouldReturnCanceledWhenTokenIsAlreadyCanceledAsync()
    {
        var sessionFactory = new TrackingSessionFactory();
        using var cancellationTokenSource = new CancellationTokenSource();
        await using var worker = new ExecutionWorker<TestSession>(sessionFactory);

        await CancelAsync(cancellationTokenSource);

        var pending = worker.ExecuteValueAsync(
            static (session, _) => session.SessionId,
            cancellationToken: cancellationTokenSource.Token);

        pending.IsCanceled.Should().BeTrue();
        sessionFactory.CreateCount.Should().Be(0);
    }
#pragma warning restore AsyncFixer04

    [Fact]
#pragma warning disable AsyncFixer04, xUnit1051
    public async Task ExecuteValueAsync_ShouldFailWhenChannelIsClosedAsync()
    {
        var sessionFactory = new TrackingSessionFactory();
        await using var worker = new ExecutionWorker<TestSession>(sessionFactory);
        worker.TryCompleteChannelForTesting().Should().BeTrue();

        var pending = worker.ExecuteValueAsync(
            static (_, _) => { },
            cancellationToken: CancellationToken.None);

        Func<Task> awaitFailure = async () => await pending;
        await awaitFailure.Should().ThrowAsync<ObjectDisposedException>();
    }
#pragma warning restore AsyncFixer04, xUnit1051

    [Fact]
#pragma warning disable AsyncFixer04, xUnit1051
    public async Task ExecuteValueAsyncOfTResult_ShouldFailWhenChannelIsClosedAsync()
    {
        var sessionFactory = new TrackingSessionFactory();
        await using var worker = new ExecutionWorker<TestSession>(sessionFactory);
        worker.TryCompleteChannelForTesting().Should().BeTrue();

        var pending = worker.ExecuteValueAsync(
            static (session, _) => session.SessionId,
            cancellationToken: CancellationToken.None);

        Func<Task> awaitFailure = async () => _ = await pending;
        await awaitFailure.Should().ThrowAsync<ObjectDisposedException>();
    }
#pragma warning restore AsyncFixer04, xUnit1051

    [Fact]
    public void ExecuteValueAsync_ShouldThrowObjectDisposedExceptionAfterDispose()
    {
#pragma warning disable IDISP016, IDISP017
        var sessionFactory = new TrackingSessionFactory();
        var worker = new ExecutionWorker<TestSession>(sessionFactory);
        worker.Dispose();

        Action call = () => _ = worker.ExecuteValueAsync(static (_, _) => { });
#pragma warning restore IDISP016, IDISP017

        call.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public Task InitializeAsync_ShouldReturnCancelledTaskForPreCancelledTokenAsync()
    {
        var sessionFactory = new TrackingSessionFactory();
        using var worker = new ExecutionWorker<TestSession>(sessionFactory);

        using var cts = new CancellationTokenSource();
#pragma warning disable VSTHRD103, S6966
        cts.Cancel();
#pragma warning restore VSTHRD103, S6966

        // AsyncFixer04 disabled: we deliberately do not await the returned
        // Task because the assertion is that a pre-cancelled token produces
        // a synchronously-cancelled Task.FromCanceled result.
#pragma warning disable AsyncFixer04
        var task = worker.InitializeAsync(cts.Token);
#pragma warning restore AsyncFixer04

        task.IsCanceled.Should().BeTrue();
        sessionFactory.CreateCount.Should().Be(0);
        return Task.CompletedTask;
    }

    [Fact]
    public void SetFatalFailure_ShouldRaiseWorkerFaultedEventOnlyOnce()
    {
        var sessionFactory = new TrackingSessionFactory();
        using var worker = new ExecutionWorker<TestSession>(sessionFactory);

        var faultCount = 0;
        worker.WorkerFaulted += (_, _) => Interlocked.Increment(ref faultCount);

        SetFatalFailure(worker, new InvalidOperationException("first"));
        SetFatalFailure(worker, new InvalidOperationException("second"));

        faultCount.Should().Be(1);
    }

    [Fact]
    public async Task ProcessWorkItem_ShouldSetActivityStatusForOkAndErrorOutcomesAsync()
    {
        var sourceName = "AdaskoTheBeAsT.Interop.Execution.Test.Activity." + Guid.NewGuid().ToString("N");
        var recordedStatuses = new ConcurrentBag<ActivityStatusCode>();

        // IDISP001/IDISP003/IDISP004/IDISP013/IDISP017 disabled: the diagnostics
        // scope, listener, and worker are manually disposed in finally blocks
        // to enforce teardown order (worker first so the activity loop drains,
        // then listener, then diagnostics).
#pragma warning disable IDISP001, IDISP003, IDISP004, IDISP013, IDISP017
        var diagnostics = new ExecutionDiagnostics(sourceName);
        var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, sourceName, StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => recordedStatuses.Add(activity.Status),
        };
        ActivitySource.AddActivityListener(listener);
        try
        {
            var sessionFactory = new TrackingSessionFactory();
            var worker = new ExecutionWorker<TestSession>(
                sessionFactory,
                new ExecutionWorkerOptions(diagnostics: diagnostics));
            try
            {
#pragma warning disable xUnit1051
                await worker.ExecuteAsync(static (_, _) => { });

                Func<Task> failing = () => worker.ExecuteAsync(
                    static (_, _) => throw new InvalidOperationException("boom"));
                await failing.Should().ThrowAsync<InvalidOperationException>();
#pragma warning restore xUnit1051

                await WaitUntilAsync(() => recordedStatuses.Count >= 2);
            }
            finally
            {
                await worker.DisposeAsync();
            }
        }
        finally
        {
            listener.Dispose();
            diagnostics.Dispose();
        }
#pragma warning restore IDISP001, IDISP003, IDISP004, IDISP013, IDISP017

        recordedStatuses.Should().Contain(ActivityStatusCode.Ok);
        recordedStatuses.Should().Contain(ActivityStatusCode.Error);
    }

    [Fact]
    public async Task Dispose_ShouldReturnWithinTimeoutWhenWorkerThreadIsBlockedAsync()
    {
        using var gate = new ManualResetEventSlim(false);
        var sessionFactory = new BlockingSessionFactory(gate);

        // This test deliberately exercises the synchronous worker Dispose
        // timeout branch by wedging the worker thread in a blocking session
        // factory; async disposal would bypass the timeout branch this test
        // is asserting on.
#pragma warning disable IDISP001, IDISP003, IDISP016, IDISP017, VSTHRD103, S6966, S125
        var worker = new ExecutionWorker<TestSession>(
            sessionFactory,
            new ExecutionWorkerOptions(disposeTimeout: TimeSpan.FromMilliseconds(100)));
        try
        {
            _ = worker.InitializeAsync(CancellationToken.None);
            await Task.Delay(50, CancellationToken.None);

            var stopwatch = Stopwatch.StartNew();
            Action call = () => worker.Dispose();
            call.Should().NotThrow();
            stopwatch.Stop();

            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        }
        finally
        {
            gate.Set();
            worker.Dispose();
        }
#pragma warning restore IDISP001, IDISP003, IDISP016, IDISP017, VSTHRD103, S6966, S125
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

    // A session factory whose CreateSession blocks on an externally-owned
    // ManualResetEventSlim and deliberately ignores the cancellation token.
    // Used to exercise the Dispose() timeout branch: the worker thread is
    // wedged inside CreateSession so _workerExitCompletionSource is never
    // signaled, causing disposeTask.Wait(timeout) to return false and
    // Dispose() to abandon the wait cleanly.
    private sealed class BlockingSessionFactory(ManualResetEventSlim gate) : IExecutionSessionFactory<TestSession>
    {
        public TestSession CreateSession(CancellationToken cancellationToken)
        {
            gate.Wait(CancellationToken.None);
            return new TestSession(1, Environment.CurrentManagedThreadId);
        }

        public void DisposeSession(TestSession session)
        {
            _ = session ?? throw new ArgumentNullException(nameof(session));
        }
    }
}
