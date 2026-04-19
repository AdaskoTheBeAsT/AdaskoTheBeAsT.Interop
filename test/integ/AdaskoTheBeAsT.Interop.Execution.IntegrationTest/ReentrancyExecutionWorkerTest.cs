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

        // The outer DisposeAsync is idempotent (returns synchronously because
        // reentrant dispose already flipped _disposeState), so session disposal
        // happens asynchronously on the worker thread's finally block. Poll with
        // a bounded timeout to observe it.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (factory.DisposeCount < 1 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }

        factory.DisposeCount.Should().BeGreaterThanOrEqualTo(
            1,
            "the session must eventually be disposed on the worker thread's unwind after reentrant Dispose()");
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
