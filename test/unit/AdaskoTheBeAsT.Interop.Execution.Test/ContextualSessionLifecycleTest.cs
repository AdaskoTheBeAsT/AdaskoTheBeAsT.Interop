using System.Collections.Concurrent;
using AwesomeAssertions;
using Xunit;

#pragma warning disable IDISP013

namespace AdaskoTheBeAsT.Interop.Execution.Test;

public sealed class ContextualSessionLifecycleTest
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public void SessionLifecycleContext_ShouldStoreAndCompareAllProperties()
    {
        var first = new SessionLifecycleContext
        {
            WorkerIndex = 2,
            WorkerCount = 4,
            WorkerName = "Calc #3",
            SessionGeneration = 7,
            PreviousRecycleReason = SessionRecycleReason.MaxOperationsReached,
            ManagedThreadId = 42,
        };
        var second = first with { SessionGeneration = 8 };

        first.WorkerIndex.Should().Be(2);
        first.WorkerCount.Should().Be(4);
        first.WorkerName.Should().Be("Calc #3");
        first.SessionGeneration.Should().Be(7);
        first.PreviousRecycleReason.Should().Be(SessionRecycleReason.MaxOperationsReached);
        first.ManagedThreadId.Should().Be(42);
        second.Should().NotBe(first);
    }

    [Fact]
    public void ContextualExecutionSessionFactory_BaseClass_ShouldBridgeNonContextualInterfaceCalls()
    {
        var factory = new RecordingBaseContextualFactory();
        var nonContextual = (IExecutionSessionFactory<LifecycleSession>)factory;

        var session = nonContextual.CreateSession(CancellationToken.None);
        nonContextual.DisposeSession(session);

        factory.CreatedEvents.Should().ContainSingle();
        var created = factory.CreatedEvents.Single();
        created.Context.WorkerIndex.Should().Be(0);
        created.Context.WorkerCount.Should().Be(1);
        created.Context.WorkerName.Should().BeNull();
        created.Context.SessionGeneration.Should().Be(0);
        created.Context.PreviousRecycleReason.Should().BeNull();
        created.Context.ManagedThreadId.Should().Be(Environment.CurrentManagedThreadId);

        factory.DisposedEvents.Should().ContainSingle();
        factory.DisposedEvents.Single().SessionId.Should().Be(session.Id);
        factory.DisposedEvents.Single().Context.ManagedThreadId.Should().Be(Environment.CurrentManagedThreadId);
    }

    [Fact]
    public void ContextualExecutionSessionFactory_BaseClass_ShouldThrowWhenDisposedSessionIsNull()
    {
        var factory = (IExecutionSessionFactory<LifecycleSession>)new RecordingBaseContextualFactory();
        Action action = () => factory.DisposeSession(null!);

        action.Should().Throw<ArgumentNullException>().WithParameterName("session");
    }

    [Fact]
    public async Task Worker_ShouldPassMatchingGenerationAndRecycleReasonsAcrossCountFaultAndShutdownAsync()
    {
        var factory = new RecordingBaseContextualFactory();
        await using (var worker = new ExecutionWorker<LifecycleSession>(
            factory,
            new ExecutionWorkerOptions(name: "StandaloneCalc", maxOperationsPerSession: 2)))
        {
            await worker.InitializeAsync(TestToken);
            _ = await worker.ExecuteAsync(static (s, _) => s.Id, cancellationToken: TestToken);
            _ = await worker.ExecuteAsync(static (s, _) => s.Id, cancellationToken: TestToken);

            Func<Task<int>> faultedCall = () => worker.ExecuteAsync<int>(
                static (_, _) => throw new InvalidOperationException("boom"),
                new ExecutionRequestOptions(recycleSessionOnFailure: true),
                TestToken);
            await faultedCall.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");

            _ = await worker.ExecuteAsync(static (s, _) => s.Id, cancellationToken: TestToken);
        }

        var created = factory.CreatedEvents.ToArray();
        var disposed = factory.DisposedEvents.ToArray();

        created.Should().HaveCount(3);
        disposed.Should().HaveCount(3);

        AssertMatchingLifecyclePair(created[0], disposed[0], expectedGen: 0, expectedReason: null);
        AssertMatchingLifecyclePair(created[1], disposed[1], expectedGen: 1, expectedReason: SessionRecycleReason.MaxOperationsReached);
        AssertMatchingLifecyclePair(created[2], disposed[2], expectedGen: 2, expectedReason: SessionRecycleReason.OperationFaulted);
    }

    [Fact]
    public async Task Worker_ShouldNotIncrementGenerationOrInvokeDisposeWhenReplacementCreationIsCanceledAsync()
    {
        using var cancelReplacementCts = new CancellationTokenSource();
        var attempt = 0;
        var factory = new RecordingBaseContextualFactory(onCreate: (_, token) =>
        {
            var currentAttempt = Interlocked.Increment(ref attempt);
            if (currentAttempt == 2)
            {
                cancelReplacementCts.Cancel();
                token.ThrowIfCancellationRequested();
            }
        });

        await using (var worker = new ExecutionWorker<LifecycleSession>(
            factory,
            new ExecutionWorkerOptions(name: "CancelableReplace", maxOperationsPerSession: 1)))
        {
            _ = await worker.ExecuteAsync(static (s, _) => s.Id, cancellationToken: TestToken);

            Func<Task<int>> canceledCall = () => worker.ExecuteAsync(
                static (s, _) => s.Id,
                cancellationToken: cancelReplacementCts.Token);
            await canceledCall.Should().ThrowAsync<OperationCanceledException>();

            var replacementId = await worker.ExecuteAsync(static (s, _) => s.Id, cancellationToken: TestToken);
            replacementId.Should().Be(3);
        }

        var created = factory.CreatedEvents.ToArray();
        var disposed = factory.DisposedEvents.ToArray();

        created.Should().HaveCount(3);
        created[0].Context.SessionGeneration.Should().Be(0);
        created[1].Context.SessionGeneration.Should().Be(1);
        created[1].Context.PreviousRecycleReason.Should().Be(SessionRecycleReason.MaxOperationsReached);
        created[2].Context.SessionGeneration.Should().Be(1);
        created[2].Context.PreviousRecycleReason.Should().Be(SessionRecycleReason.MaxOperationsReached);

        disposed.Should().HaveCount(2);
        disposed[0].Context.SessionGeneration.Should().Be(0);
        disposed[1].Context.SessionGeneration.Should().Be(1);
    }

    [Fact]
    public async Task Worker_ShouldFaultWhenContextualFactoryReturnsNullWithoutDisposingNullSessionAsync()
    {
        var factory = new NullReturningContextualFactory();
        await using var worker = new ExecutionWorker<LifecycleSession>(factory);

        Func<Task> init = () => worker.InitializeAsync(TestToken);
        await init.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*returned null*");

        factory.DisposeCalled.Should().BeFalse();
        worker.IsFaulted.Should().BeTrue();
    }

    [Fact]
    public async Task Worker_ShouldFaultWhenContextualDisposeSessionThrowsOnRecycleAsync()
    {
        var factory = new RecordingBaseContextualFactory(
            onDispose: context =>
            {
                if (context.SessionGeneration == 0)
                {
                    throw new InvalidOperationException("contextual dispose failed");
                }
            });

        await using var worker = new ExecutionWorker<LifecycleSession>(
            factory,
            new ExecutionWorkerOptions(maxOperationsPerSession: 1));

        Func<Task<int>> call = () => worker.ExecuteAsync(static (s, _) => s.Id, cancellationToken: TestToken);
        await call.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("contextual dispose failed");

        worker.IsFaulted.Should().BeTrue();
    }

    [Fact]
    public async Task Pool_ShouldPassBoundedWorkerSlotsAndWorkerCountToSharedContextualFactoryAsync()
    {
        var factory = new RecordingBaseContextualFactory();
        await using (var pool = new ExecutionWorkerPool<LifecycleSession>(
            factory,
            new ExecutionWorkerPoolOptions(
                workerCount: 3,
                name: "EnginePool",
                schedulingStrategy: SchedulingStrategy.RoundRobin,
                maxOperationsPerSession: 1)))
        {
            await pool.InitializeAsync(TestToken);
            for (var i = 0; i < 6; i++)
            {
                _ = await pool.ExecuteAsync(static (s, _) => s.Id, cancellationToken: TestToken);
            }
        }

        var created = factory.CreatedEvents.ToArray();
        created.Should().NotBeEmpty();
        created.Should().AllSatisfy(evt =>
        {
            evt.Context.WorkerIndex.Should().BeInRange(0, 2);
            evt.Context.WorkerCount.Should().Be(3);
            evt.Context.WorkerName.Should().StartWith("EnginePool #");
        });
    }

    private static void AssertMatchingLifecyclePair(
        LifecycleRecord created,
        LifecycleRecord disposed,
        long expectedGen,
        SessionRecycleReason? expectedReason)
    {
        created.SessionId.Should().Be(disposed.SessionId);
        created.Context.WorkerIndex.Should().Be(0);
        created.Context.WorkerCount.Should().Be(1);
        created.Context.WorkerName.Should().Be("StandaloneCalc");
        created.Context.SessionGeneration.Should().Be(expectedGen);
        created.Context.PreviousRecycleReason.Should().Be(expectedReason);
        created.Context.ManagedThreadId.Should().BePositive();
        disposed.Context.Should().Be(created.Context);
    }

    private readonly record struct LifecycleRecord(int SessionId, SessionLifecycleContext Context);

    private sealed class LifecycleSession(int id)
    {
        public int Id { get; } = id;
    }

    private sealed class NullReturningContextualFactory : IContextualExecutionSessionFactory<LifecycleSession>
    {
        public bool DisposeCalled { get; private set; }

        public LifecycleSession CreateSession(CancellationToken cancellationToken) => null!;

        public LifecycleSession CreateSession(SessionLifecycleContext context, CancellationToken cancellationToken) => null!;

        public void DisposeSession(LifecycleSession session) => DisposeCalled = true;

        public void DisposeSession(LifecycleSession session, SessionLifecycleContext context) => DisposeCalled = true;
    }

    private sealed class RecordingBaseContextualFactory(
        Action<SessionLifecycleContext, CancellationToken>? onCreate = null,
        Action<SessionLifecycleContext>? onDispose = null)
        : ContextualExecutionSessionFactory<LifecycleSession>
    {
        private int _nextId;

        public ConcurrentQueue<LifecycleRecord> CreatedEvents { get; } = new();

        public ConcurrentQueue<LifecycleRecord> DisposedEvents { get; } = new();

        public override LifecycleSession CreateSession(
            SessionLifecycleContext context,
            CancellationToken cancellationToken)
        {
            var id = Interlocked.Increment(ref _nextId);
            CreatedEvents.Enqueue(new LifecycleRecord(id, context));
            onCreate?.Invoke(context, cancellationToken);
            return new LifecycleSession(id);
        }

        public override void DisposeSession(
            LifecycleSession session,
            SessionLifecycleContext context)
        {
            DisposedEvents.Enqueue(new LifecycleRecord(session.Id, context));
            onDispose?.Invoke(context);
        }
    }
}
