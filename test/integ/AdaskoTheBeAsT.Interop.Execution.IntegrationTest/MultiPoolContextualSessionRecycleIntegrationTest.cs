using System.Collections.Concurrent;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Execution.IntegrationTest;

public sealed class MultiPoolContextualSessionRecycleIntegrationTest
{
    private const int MinRecyclesPerSlot = 55;

    [Fact]
    public async Task TwoPools_WithOver50CountAndFaultRecyclesPerSlot_ShouldKeepStableSlotsAndMatchingGenerationsAsync()
    {
        var staFactory = new PoolRecordingContextualFactory();
        var mtaFactory = new PoolRecordingContextualFactory();

        await using (var staPool = new ExecutionWorkerPool<IntegrationSession>(
            staFactory,
            new ExecutionWorkerPoolOptions(
                workerCount: 4,
                name: "StaCalcPool",
                schedulingStrategy: SchedulingStrategy.RoundRobin,
                useStaThread: true,
                maxOperationsPerSession: 2)))
        await using (var mtaPool = new ExecutionWorkerPool<IntegrationSession>(
            mtaFactory,
            new ExecutionWorkerPoolOptions(
                workerCount: 3,
                name: "MtaCalcPool",
                schedulingStrategy: SchedulingStrategy.RoundRobin,
                useStaThread: false,
                maxOperationsPerSession: 2)))
        {
            await Task.WhenAll(
                staPool.InitializeAsync(TestCt.Current),
                mtaPool.InitializeAsync(TestCt.Current));

            await Task.WhenAll(
                DriveRecyclesAcrossAllSlotsAsync(staPool, workerCount: 4),
                DriveRecyclesAcrossAllSlotsAsync(mtaPool, workerCount: 3));
        }

        AssertPoolLifecycleInvariants(staFactory, expectedWorkerCount: 4, expectedPoolPrefix: "StaCalcPool #", expectStaOnWindows: true);
        AssertPoolLifecycleInvariants(mtaFactory, expectedWorkerCount: 3, expectedPoolPrefix: "MtaCalcPool #", expectStaOnWindows: false);
    }

    private static async Task DriveRecyclesAcrossAllSlotsAsync(
        IExecutionWorkerPool<IntegrationSession> pool,
        int workerCount)
    {
        // With RoundRobin and MaxOperationsPerSession = 2:
        // - Every round of `workerCount` faulted calls (RecycleSessionOnFailure = true) recycles each slot once via OperationFaulted.
        // - Every 2 rounds of `workerCount` normal calls recycles each slot once via MaxOperationsReached.
        // 15 faulted rounds + 84 normal rounds = 15 + 42 = 57 recycles per worker slot!
        for (var round = 0; round < 15; round++)
        {
            for (var slot = 0; slot < workerCount; slot++)
            {
                Func<Task<int>> faultOp = () => pool.ExecuteAsync<int>(
                    static (_, _) => throw new InvalidOperationException("simulated native fault"),
                    new ExecutionRequestOptions(recycleSessionOnFailure: true),
                    TestCt.Current);
                await faultOp.Should().ThrowAsync<InvalidOperationException>();
            }
        }

        for (var round = 0; round < 84; round++)
        {
            for (var slot = 0; slot < workerCount; slot++)
            {
                _ = await pool.ExecuteValueAsync(
                    static (session, _) => session.SessionId,
                    cancellationToken: TestCt.Current);
            }
        }
    }

    private static void AssertPoolLifecycleInvariants(
        PoolRecordingContextualFactory factory,
        int expectedWorkerCount,
        string expectedPoolPrefix,
        bool expectStaOnWindows)
    {
        var createdBySlot = factory.Created.ToArray()
            .GroupBy(static r => r.Context.WorkerIndex)
            .ToDictionary(static g => g.Key, static g => g.OrderBy(static r => r.Sequence).ToArray());
        var disposedBySlot = factory.Disposed.ToArray()
            .GroupBy(static r => r.Context.WorkerIndex)
            .ToDictionary(static g => g.Key, static g => g.OrderBy(static r => r.Sequence).ToArray());

        createdBySlot.Keys.Should().BeEquivalentTo(Enumerable.Range(0, expectedWorkerCount));
        disposedBySlot.Keys.Should().BeEquivalentTo(Enumerable.Range(0, expectedWorkerCount));

        for (var slot = 0; slot < expectedWorkerCount; slot++)
        {
            var created = createdBySlot[slot];
            var disposed = disposedBySlot[slot];

            created.Length.Should().BeGreaterThanOrEqualTo(MinRecyclesPerSlot + 1);
            disposed.Length.Should().Be(created.Length);

            var expectedThreadId = created[0].Context.ManagedThreadId;
            created[0].Context.PreviousRecycleReason.Should().BeNull();

            for (var gen = 0; gen < created.Length; gen++)
            {
                created[gen].Context.WorkerIndex.Should().Be(slot);
                created[gen].Context.WorkerCount.Should().Be(expectedWorkerCount);
                created[gen].Context.WorkerName.Should().StartWith(expectedPoolPrefix);
                created[gen].Context.SessionGeneration.Should().Be(gen);
                created[gen].Context.ManagedThreadId.Should().Be(expectedThreadId);

                disposed[gen].SessionId.Should().Be(created[gen].SessionId);
                disposed[gen].Context.Should().Be(created[gen].Context);

                if (expectStaOnWindows && IsWindowsPlatform())
                {
                    created[gen].Apartment.Should().Be(ApartmentState.STA);
                }
            }

            created.Select(static r => r.Context.PreviousRecycleReason)
                .Should().Contain(SessionRecycleReason.MaxOperationsReached);
            created.Select(static r => r.Context.PreviousRecycleReason)
                .Should().Contain(SessionRecycleReason.OperationFaulted);
        }
    }

    private static bool IsWindowsPlatform() =>
#if NET5_0_OR_GREATER
        OperatingSystem.IsWindows();
#else
        Environment.OSVersion.Platform == PlatformID.Win32NT;
#endif

    private readonly struct SlotEvent(
        long sequence,
        int sessionId,
        ApartmentState apartment,
        SessionLifecycleContext context)
    {
        public long Sequence { get; } = sequence;

        public int SessionId { get; } = sessionId;

        public ApartmentState Apartment { get; } = apartment;

        public SessionLifecycleContext Context { get; } = context;
    }

    private sealed class PoolRecordingContextualFactory : ContextualExecutionSessionFactory<IntegrationSession>
    {
        private int _nextSessionId;
        private long _sequence;

        public ConcurrentQueue<SlotEvent> Created { get; } = new();

        public ConcurrentQueue<SlotEvent> Disposed { get; } = new();

        public override IntegrationSession CreateSession(
            SessionLifecycleContext context,
            CancellationToken cancellationToken)
        {
            var id = Interlocked.Increment(ref _nextSessionId);
            var seq = Interlocked.Increment(ref _sequence);
            Created.Enqueue(new SlotEvent(seq, id, Thread.CurrentThread.GetApartmentState(), context));
            return new IntegrationSession(id, Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState());
        }

        public override void DisposeSession(
            IntegrationSession session,
            SessionLifecycleContext context)
        {
            var seq = Interlocked.Increment(ref _sequence);
            Disposed.Enqueue(new SlotEvent(seq, session.SessionId, Thread.CurrentThread.GetApartmentState(), context));
        }
    }
}
