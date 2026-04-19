using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Execution.IntegrationTest;

public sealed class SnapshotAndSharedFactoryTest
{
    [Fact]
    public async Task Worker_GetSnapshot_ShouldReturnCoherentViewAsync()
    {
        var factory = new IntegrationSessionFactory();
        await using var worker = new ExecutionWorker<IntegrationSession>(
            factory,
            new ExecutionWorkerOptions(name: "Snapshot Worker"));

        await worker.InitializeAsync(TestCt.Current);
        await worker.ExecuteAsync((_, _) => { }, cancellationToken: TestCt.Current);

        var snapshot = worker.GetSnapshot();

        snapshot.Name.Should().Be("Snapshot Worker");
        snapshot.IsFaulted.Should().BeFalse();
        snapshot.Fault.Should().BeNull();
        snapshot.QueueDepth.Should().Be(0);
        worker.Name.Should().Be("Snapshot Worker");
    }

    [Fact]
    public async Task Pool_GetSnapshot_ShouldReflectAllWorkersAsync()
    {
        var factory = new IntegrationSessionFactory();
        await using var pool = new ExecutionWorkerPool<IntegrationSession>(
            factory,
            new ExecutionWorkerPoolOptions(workerCount: 3, name: "Shared Pool"));

        await pool.InitializeAsync(TestCt.Current);

        var snapshot = pool.GetSnapshot();

        snapshot.Name.Should().Be("Shared Pool");
        snapshot.WorkerCount.Should().Be(3);
        snapshot.IsAnyFaulted.Should().BeFalse();
        snapshot.Workers.Should().HaveCount(3);
        snapshot.Workers.Should().AllSatisfy(workerSnapshot =>
        {
            workerSnapshot.Name.Should().StartWith("Shared Pool #");
            workerSnapshot.IsFaulted.Should().BeFalse();
        });
        pool.Name.Should().Be("Shared Pool");
    }

    [Fact]
    public async Task Pool_SharedFactoryCtor_ShouldUseSameFactoryAcrossWorkersAsync()
    {
        var factory = new IntegrationSessionFactory();
        await using var pool = new ExecutionWorkerPool<IntegrationSession>(
            factory,
            new ExecutionWorkerPoolOptions(workerCount: 3));

        await pool.InitializeAsync(TestCt.Current);

        factory.CreateCount.Should().Be(3);
    }

    [Fact]
    public async Task Pool_GetSnapshot_ShouldAlignWithWorkerFaultsForPartialFailureAsync()
    {
        var healthyFactory = new IntegrationSessionFactory();
        var faultingFactory = new AlwaysFaultingSessionFactory();

#pragma warning disable IDISP013
        // The pool must be constructed and its InitializeAsync must be
        // awaited so that the partial-failure path runs. Disposal is
        // deferred to DisposeAsync after assertions.
        var pool = new ExecutionWorkerPool<IntegrationSession>(
            workerIndex => workerIndex == 1
                ? (IExecutionSessionFactory<IntegrationSession>)faultingFactory
                : healthyFactory,
            new ExecutionWorkerPoolOptions(workerCount: 3, name: "Partial Fault Pool"));
        try
        {
            Func<Task> initializeAction = () => pool.InitializeAsync(TestCt.Current);
            await initializeAction.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*worker factory failure*");

            var snapshot = pool.GetSnapshot();
            var faults = pool.WorkerFaults;

            snapshot.WorkerCount.Should().Be(3);
            snapshot.Workers.Should().HaveCount(3);
            faults.Should().HaveCount(3);

            snapshot.IsAnyFaulted.Should().BeTrue();
            pool.IsAnyFaulted.Should().BeTrue();

            for (var workerIndex = 0; workerIndex < snapshot.Workers.Count; workerIndex++)
            {
                var workerSnapshot = snapshot.Workers[workerIndex];
                var workerFault = faults[workerIndex];

                workerSnapshot.IsFaulted.Should().Be(
                    workerFault is not null,
                    $"snapshot.IsFaulted must match WorkerFaults[{workerIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)}] != null");

                if (workerFault is null)
                {
                    workerSnapshot.Fault.Should().BeNull();
                }
                else
                {
                    workerSnapshot.Fault.Should().BeSameAs(workerFault);
                }
            }

            snapshot.Workers[1].IsFaulted.Should().BeTrue();
            snapshot.Workers[1].Fault.Should().BeOfType<InvalidOperationException>()
                .Which.Message.Should().Contain("worker factory failure");
        }
        finally
        {
            await pool.DisposeAsync();
        }
#pragma warning restore IDISP013
    }

    private sealed class AlwaysFaultingSessionFactory : IExecutionSessionFactory<IntegrationSession>
    {
        public IntegrationSession CreateSession(CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("worker factory failure");
        }

        public void DisposeSession(IntegrationSession session)
        {
        }
    }
}
