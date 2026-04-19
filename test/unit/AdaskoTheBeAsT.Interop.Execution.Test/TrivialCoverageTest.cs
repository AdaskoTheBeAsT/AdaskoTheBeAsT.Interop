using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Execution.Test;

// Focused null-guard / argument-range tests that fill in the small
// defensive branches otherwise only reachable from contrived setups.
// Each test is intentionally narrow so a failure pinpoints one branch.
//
// IDISP001 / IDISP004 / CA2000 disabled file-wide: schedulers return
// IExecutionWorker<T> which inherits IDisposable. Several tests intentionally
// invoke the scheduler via `Action action = () => _ = scheduler.SelectWorker(...)`
// where the scheduler throws before any worker is produced; the analyser
// cannot see past the throw and incorrectly flags the discard as leaking
// an IDisposable. The FakeWorker used by the positive paths is a pure
// stand-in with a no-op Dispose, so analyser IDISP hygiene warnings have no
// runtime consequence.
#pragma warning disable IDISP001, IDISP004, CA2000
public sealed class TrivialCoverageTest
{
    [Fact]
    public void ExecutionWorkerOptions_ShouldThrowForNegativeDisposeTimeout()
    {
        Action action = () => _ = new ExecutionWorkerOptions(disposeTimeout: TimeSpan.FromSeconds(-1));

        action.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be(nameof(ExecutionWorkerOptions.DisposeTimeout));
    }

    [Fact]
    public void ExecutionWorkerOptions_ShouldAcceptInfiniteTimeSpan()
    {
        var options = new ExecutionWorkerOptions(disposeTimeout: Timeout.InfiniteTimeSpan);

        options.DisposeTimeout.Should().Be(Timeout.InfiniteTimeSpan);
    }

    [Fact]
    public void ExecutionWorkerOptions_DefaultShouldReturnFreshInstanceEachCall()
    {
        var first = ExecutionWorkerOptions.Default;
        var second = ExecutionWorkerOptions.Default;

        first.Should().NotBeSameAs(second);
    }

    [Fact]
    public void ExecutionWorkerPoolOptions_ShouldThrowForNegativeDisposeTimeout()
    {
        Action action = () => _ = new ExecutionWorkerPoolOptions(
            workerCount: 1,
            disposeTimeout: TimeSpan.FromMilliseconds(-5));

        action.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be(nameof(ExecutionWorkerPoolOptions.DisposeTimeout));
    }

    [Fact]
    public void ExecutionWorkerPoolOptions_ShouldThrowForUndefinedSchedulingStrategy()
    {
        Action action = () => _ = new ExecutionWorkerPoolOptions(
            workerCount: 1,
            schedulingStrategy: (SchedulingStrategy)99);

        action.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be(nameof(ExecutionWorkerPoolOptions.SchedulingStrategy));
    }

    [Fact]
    public void WorkerFaultedEventArgs_ShouldThrowWhenExceptionIsNull()
    {
        const Exception? exception = null;
        Action action = () => _ = new WorkerFaultedEventArgs(exception!, workerName: "name");

        action.Should().Throw<ArgumentNullException>().WithParameterName(nameof(exception));
    }

