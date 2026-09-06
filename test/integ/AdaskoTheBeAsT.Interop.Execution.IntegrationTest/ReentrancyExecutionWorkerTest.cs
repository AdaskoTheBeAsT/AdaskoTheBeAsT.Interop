using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Execution.IntegrationTest;

public sealed class ReentrancyExecutionWorkerTest
{
    [Fact]
    public async Task Worker_ShouldAllowReentrantDisposeFromInsideWorkItemAsync()
    {
        var factory = new IntegrationSessionFactory();
        var worker = new ExecutionWorker<IntegrationSession>(factory);

        try
        {
            await worker.InitializeAsync(TestCt.Current);

            var reentrantDisposeCompleted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            _ = worker.ExecuteAsync(
                (_, _) =>
                {
                    worker.Dispose();
                    reentrantDisposeCompleted.TrySetResult(true);
                },
                cancellationToken: TestCt.Current);

            var completedFirst = await Task.WhenAny(
                reentrantDisposeCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(10)));

            completedFirst.Should().Be(
                reentrantDisposeCompleted.Task,
                "reentrant Dispose() from inside the worker delegate must return synchronously without deadlocking");

            await reentrantDisposeCompleted.Task;
        }
        finally
        {
            await worker.DisposeAsync();
        }

        factory.DisposeCount.Should().Be(
            1,
            "external DisposeAsync must join teardown even after reentrant Dispose()");
    }

    [Fact]
    public async Task Worker_ShouldAllowNestedExecuteAsyncFromOutsideDelegateAsync()
    {
        var factory = new IntegrationSessionFactory();
        await using var worker = new ExecutionWorker<IntegrationSession>(factory);

        var firstSessionId = await worker.ExecuteAsync(
            (session, _) => session.SessionId,
            cancellationToken: TestCt.Current);

        var secondSessionId = await worker.ExecuteAsync(
            (session, _) => session.SessionId,
            cancellationToken: TestCt.Current);

        firstSessionId.Should().Be(
            secondSessionId,
            "both submissions must see the same persistent session");
    }
}
