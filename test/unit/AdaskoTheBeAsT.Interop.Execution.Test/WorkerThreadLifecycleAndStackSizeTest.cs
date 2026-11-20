using System.Collections.Concurrent;
using AwesomeAssertions;
using Xunit;

#pragma warning disable IDISP011, IDISP013, IDISP016, VSTHRD003, AsyncFixer04

namespace AdaskoTheBeAsT.Interop.Execution.Test;

public sealed class WorkerThreadLifecycleAndStackSizeTest
{
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(10);

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public void Options_Validate_And_Snapshot_ShouldCoverMaxStackSizeAndWorkerThreadCallbacks()
    {
        var workerInvalid = new ExecutionWorkerOptions { MaxStackSize = -1 };
        Action validateWorker = workerInvalid.Validate;
        validateWorker.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("MaxStackSize");

        var poolInvalid = new ExecutionWorkerPoolOptions(1) { MaxStackSize = -4096 };
        Action validatePool = poolInvalid.Validate;
        validatePool.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("MaxStackSize");

        var workerStarted = 0;
        var poolStartedSlot = -1;
        using var dummyScope = new TrackingScope(static () => { });

        var workerOpts = new ExecutionWorkerOptions
        {
            MaxStackSize = 524_288,
            OnWorkerThreadStart = () => Interlocked.Increment(ref workerStarted),
            CreateWorkerThreadScope = () => dummyScope,
        };
        var workerSnap = workerOpts.Snapshot();
        workerSnap.MaxStackSize.Should().Be(524_288);
        workerSnap.OnWorkerThreadStart!.Invoke();
        workerStarted.Should().Be(1);
        workerSnap.CreateWorkerThreadScope!.Invoke().Should().BeSameAs(dummyScope);

        var poolOpts = new ExecutionWorkerPoolOptions(2)
        {
            MaxStackSize = 1_048_576,
            OnWorkerThreadStart = slot => Interlocked.Exchange(ref poolStartedSlot, slot),
            CreateWorkerThreadScope = _ => dummyScope,
        };
        var poolSnap = poolOpts.Snapshot();
        poolSnap.MaxStackSize.Should().Be(1_048_576);
        poolSnap.OnWorkerThreadStart!.Invoke(1);
        poolStartedSlot.Should().Be(1);
        poolSnap.CreateWorkerThreadScope!.Invoke(0).Should().BeSameAs(dummyScope);
    }

    [Fact]
    public async Task Worker_LifecycleHooks_ShouldRunOnceOnDedicatedStaThreadSurviveRecyclesAndDisposeAfterSessionAsync()
    {
        var events = new ConcurrentQueue<(string Stage, int ThreadId, ApartmentState Apartment)>();
        var callerThreadId = Environment.CurrentManagedThreadId;

        var factory = new DelegateExecutionSessionFactory<object>(
            createSession: (ctx, _) =>
            {
                events.Enqueue(($"create:{ctx.SessionGeneration}", Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState()));
                return new object();
            },
            disposeSession: (_, ctx) =>
            {
                events.Enqueue(($"dispose:{ctx.SessionGeneration}", Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState()));
            });

        var options = CreateLifecycleWorkerOptions(events);
        await using (var worker = new ExecutionWorker<object>(factory, options))
        {
            await worker.InitializeAsync(TestToken);
            _ = await worker.ExecuteAsync(static (_, _) => 1, cancellationToken: TestToken);
            _ = await worker.ExecuteAsync(static (_, _) => 2, cancellationToken: TestToken);
        }

        AssertLifecycleEventOrderAndThreadAffinity(events.ToArray(), callerThreadId);
    }

    [Fact]
    public async Task Pool_LifecycleHooks_And_MaxStackSize_ShouldPropagatePerWorkerSlotAndAllowNullScopeAsync()
    {
        var startSlots = new ConcurrentDictionary<int, int>();
        var scopeDisposedSlots = new ConcurrentDictionary<int, int>();

        var factory = new DelegateExecutionSessionFactory<object>(
            static (ctx, _) =>
            {
                Span<byte> stackProbe = stackalloc byte[64 * 1024];
                stackProbe[0] = (byte)(ctx.WorkerIndex + 1);
                stackProbe[stackProbe.Length - 1] = stackProbe[0];
                return new object();
            });

        var poolOptions = new ExecutionWorkerPoolOptions(2, name: "LifecyclePool", useStaThread: true, maxOperationsPerSession: 1)
        {
            MaxStackSize = 2 * 1024 * 1024,
            SchedulingStrategy = SchedulingStrategy.RoundRobin,
            OnWorkerThreadStart = slot => startSlots[slot] = Environment.CurrentManagedThreadId,
            CreateWorkerThreadScope = slot => slot == 0
                ? new TrackingScope(() => scopeDisposedSlots[slot] = Environment.CurrentManagedThreadId)
                : null,
        };

        await using (var pool = new ExecutionWorkerPool<object>(factory, poolOptions))
        {
            await pool.InitializeAsync(TestToken);
            _ = await pool.ExecuteAsync(static (_, _) => 1, cancellationToken: TestToken);
            _ = await pool.ExecuteAsync(static (_, _) => 2, cancellationToken: TestToken);
            _ = await pool.ExecuteAsync(static (_, _) => 3, cancellationToken: TestToken);
        }

        startSlots.Keys.Should().BeEquivalentTo([0, 1]);
        startSlots[0].Should().NotBe(startSlots[1]);
        scopeDisposedSlots.Keys.Should().Equal(0);
        scopeDisposedSlots[0].Should().Be(startSlots[0]);
    }

