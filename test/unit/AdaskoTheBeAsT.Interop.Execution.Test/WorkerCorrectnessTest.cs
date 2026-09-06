using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.ExceptionServices;
using AwesomeAssertions;
using Xunit;

// These tests intentionally hold worker-owned tasks, call disposal repeatedly, and
// hand a Task<Task<int>> through a startup gate. Every task is awaited and every
// blocking gate is released in finally; the analyzers cannot model those protocols.
#pragma warning disable IDISP013, IDISP016, VSTHRD003, S5034, AsyncFixer04, AsyncFixer05

namespace AdaskoTheBeAsT.Interop.Execution.Test;

public sealed class WorkerCorrectnessTest
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    private static CancellationToken TestToken =>
#if NET8_0_OR_GREATER
        TestContext.Current.CancellationToken;
#else
        CancellationToken.None;
#endif

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedRequestShouldRecycleAndRecordFailureForBothApisAsync(bool pooled)
    {
        using var diagnostics = new ExecutionDiagnostics("recycle." + Guid.NewGuid());
        var outcomes = new ConcurrentBag<string>();
        using var listener = ListenForOutcomes(diagnostics, outcomes);
        var factory = new SessionFactory();
        await using var worker = new ExecutionWorker<Session>(
            factory, new ExecutionWorkerOptions(diagnostics: diagnostics));
        var first = await worker.ExecuteAsync(static (s, _) => s.Id, cancellationToken: TestToken);

        Func<Task<int>> fail = () => SubmitAsync(
            worker,
            static (_, _) => throw new InvalidOperationException("request"),
            pooled,
            new ExecutionRequestOptions(recycleSessionOnFailure: true));
        await fail.Should().ThrowAsync<InvalidOperationException>().WithMessage("request");

        var second = await worker.ExecuteAsync(static (s, _) => s.Id, cancellationToken: TestToken);
        await worker.DisposeAsync();
        second.Should().NotBe(first);
        outcomes.Count(static outcome => string.Equals(
            outcome, ExecutionDiagnosticNames.OutcomeFaulted, StringComparison.Ordinal)).Should().Be(1);
    }

    [Fact]
    public async Task PooledVoidFailureShouldRecycleAsync()
    {
        var factory = new SessionFactory();
        await using var worker = new ExecutionWorker<Session>(factory);
        var first = await worker.ExecuteAsync(static (s, _) => s.Id, cancellationToken: TestToken);
        Func<Task> fail = async () => await worker.ExecuteValueAsync(
            static (_, _) => throw new InvalidOperationException("void request"),
            new ExecutionRequestOptions(recycleSessionOnFailure: true),
            TestToken);
        await fail.Should().ThrowAsync<InvalidOperationException>();
        var second = await worker.ExecuteAsync(static (s, _) => s.Id, cancellationToken: TestToken);
        second.Should().NotBe(first);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResultMustNotBePublishedUntilPeriodicCleanupFinishesAsync(bool pooled)
    {
        using var cleanupEntered = new ManualResetEventSlim();
        using var releaseCleanup = new ManualResetEventSlim();
        var factory = new SessionFactory(onDispose: _ =>
        {
            cleanupEntered.Set();
            releaseCleanup.Wait(Deadline, TestToken).Should().BeTrue();
        });
        await using var worker = new ExecutionWorker<Session>(
            factory, new ExecutionWorkerOptions(maxOperationsPerSession: 1));
        var result = SubmitAsync(worker, static (s, _) => s.Id, pooled);
        try
        {
            await WaitForSignalAsync(cleanupEntered);
            result.IsCompleted.Should().BeFalse("the producer still owns the work item during cleanup");
        }
        finally
        {
            releaseCleanup.Set();
        }

        (await AwaitAsync(result)).Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PeriodicCleanupFailureShouldFaultTheRequestAndWorkerAsync(bool pooled)
    {
        var expected = new InvalidOperationException("cleanup");
        var factory = new SessionFactory(onDispose: _ => throw expected);
        await using var worker = new ExecutionWorker<Session>(
            factory, new ExecutionWorkerOptions(maxOperationsPerSession: 1));
        Func<Task<int>> action = () => SubmitAsync(worker, static (s, _) => s.Id, pooled);
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("cleanup");
        await worker.DisposeAsync();
        worker.Fault.Should().BeSameAs(expected);
        factory.CreateCount.Should().Be(1);
    }

    [Fact]
    public async Task ReplacementCreationFailureShouldBeTerminalAsync()
    {
        var expected = new InvalidOperationException("replacement");
        var factory = new SessionFactory(create: id => id == 2 ? throw expected : new Session(id));
        await using var worker = new ExecutionWorker<Session>(
            factory, new ExecutionWorkerOptions(maxOperationsPerSession: 1));
        _ = await worker.ExecuteAsync(static (s, _) => s.Id, cancellationToken: TestToken);
        Func<Task<int>> action = () => worker.ExecuteAsync(static (s, _) => s.Id, cancellationToken: TestToken);
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("replacement");
        await worker.DisposeAsync();
        worker.Fault.Should().BeSameAs(expected);
        factory.CreateCount.Should().Be(2);
    }

    [Fact]
    public async Task EveryExternalDisposeShouldAwaitTheSameExitAsync()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var factory = new SessionFactory();
        await using var worker = new ExecutionWorker<Session>(factory);
        var job = worker.ExecuteAsync(
            (s, _) =>
            {
                entered.Set();
                release.Wait(Deadline, TestToken).Should().BeTrue();
                return s.Id;
            },
            cancellationToken: TestToken);
        await WaitForSignalAsync(entered);
        var first = worker.DisposeAsync().AsTask();
        var second = worker.DisposeAsync().AsTask();
        try
        {
            first.IsCompleted.Should().BeFalse();
            second.IsCompleted.Should().BeFalse();
        }
        finally
        {
            release.Set();
        }

        await AwaitAsync(Task.WhenAll(first, second, job));
        factory.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task ReentrantPoolDisposeMustNotCompleteExternalDisposeAsync()
    {
        using var stopped = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var factory = new SessionFactory();
        await using var pool = new ExecutionWorkerPool<Session>(factory, new ExecutionWorkerPoolOptions(1));
        var job = pool.ExecuteAsync(
            (s, _) =>
            {
                pool.Dispose();
                stopped.Set();
                release.Wait(Deadline, TestToken).Should().BeTrue();
                return s.Id;
            },
            cancellationToken: TestToken);
        await WaitForSignalAsync(stopped);
        var exit = pool.DisposeAsync().AsTask();
        try
        {
            exit.IsCompleted.Should().BeFalse();
        }
        finally
        {
            release.Set();
        }

        await AwaitAsync(Task.WhenAll(exit, job));
        factory.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task FaultSubscribersShouldObserveTheFirstFaultOnThreadPoolAsync()
    {
        await using var worker = new ExecutionWorker<Session>(new SessionFactory());
        var expected = new InvalidOperationException("first");
        var observed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        worker.WorkerFaulted += (_, _) => observed.TrySetResult(
            Thread.CurrentThread.IsThreadPoolThread && ReferenceEquals(worker.Fault, expected));
        worker.SetFatalFailure(ExceptionDispatchInfo.Capture(expected));
        worker.SetFatalFailure(ExceptionDispatchInfo.Capture(new InvalidOperationException("second")));
        (await AwaitAsync(observed.Task)).Should().BeTrue();
        worker.Fault.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task ThrowingDiagnosticsMustNotOrphanOrFailTheRequestAsync()
    {
        using var diagnostics = new ExecutionDiagnostics("throwing-listener." + Guid.NewGuid());
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, diagnostics.SourceName, StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                throw new InvalidOperationException("listener"),
        };
        ActivitySource.AddActivityListener(listener);
        await using var worker = new ExecutionWorker<Session>(
            new SessionFactory(), new ExecutionWorkerOptions(diagnostics: diagnostics));
        var result = await AwaitAsync(worker.ExecuteAsync(static (s, _) => s.Id, cancellationToken: TestToken));
        result.Should().Be(1);
        worker.IsFaulted.Should().BeFalse();
    }

    [Fact]
    public void PartialPoolConstructionShouldUnregisterCreatedWorkers()
    {
        using var diagnostics = new ExecutionDiagnostics("constructor." + Guid.NewGuid());
        var registrations = 0;
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, receiver) =>
            {
                if (string.Equals(instrument.Meter.Name, diagnostics.SourceName, StringComparison.Ordinal))
                {
                    receiver.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<int>((_, _, _, _) => registrations++);
        listener.Start();
        Action create = () =>
        {
            using var pool = new ExecutionWorkerPool<Session>(
                index => index == 0 ? new SessionFactory() : throw new InvalidOperationException("factory"),
                new ExecutionWorkerPoolOptions(2, diagnostics: diagnostics));
        };
        create.Should().Throw<InvalidOperationException>().WithMessage("factory");
        listener.RecordObservableInstruments();
        registrations.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnrequestedCancellationExceptionShouldBeFaultedNotCanceledAsync(bool pooled)
    {
        await using var worker = new ExecutionWorker<Session>(new SessionFactory());
        var task = SubmitAsync(worker, static (_, _) => throw new OperationCanceledException("not requested"), pooled);
        Func<Task<int>> action = () => task;
        await action.Should().ThrowAsync<OperationCanceledException>();
        task.IsFaulted.Should().BeTrue();
        task.IsCanceled.Should().BeFalse();
    }

    [Fact]
    public async Task ColdSubmissionShouldReturnBeforeSessionCreationCompletesAsync()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var factory = new SessionFactory(create: id =>
        {
            entered.Set();
            release.Wait(Deadline, TestToken).Should().BeTrue();
            return new Session(id);
        });
        await using var worker = new ExecutionWorker<Session>(factory);
        var published = new TaskCompletionSource<Task<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var caller = Task.Run(
            () =>
            {
                var result = worker.ExecuteAsync(static (s, _) => s.Id, cancellationToken: TestToken);
                published.TrySetResult(result);
            },
            TestToken);
        try
        {
            await WaitForSignalAsync(entered);
            var result = await AwaitAsync(published.Task);
            result.IsCompleted.Should().BeFalse();
        }
        finally
        {
            release.Set();
        }

        await AwaitAsync(caller);
        (await AwaitAsync(await published.Task)).Should().Be(1);
    }

    [Theory]
    [InlineData(ExecutionShutdownMode.Drain)]
    [InlineData(ExecutionShutdownMode.CancelPending)]
    public async Task ShutdownModeShouldControlPendingRequestsAsync(ExecutionShutdownMode mode)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var options = new ExecutionWorkerOptions { ShutdownMode = mode };
        await using var worker = new ExecutionWorker<Session>(new SessionFactory(), options);
        var first = worker.ExecuteAsync(
            (s, _) =>
            {
                entered.Set();
                release.Wait(Deadline, TestToken).Should().BeTrue();
                return s.Id;
            },
            cancellationToken: TestToken);
        await WaitForSignalAsync(entered);
        var executed = false;
        var pending = worker.ExecuteAsync(
            (s, _) =>
            {
                executed = true;
                return s.Id;
            },
            cancellationToken: TestToken);
        var exit = worker.DisposeAsync().AsTask();
        release.Set();
        await AwaitAsync(first);
        await AwaitAsync(exit);
        executed.Should().Be(mode == ExecutionShutdownMode.Drain);
        pending.IsCanceled.Should().Be(mode == ExecutionShutdownMode.CancelPending);
    }

    [Fact]
    public async Task CapacityMustIncludeRequestsWaitingForStartupAsync()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var factory = new SessionFactory(create: id =>
        {
            entered.Set();
            release.Wait(Deadline, TestToken).Should().BeTrue();
            return new Session(id);
        });
        await using var worker = new ExecutionWorker<Session>(
            factory, new ExecutionWorkerOptions { QueueCapacity = 1 });
        var first = worker.ExecuteAsync(static (s, _) => s.Id, cancellationToken: TestToken);
        try
        {
            await WaitForSignalAsync(entered);
            Func<Task<int>> next = () => worker.ExecuteAsync(static (s, _) => s.Id, cancellationToken: TestToken);
            await next.Should().ThrowAsync<InvalidOperationException>().WithMessage("*capacity*");
            worker.QueueDepth.Should().Be(1);
        }
        finally
        {
            release.Set();
        }

        await AwaitAsync(first);
        worker.QueueDepth.Should().Be(0);
    }

    [Fact]
    public async Task WorkerShouldSnapshotOptionsAsync()
    {
        var options = new ExecutionWorkerOptions { Name = "original", MaxOperationsPerSession = 1 };
        await using var worker = new ExecutionWorker<Session>(new SessionFactory(), options);
        options.Name = "mutated";
        options.MaxOperationsPerSession = 0;
        var first = await worker.ExecuteAsync(static (s, _) => s.Id, cancellationToken: TestToken);
        var second = await worker.ExecuteAsync(static (s, _) => s.Id, cancellationToken: TestToken);
        worker.Name.Should().Be("original");
        second.Should().NotBe(first);
    }

    [Fact]
    public async Task QueueDepthMustNeverBeNegativeAsync()
    {
        using var diagnostics = new ExecutionDiagnostics("queue." + Guid.NewGuid());
        await using var worker = new ExecutionWorker<Session>(
            new SessionFactory(), new ExecutionWorkerOptions(diagnostics: diagnostics));
        var minimum = 0;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, diagnostics.SourceName, StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
            {
                minimum = Math.Min(minimum, worker.QueueDepth);
                return ActivitySamplingResult.None;
            },
        };
        ActivitySource.AddActivityListener(listener);
        for (var i = 0; i < 2000; i++)
        {
            _ = await worker.ExecuteAsync(static (s, _) => s.Id, cancellationToken: TestToken);
        }

        minimum.Should().Be(0);
        worker.QueueDepth.Should().Be(0);
    }

    [Fact]
    public async Task ExecutionActivityShouldUseSubmittingParentAsync()
    {
        using var diagnostics = new ExecutionDiagnostics("parent." + Guid.NewGuid());
        using var source = new ActivitySource(diagnostics.SourceName + ".caller");
        var parents = new ConcurrentBag<ActivitySpanId>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = activitySource => activitySource.Name.StartsWith(diagnostics.SourceName, StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (string.Equals(activity.Source.Name, diagnostics.SourceName, StringComparison.Ordinal))
                {
                    parents.Add(activity.ParentSpanId);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        await using var worker = new ExecutionWorker<Session>(
            new SessionFactory(), new ExecutionWorkerOptions(diagnostics: diagnostics));
        await worker.InitializeAsync(TestToken);
        using var parent = source.StartActivity("caller");
        parent.Should().NotBeNull();
        _ = await worker.ExecuteAsync(static (s, _) => s.Id, cancellationToken: TestToken);
        parents.Should().ContainSingle().Which.Should().Be(parent.SpanId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartupWaitCancellationMustNotStopSharedInitializationAsync(bool pooled)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var factory = new SessionFactory(create: id =>
        {
            entered.Set();
            release.Wait(Deadline, TestToken).Should().BeTrue();
            return new Session(id);
        });
        await using var worker = new ExecutionWorker<Session>(factory);
        await using var pool = new ExecutionWorkerPool<Session>(factory, new ExecutionWorkerPoolOptions(1));
        var abandoned = pooled ? pool.InitializeAsync(cancellation.Token) : worker.InitializeAsync(cancellation.Token);
        try
        {
            await WaitForSignalAsync(entered);
            await Task.Run(cancellation.Cancel, TestToken);
            Func<Task> wait = () => AwaitAsync(abandoned);
            await wait.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            release.Set();
        }

        await AwaitAsync(pooled ? pool.InitializeAsync(TestToken) : worker.InitializeAsync(TestToken));
        factory.CreateCount.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartupFailureMustSettleEveryAdmittedRequestAsync(bool pooled)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var expected = new InvalidOperationException("startup");
        var factory = new SessionFactory(create: _ =>
        {
            entered.Set();
            release.Wait(Deadline, TestToken).Should().BeTrue();
            throw expected;
        });
        await using var worker = new ExecutionWorker<Session>(factory);
        var first = SubmitAsync(worker, static (s, _) => s.Id, pooled);
        var second = SubmitAsync(worker, static (s, _) => s.Id, pooled);
        try
        {
            await WaitForSignalAsync(entered);
            worker.QueueDepth.Should().Be(2);
        }
        finally
        {
            release.Set();
        }

        Func<Task<int[]>> wait = () => AwaitAsync(Task.WhenAll(first, second));
        await wait.Should().ThrowAsync<InvalidOperationException>().WithMessage("startup");
        await worker.DisposeAsync();
        first.IsFaulted.Should().BeTrue();
        second.IsFaulted.Should().BeTrue();
        worker.Fault.Should().BeSameAs(expected);
        worker.QueueDepth.Should().Be(0);
    }

    [Fact]
    public async Task RequestsAdmittedDuringStartupMustKeepFifoOrderAsync()
    {
        using var release = new ManualResetEventSlim();
        var order = new List<int>();
        var factory = new SessionFactory(create: id =>
        {
            release.Wait(Deadline, TestToken).Should().BeTrue();
            return new Session(id);
        });
        await using var worker = new ExecutionWorker<Session>(factory);
        var requests = new Task<int>[20];
        try
        {
            for (var index = 0; index < requests.Length; index++)
            {
                var value = index;
                requests[index] = SubmitAsync(
                    worker,
                    (_, _) =>
                    {
                        order.Add(value);
                        return value;
                    },
                    pooled: index % 2 == 0);
            }
        }
        finally
        {
            release.Set();
        }

        await AwaitAsync(Task.WhenAll(requests));
        order.Should().Equal(Enumerable.Range(0, requests.Length));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SynchronousTimeoutMustLeaveAsyncJoinPendingAsync(bool pooled)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var factory = new SessionFactory();
        await using var worker = new ExecutionWorker<Session>(
            factory, new ExecutionWorkerOptions(disposeTimeout: TimeSpan.Zero));
        await using var pool = new ExecutionWorkerPool<Session>(
            factory, new ExecutionWorkerPoolOptions(1, disposeTimeout: TimeSpan.Zero));
        int Execute(Session session, CancellationToken token)
        {
            entered.Set();
            release.Wait(Deadline, token).Should().BeTrue();
            return session.Id;
        }

        var job = pooled
            ? pool.ExecuteAsync(Execute, cancellationToken: TestToken)
            : worker.ExecuteAsync(Execute, cancellationToken: TestToken);
        try
        {
            await WaitForSignalAsync(entered);
            await AwaitAsync(Task.Run(pooled ? pool.Dispose : worker.Dispose, TestToken));
            var exit = pooled ? pool.DisposeAsync().AsTask() : worker.DisposeAsync().AsTask();
            exit.IsCompleted.Should().BeFalse();
            factory.DisposeCount.Should().Be(0);
        }
        finally
        {
            release.Set();
        }

        await AwaitAsync(job);
        await AwaitAsync(pooled ? pool.DisposeAsync().AsTask() : worker.DisposeAsync().AsTask());
        factory.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task PoolMustForwardFinalTeardownFailuresAsync()
    {
        var expected = new InvalidOperationException("final teardown");
        var observed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var pool = new ExecutionWorkerPool<Session>(
            new SessionFactory(onDispose: _ => throw expected), new ExecutionWorkerPoolOptions(1));
        pool.WorkerFaulted += (_, args) => observed.TrySetResult(args.Exception);
        await pool.InitializeAsync(TestToken);
        await pool.DisposeAsync();
        (await AwaitAsync(observed.Task)).Should().BeSameAs(expected);
        pool.IsAnyFaulted.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PoolMustRejectAConcreteForeignWorkerAsync(bool pooled)
    {
        await using var foreign = new ExecutionWorker<Session>(new SessionFactory());
        await using var pool = new ExecutionWorkerPool<Session>(
            new SessionFactory(), new ExecutionWorkerPoolOptions(1), new ForeignScheduler(foreign));
        Func<Task<int>> submit = () => pooled
            ? pool.ExecuteValueAsync(static (s, _) => s.Id, cancellationToken: TestToken).AsTask()
            : pool.ExecuteAsync(static (s, _) => s.Id, cancellationToken: TestToken);
        await submit.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not owned*");
    }

    [Fact]
    public async Task BlockingStartupCancellationCallbackMustNotBlockDisposeAsync()
    {
        using var entered = new ManualResetEventSlim();
        using var releaseStartup = new ManualResetEventSlim();
        using var callbackEntered = new ManualResetEventSlim();
        using var releaseCallback = new ManualResetEventSlim();
        var callbackFinished = 0;
        var factory = new SessionFactory(onCreate: token =>
        {
            using var registration = token.Register(() =>
            {
                callbackEntered.Set();
                try
                {
                    releaseCallback.Wait(Deadline, TestToken).Should().BeTrue();
                }
                finally
                {
                    Interlocked.Exchange(ref callbackFinished, 1);
                }
            });
            entered.Set();
            releaseStartup.Wait(Deadline, TestToken).Should().BeTrue();
        });
        await using var worker = new ExecutionWorker<Session>(factory);
        var startup = worker.InitializeAsync(TestToken);
        var published = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await WaitForSignalAsync(entered);
            var stopping = Task.Run(() => published.TrySetResult(worker.DisposeAsync().AsTask()), TestToken);
            await WaitForSignalAsync(callbackEntered);
            var exit = await AwaitAsync(published.Task);
            Volatile.Read(ref callbackFinished).Should().Be(0);
            exit.IsCompleted.Should().BeFalse();
            await AwaitAsync(stopping);
        }
        finally
        {
            releaseCallback.Set();
            releaseStartup.Set();
        }

        await AwaitAsync(startup);
        await worker.DisposeAsync();
    }

    [Theory]
    [InlineData(-1, ExecutionShutdownMode.Drain, 0)]
    [InlineData(0, (ExecutionShutdownMode)9, 0)]
    [InlineData(0, ExecutionShutdownMode.Drain, -2)]
    [InlineData(0, ExecutionShutdownMode.Drain, 2147483648L)]
    public void InvalidAdmissionAndShutdownOptionsMustBeRejected(int capacity, ExecutionShutdownMode mode, long milliseconds)
    {
        var workerOptions = new ExecutionWorkerOptions
        {
            QueueCapacity = capacity,
            ShutdownMode = mode,
            DisposeTimeout = TimeSpan.FromMilliseconds(milliseconds),
        };
        var poolOptions = new ExecutionWorkerPoolOptions
        {
            QueueCapacity = capacity,
            ShutdownMode = mode,
            DisposeTimeout = TimeSpan.FromMilliseconds(milliseconds),
        };
        Action validateWorker = workerOptions.Validate;
        Action validatePool = poolOptions.Validate;
        validateWorker.Should().Throw<ArgumentOutOfRangeException>();
        validatePool.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static Task<int> SubmitAsync(
        ExecutionWorker<Session> worker,
        Func<Session, CancellationToken, int> action,
        bool pooled,
        ExecutionRequestOptions? options = null) =>
        pooled
            ? worker.ExecuteValueAsync(action, options, TestToken).AsTask()
            : worker.ExecuteAsync(action, options, TestToken);

    private static MeterListener ListenForOutcomes(ExecutionDiagnostics diagnostics, ConcurrentBag<string> outcomes)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, receiver) =>
            {
                if (string.Equals(instrument.Meter.Name, diagnostics.SourceName, StringComparison.Ordinal) &&
                    string.Equals(instrument.Name, ExecutionDiagnosticNames.MetricOperations, StringComparison.Ordinal))
                {
                    receiver.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (string.Equals(tag.Key, ExecutionDiagnosticNames.TagOutcome, StringComparison.Ordinal) &&
                    tag.Value is string outcome)
                {
                    outcomes.Add(outcome);
                }
            }
        });
        listener.Start();
        return listener;
    }

    private static Task WaitForSignalAsync(ManualResetEventSlim signal) =>
        Task.Run(() => signal.Wait(Deadline, TestToken).Should().BeTrue(), TestToken);

    private static async Task AwaitAsync(Task task)
    {
        var completed = await Task.WhenAny(task, Task.Delay(Deadline, TestToken));
        completed.Should().BeSameAs(task);
        await task;
    }

    private static async Task<T> AwaitAsync<T>(Task<T> task)
    {
        await AwaitAsync((Task)task);
        return await task;
    }

    private sealed class ForeignScheduler(IExecutionWorker<Session> foreign) : IWorkerScheduler<Session>
    {
        public IExecutionWorker<Session> SelectWorker(IReadOnlyList<IExecutionWorker<Session>> workers) => foreign;
    }

    private sealed class Session(int id)
    {
        public int Id { get; } = id;
    }

    private sealed class SessionFactory(
        Func<int, Session>? create = null,
        Action<Session>? onDispose = null,
        Action<CancellationToken>? onCreate = null) : IExecutionSessionFactory<Session>
    {
        private int _createCount;
        private int _disposeCount;

        public int CreateCount => Volatile.Read(ref _createCount);

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public Session CreateSession(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            onCreate?.Invoke(cancellationToken);
            var id = Interlocked.Increment(ref _createCount);
            return create?.Invoke(id) ?? new Session(id);
        }

        public void DisposeSession(Session session)
        {
            Interlocked.Increment(ref _disposeCount);
            onDispose?.Invoke(session);
        }
    }
}
