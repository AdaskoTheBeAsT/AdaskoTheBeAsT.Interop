using System.Collections.Concurrent;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Execution.IntegrationTest;

public sealed class MultiThreadedExecutionWorkerTest
{
    [Fact]
    public async Task Worker_ShouldSerialiseConcurrentSubmissionsOntoSingleSessionThreadAsync()
    {
        const int ConcurrentSubmitters = 32;
        const int SubmissionsPerSubmitter = 64;
        const int TotalSubmissions = ConcurrentSubmitters * SubmissionsPerSubmitter;

        var factory = new IntegrationSessionFactory();
        await using var worker = new ExecutionWorker<IntegrationSession>(factory);
        await worker.InitializeAsync(TestCt.Current);

        var observedThreadIds = new ConcurrentBag<int>();
        var observedSessionIds = new ConcurrentBag<int>();

        var submitterTasks = new Task[ConcurrentSubmitters];
        for (var submitterIndex = 0; submitterIndex < ConcurrentSubmitters; submitterIndex++)
        {
            submitterTasks[submitterIndex] = Task.Run(async () =>
            {
                for (var submissionIndex = 0; submissionIndex < SubmissionsPerSubmitter; submissionIndex++)
                {
                    await worker.ExecuteAsync(
                        (session, _) =>
                        {
                            observedThreadIds.Add(Environment.CurrentManagedThreadId);
                            observedSessionIds.Add(session.SessionId);
                        },
                        cancellationToken: TestCt.Current);
                }
            });
        }

        await Task.WhenAll(submitterTasks);

        observedThreadIds.Should().HaveCount(TotalSubmissions);
        observedSessionIds.Should().HaveCount(TotalSubmissions);
        observedThreadIds.Distinct().Should().ContainSingle(
            "all submissions must run on the single dedicated worker thread");
        observedSessionIds.Distinct().Should().ContainSingle(
            "the single session must be shared across all submissions");
        factory.CreateCount.Should().Be(1);
    }

    [Fact]
    public async Task Pool_ShouldFanOutAcrossWorkersAndKeepOrderPerWorkerAsync()
    {
        const int WorkerCount = 4;
        const int SubmissionsPerSubmitter = 200;
        const int SubmitterCount = 16;

        var factories = Enumerable
            .Range(0, WorkerCount)
            .Select(_ => new IntegrationSessionFactory())
            .ToArray();

        var poolOptions = new ExecutionWorkerPoolOptions(
            workerCount: WorkerCount,
            name: "integ-pool",
            schedulingStrategy: SchedulingStrategy.LeastQueued);

        await using var pool = new ExecutionWorkerPool<IntegrationSession>(
            workerIndex => factories[workerIndex],
            poolOptions);

        await pool.InitializeAsync(TestCt.Current);

        var perThreadCompletedCount = new ConcurrentDictionary<int, int>();

        var submitterTasks = new Task[SubmitterCount];
        for (var submitterIndex = 0; submitterIndex < SubmitterCount; submitterIndex++)
        {
            submitterTasks[submitterIndex] = Task.Run(async () =>
            {
                for (var submissionIndex = 0; submissionIndex < SubmissionsPerSubmitter; submissionIndex++)
                {
                    await pool.ExecuteAsync(
                        (session, _) =>
                        {
                            perThreadCompletedCount.AddOrUpdate(
                                session.OwnerThreadId,
                                1,
                                (_, currentCount) => currentCount + 1);
                        },
                        cancellationToken: TestCt.Current);
                }
            });
        }

        await Task.WhenAll(submitterTasks);

        const int TotalSubmissions = SubmitterCount * SubmissionsPerSubmitter;
        perThreadCompletedCount.Values.Sum().Should().Be(TotalSubmissions);
        perThreadCompletedCount.Keys.Should().HaveCount(
            WorkerCount,
            "each pool worker owns exactly one dedicated thread and every worker should have received work");
        factories.Sum(f => f.CreateCount).Should().Be(WorkerCount);
    }

    [Fact]
    public async Task Worker_ShouldPreserveSubmissionOrderPerSubmitterAsync()
    {
        const int SubmitterCount = 8;
        const int SubmissionsPerSubmitter = 50;

        var factory = new IntegrationSessionFactory();
        await using var worker = new ExecutionWorker<IntegrationSession>(factory);

        var perSubmitterSequences = new ConcurrentDictionary<int, List<int>>();
        for (var submitterIndex = 0; submitterIndex < SubmitterCount; submitterIndex++)
        {
            perSubmitterSequences[submitterIndex] = new List<int>(capacity: SubmissionsPerSubmitter);
        }

        var submitterTasks = new Task[SubmitterCount];
        for (var submitterIndex = 0; submitterIndex < SubmitterCount; submitterIndex++)
        {
            var capturedSubmitterIndex = submitterIndex;
            submitterTasks[submitterIndex] = Task.Run(async () =>
            {
                for (var submissionIndex = 0; submissionIndex < SubmissionsPerSubmitter; submissionIndex++)
                {
                    var capturedSubmissionIndex = submissionIndex;
                    await worker.ExecuteAsync(
                        (_, _) => perSubmitterSequences[capturedSubmitterIndex].Add(capturedSubmissionIndex),
                        cancellationToken: TestCt.Current);
                }
            });
        }

        await Task.WhenAll(submitterTasks);

        foreach (var kvp in perSubmitterSequences)
        {
            kvp.Value.Should().BeInAscendingOrder(
                "each submitter awaits its previous submission before enqueuing the next one, and the dedicated thread consumes the channel in FIFO order");
        }
    }
}
