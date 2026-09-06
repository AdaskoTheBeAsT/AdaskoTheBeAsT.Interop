using AdaskoTheBeAsT.Interop.Execution;
using AwesomeAssertions;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Execution.Hosting.Test;

public sealed class ShutdownDeadlineTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostDeadlineDuringShutdownMustNotCancelCleanupAsync(bool pool)
    {
        var cleanup = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = CreateService(cleanup.Task, pool);
        using var cancellation = new CancellationTokenSource();
        var stopping = service.StopAsync(cancellation.Token);
        try
        {
            stopping.IsCompleted.Should().BeFalse();
            await Task.Run(cancellation.Cancel, CancellationToken.None);
            var completed = await Task.WhenAny(stopping, Task.Delay(TimeSpan.FromSeconds(10), CancellationToken.None));
            completed.Should().BeSameAs(stopping);

            // The assertion awaits the existing host wait before the CTS is disposed.
#pragma warning disable VSTHRD003, IDISP013
            Func<Task> stop = () => stopping;
#pragma warning restore VSTHRD003, IDISP013
            await stop.Should().ThrowAsync<OperationCanceledException>();
            cleanup.Task.IsCompleted.Should().BeFalse();
        }
        finally
        {
            cleanup.TrySetResult(true);
        }

        await service.StopAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostCancellationShouldBoundTheWaitWithoutCompletingCleanupAsync(bool pool)
    {
        var cleanup = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = CreateService(cleanup.Task, pool);
        var token = new CancellationToken(canceled: true);
        var stopping = service.StopAsync(token);
        try
        {
            stopping.IsCompleted.Should().BeTrue("the host deadline is already canceled");

            // The assertion intentionally observes this already-started host wait.
#pragma warning disable VSTHRD003
            Func<Task> stop = () => stopping;
#pragma warning restore VSTHRD003
            await stop.Should().ThrowAsync<OperationCanceledException>();
            cleanup.Task.IsCompleted.Should().BeFalse();
        }
        finally
        {
            cleanup.TrySetResult(true);
        }

        await service.StopAsync(CancellationToken.None);
    }

    private static IHostedService CreateService(Task cleanup, bool pool)
    {
        if (pool)
        {
            var mock = new Mock<IExecutionWorkerPool<HostedTestSession>>(MockBehavior.Strict);
            mock.Setup(static worker => worker.DisposeAsync()).Returns(() => new ValueTask(cleanup));
            return new ExecutionWorkerPoolHostedService<HostedTestSession>(mock.Object);
        }

        var workerMock = new Mock<IExecutionWorker<HostedTestSession>>(MockBehavior.Strict);
        workerMock.Setup(static worker => worker.DisposeAsync()).Returns(() => new ValueTask(cleanup));
        return new ExecutionWorkerHostedService<HostedTestSession>(workerMock.Object);
    }
}
