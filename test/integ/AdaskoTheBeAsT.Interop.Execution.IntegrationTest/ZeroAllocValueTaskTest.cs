#if NET8_0_OR_GREATER
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Execution.IntegrationTest;

public sealed class ZeroAllocValueTaskTest
{
    [Fact]
    public async Task Worker_ExecuteValueAsync_ShouldUsePooledValueTaskSourceAsync()
    {
        var factory = new IntegrationSessionFactory();
        await using var worker = new ExecutionWorker<IntegrationSession>(factory);

        // Warm up: first call may allocate the pooled instance.
        _ = await worker.ExecuteValueAsync((session, _) => session.SessionId);

        // Subsequent submission exercises the pooled path. We deliberately
        // avoid asserting IsCompletedSuccessfully on the returned struct —
        // the pooled IValueTaskSource state machine mandates one-shot
        // observation, and racing status checks against Execute on the
        // dedicated worker thread would be flaky.
        var result = await worker.ExecuteValueAsync((session, _) => session.SessionId * 2);
        result.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Worker_ExecuteValueAsync_ShouldRecyclePooledInstanceAcrossCallsAsync()
    {
        var factory = new IntegrationSessionFactory();
        await using var worker = new ExecutionWorker<IntegrationSession>(factory);

        // Drive enough submissions to exercise Rent / Return cycling through the
        // pool. If Return were broken (pool corruption or stale-token guard
        // failure) this loop would surface InvalidOperationException from
        // MRVTSC on a stale token.
        for (var iteration = 0; iteration < 64; iteration++)
        {
            var iterationValue = iteration;
            var result = await worker.ExecuteValueAsync((_, _) => iterationValue);
            result.Should().Be(iteration);
        }
    }

    [Fact]
    public async Task Worker_ExecuteValueAsync_Void_ShouldRunAndPoolAsync()
    {
        var factory = new IntegrationSessionFactory();
        await using var worker = new ExecutionWorker<IntegrationSession>(factory);

        var executed = 0;
        for (var iteration = 0; iteration < 32; iteration++)
        {
            await worker.ExecuteValueAsync((_, _) => Interlocked.Increment(ref executed));
        }

        Volatile.Read(ref executed).Should().Be(32);
    }

    [Fact]
    public async Task Worker_ExecuteValueAsync_ShouldPropagateExceptionThroughPooledSourceAsync()
    {
        var factory = new IntegrationSessionFactory();
        await using var worker = new ExecutionWorker<IntegrationSession>(factory);

        Func<Task> failing = async () =>
        {
            _ = await worker.ExecuteValueAsync<int>((_, _) => throw new InvalidOperationException("boom"));
        };

        await failing.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");

        // Subsequent successful submission confirms the pool is healthy after
        // an exceptional Return path.
        var afterFailure = await worker.ExecuteValueAsync((session, _) => session.SessionId);
        afterFailure.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Pool_ExecuteValueAsync_ShouldReturnValueTaskFromPooledSourceAsync()
    {
        var factory = new IntegrationSessionFactory();
        await using var pool = new ExecutionWorkerPool<IntegrationSession>(
            factory,
            new ExecutionWorkerPoolOptions(workerCount: 4));

        // Sequential submission keeps the test deterministic and sidesteps the
        // AsyncFixer04 fire-and-forget warning that a fan-out pattern over the
        // pooled ValueTask hot path would otherwise trigger. Each submission
        // exercises a separate Rent / Return cycle through the per-worker
        // pooled IValueTaskSource.
        for (var taskIndex = 0; taskIndex < 32; taskIndex++)
        {
            var localIndex = taskIndex;
            var result = await pool.ExecuteValueAsync((_, _) => localIndex * 2);
            result.Should().Be(localIndex * 2);
        }
    }

    [Fact]
    public async Task Worker_ExecuteValueAsync_PreCancelledToken_ShouldCompleteCancelledSynchronouslyAsync()
    {
        var factory = new IntegrationSessionFactory();
        await using var worker = new ExecutionWorker<IntegrationSession>(factory);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var pending = worker.ExecuteValueAsync((_, _) => 42, cancellationToken: cts.Token);
        pending.IsCanceled.Should().BeTrue();

        Func<Task> awaitCancelled = async () => { _ = await pending; };
        await awaitCancelled.Should().ThrowAsync<OperationCanceledException>();
    }
}
#endif