    [Fact]
    public void WorkerFaultedEventArgs_ShouldCaptureExceptionAndName()
    {
        var ex = new InvalidOperationException("x");

        var args = new WorkerFaultedEventArgs(ex, "worker-1");

        args.Exception.Should().BeSameAs(ex);
        args.WorkerName.Should().Be("worker-1");
        args.FaultedAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ExecutionWorkerPoolSnapshot_ShouldFallBackToEmptyWhenWorkersIsNull()
    {
        var snapshot = new ExecutionWorkerPoolSnapshot("pool", workers: null!);

        snapshot.Name.Should().Be("pool");
        snapshot.WorkerCount.Should().Be(0);
        snapshot.QueueDepth.Should().Be(0);
        snapshot.IsAnyFaulted.Should().BeFalse();
        snapshot.Workers.Should().BeEmpty();
    }

    [Fact]
    public void ExecutionWorkerPoolSnapshot_ShouldAggregateQueueDepthAndFaultsAcrossWorkers()
    {
        var workers = new[]
        {
            new ExecutionWorkerSnapshot("w1", 2, isFaulted: false, fault: null),
            new ExecutionWorkerSnapshot("w2", 3, isFaulted: true, fault: new InvalidOperationException("x")),
        };

        var snapshot = new ExecutionWorkerPoolSnapshot("pool", workers);

        snapshot.WorkerCount.Should().Be(2);
        snapshot.QueueDepth.Should().Be(5);
        snapshot.IsAnyFaulted.Should().BeTrue();
    }

    [Fact]
    public void RoundRobinWorkerScheduler_ShouldThrowWhenWorkersIsNull()
    {
        var scheduler = new RoundRobinWorkerScheduler<object>();
        const IReadOnlyList<IExecutionWorker<object>>? workers = null;

        Action action = () => _ = scheduler.SelectWorker(workers!);

        action.Should().Throw<ArgumentNullException>().WithParameterName(nameof(workers));
    }

    [Fact]
    public void RoundRobinWorkerScheduler_ShouldThrowWhenWorkersIsEmpty()
    {
        var scheduler = new RoundRobinWorkerScheduler<object>();

        Action action = () => _ = scheduler.SelectWorker(Array.Empty<IExecutionWorker<object>>());

        action.Should().Throw<ArgumentException>().WithParameterName("workers");
    }

    [Fact]
    public void RoundRobinWorkerScheduler_ShouldShortCircuitForSingleWorker()
    {
        var scheduler = new RoundRobinWorkerScheduler<object>();
        using var worker = new FakeWorker();

        var selected = scheduler.SelectWorker(new[] { (IExecutionWorker<object>)worker });

        selected.Should().BeSameAs(worker);
    }

    [Fact]
    public void RoundRobinWorkerScheduler_ShouldReturnStartIndexWhenAllWorkersAreFaulted()
    {
        var scheduler = new RoundRobinWorkerScheduler<object>();
        using var w1 = new FakeWorker(isFaulted: true);
        using var w2 = new FakeWorker(isFaulted: true);
        using var w3 = new FakeWorker(isFaulted: true);
        var workers = new IExecutionWorker<object>[] { w1, w2, w3 };

        var selected = scheduler.SelectWorker(workers);

        workers.Should().Contain(selected);
    }

    [Fact]
    public void LeastQueuedWorkerScheduler_ShouldThrowWhenWorkersIsNull()
    {
        var scheduler = new LeastQueuedWorkerScheduler<object>();
        const IReadOnlyList<IExecutionWorker<object>>? workers = null;

        Action action = () => _ = scheduler.SelectWorker(workers!);

        action.Should().Throw<ArgumentNullException>().WithParameterName(nameof(workers));
    }

    [Fact]
    public void LeastQueuedWorkerScheduler_ShouldThrowWhenWorkersIsEmpty()
    {
        var scheduler = new LeastQueuedWorkerScheduler<object>();

        Action action = () => _ = scheduler.SelectWorker(Array.Empty<IExecutionWorker<object>>());

        action.Should().Throw<ArgumentException>().WithParameterName("workers");
    }

    [Fact]
    public void LeastQueuedWorkerScheduler_ShouldShortCircuitForSingleWorker()
    {
        var scheduler = new LeastQueuedWorkerScheduler<object>();
        using var worker = new FakeWorker();

        var selected = scheduler.SelectWorker(new[] { (IExecutionWorker<object>)worker });

        selected.Should().BeSameAs(worker);
    }

    [Fact]
    public void LeastQueuedWorkerScheduler_ShouldFallBackWhenAllWorkersAreFaulted()
    {
        var scheduler = new LeastQueuedWorkerScheduler<object>();
        using var w1 = new FakeWorker(isFaulted: true, queueDepth: 10);
        using var w2 = new FakeWorker(isFaulted: true, queueDepth: 1);
        using var w3 = new FakeWorker(isFaulted: true, queueDepth: 5);
        var workers = new IExecutionWorker<object>[] { w1, w2, w3 };

        var selected = scheduler.SelectWorker(workers);

        workers.Should().Contain(selected);
    }

    [Fact]
    public void LeastQueuedWorkerScheduler_ShouldPickHealthyWorkerWithLowestQueueDepth()
    {
        var scheduler = new LeastQueuedWorkerScheduler<object>();
        using var busy = new FakeWorker(isFaulted: false, queueDepth: 7);
        using var healthyLeast = new FakeWorker(isFaulted: false, queueDepth: 0);
        using var faulted = new FakeWorker(isFaulted: true, queueDepth: 0);
        var workers = new IExecutionWorker<object>[] { busy, healthyLeast, faulted };

        var selected = scheduler.SelectWorker(workers);

        selected.Should().BeSameAs(healthyLeast);
    }

    private sealed class FakeWorker(bool isFaulted = false, int queueDepth = 0) : IExecutionWorker<object>
    {
        public event EventHandler<WorkerFaultedEventArgs>? WorkerFaulted;

        public string? Name => null;

        public int QueueDepth { get; } = queueDepth;

        public bool IsFaulted { get; } = isFaulted;

        public Exception? Fault => null;

        public ExecutionWorkerSnapshot GetSnapshot() => new(null, QueueDepth, IsFaulted, Fault);

        public void Initialize()
        {
        }

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            // Reference field to silence the "unread" analyzer warning on WorkerFaulted.
            _ = WorkerFaulted;
            return Task.CompletedTask;
        }

        public Task ExecuteAsync(
            Action<object, CancellationToken> action,
            ExecutionRequestOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<TResult> ExecuteAsync<TResult>(
            Func<object, CancellationToken, TResult> action,
            ExecutionRequestOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(default(TResult)!);

        public ValueTask DisposeAsync() => default;

        public void Dispose()
        {
        }
    }
}
#pragma warning restore IDISP001, IDISP004, CA2000