    [Fact]
    public async Task Worker_StartupHookAndScopeFailures_ShouldFaultDeterministicallyAndCleanUpCreatedScopeAsync()
    {
        var startFailFactory = new DelegateExecutionSessionFactory<object>(static _ => new object());
        await using (var startFailWorker = new ExecutionWorker<object>(
            startFailFactory,
            new ExecutionWorkerOptions
            {
                OnWorkerThreadStart = static () => throw new InvalidOperationException("thread-start-boom"),
            }))
        {
            Func<Task> init = () => startFailWorker.InitializeAsync(TestToken);
            await init.Should().ThrowAsync<InvalidOperationException>().WithMessage("thread-start-boom");
            startFailWorker.GetSnapshot().IsFaulted.Should().BeTrue();
            startFailWorker.GetSnapshot().Fault!.Message.Should().Be("thread-start-boom");
        }

        await using (var scopeCreateFailWorker = new ExecutionWorker<object>(
            startFailFactory,
            new ExecutionWorkerOptions
            {
                CreateWorkerThreadScope = static () => throw new InvalidOperationException("scope-create-boom"),
            }))
        {
            Func<Task<int>> pending = () => scopeCreateFailWorker.ExecuteAsync(static (_, _) => 1, cancellationToken: TestToken);
            await pending.Should().ThrowAsync<InvalidOperationException>().WithMessage("scope-create-boom");
            scopeCreateFailWorker.GetSnapshot().Fault!.Message.Should().Be("scope-create-boom");
        }

        var scopeDisposedThreadId = 0;
        var sessionFailFactory = new DelegateExecutionSessionFactory<object>(
            static _ => throw new InvalidOperationException("session-create-boom"));
        await using (var sessionFailWorker = new ExecutionWorker<object>(
            sessionFailFactory,
            new ExecutionWorkerOptions
            {
                CreateWorkerThreadScope = () => new TrackingScope(() =>
                {
                    Interlocked.Exchange(ref scopeDisposedThreadId, Environment.CurrentManagedThreadId);
                    throw new InvalidOperationException("secondary-scope-dispose-boom");
                }),
            }))
        {
            Func<Task> init = () => sessionFailWorker.InitializeAsync(TestToken);
            await init.Should().ThrowAsync<InvalidOperationException>().WithMessage("session-create-boom");
            await sessionFailWorker.DisposeAsync();
            scopeDisposedThreadId.Should().NotBe(0);
            sessionFailWorker.Fault!.Message.Should().Be("session-create-boom");
        }
    }

    [Fact]
    public async Task Worker_ScopeDisposeFailureOnShutdown_ShouldLatchFaultCleanlyWithoutUnhandledExceptionAsync()
    {
        using var faultRaised = new ManualResetEventSlim();
        var factory = new DelegateExecutionSessionFactory<object>(static _ => new object());
        var worker = new ExecutionWorker<object>(
            factory,
            new ExecutionWorkerOptions(name: "ScopeFaultWorker")
            {
                CreateWorkerThreadScope = static () => new TrackingScope(
                    static () => throw new InvalidOperationException("scope-shutdown-boom")),
            });
        worker.WorkerFaulted += (_, _) => faultRaised.Set();

        await worker.InitializeAsync(TestToken);
        _ = await worker.ExecuteAsync(static (_, _) => 42, cancellationToken: TestToken);
        await worker.DisposeAsync();

        faultRaised.Wait(GateTimeout, TestToken).Should().BeTrue();
        var snapshot = worker.GetSnapshot();
        snapshot.IsFaulted.Should().BeTrue();
        snapshot.Fault.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be("scope-shutdown-boom");
    }

    private static ExecutionWorkerOptions CreateLifecycleWorkerOptions(
        ConcurrentQueue<(string Stage, int ThreadId, ApartmentState Apartment)> events) =>
        new(name: "LifecycleWorker", useStaThread: true, maxOperationsPerSession: 1)
        {
            MaxStackSize = 512 * 1024,
            BeforeThreadStart = () =>
                events.Enqueue(("before", Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState())),
            OnWorkerThreadStart = () =>
                events.Enqueue(("thread-start", Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState())),
            CreateWorkerThreadScope = () =>
            {
                events.Enqueue(("scope-create", Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState()));
                return new TrackingScope(() =>
                    events.Enqueue(("scope-dispose", Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState())));
            },
        };

    private static void AssertLifecycleEventOrderAndThreadAffinity(
        (string Stage, int ThreadId, ApartmentState Apartment)[] recorded,
        int callerThreadId)
    {
        recorded.Select(static e => e.Stage).Should().Equal(
            "before",
            "thread-start",
            "scope-create",
            "create:0",
            "dispose:0",
            "create:1",
            "dispose:1",
            "scope-dispose");

        recorded[0].ThreadId.Should().Be(callerThreadId);
        var workerThreadId = recorded[1].ThreadId;
        workerThreadId.Should().NotBe(callerThreadId);
        foreach (var entry in recorded.Skip(1))
        {
            entry.ThreadId.Should().Be(workerThreadId);
#if NET5_0_OR_GREATER
            if (OperatingSystem.IsWindows())
            {
                entry.Apartment.Should().Be(ApartmentState.STA);
            }
#else
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                entry.Apartment.Should().Be(ApartmentState.STA);
            }
#endif
        }
    }

    private sealed class TrackingScope(Action onDispose) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                onDispose?.Invoke();
            }
        }
    }
}
