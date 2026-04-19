using System.Threading.Tasks.Sources;
using AwesomeAssertions;
using Xunit;

// AsyncFixer01 disabled file-wide: the pooled work item tests deliberately
// hand-roll a single await over an Action<Task> lambda — the test assertion is
// the single-expression pattern the analyzer dislikes. xUnit1031 disabled
// file-wide: the pooled work items expose `GetResult(short)` methods that the
// analyzer mistakes for blocking Task.GetResult calls; they return TResult
// synchronously through MRVTSC and are the primary surface under test.
#pragma warning disable AsyncFixer01, xUnit1031

namespace AdaskoTheBeAsT.Interop.Execution.Test;

/// <summary>
/// Direct unit tests for the internal pooled value-task-source work items,
/// covering defensive / explicit-interface branches that are not reachable
/// via the public ExecuteValueAsync surface.
/// </summary>
public sealed class PooledExecutionWorkItemTest
{
    [Fact]
    public async Task PooledVoidWorkItem_TrySetCanceled_ShouldProduceCanceledValueTaskAsync()
    {
        var item = PooledVoidExecutionWorkItem<TestSession>.Rent(
            static (_, _) => { },
            ExecutionRequestOptions.Default,
            CancellationToken.None);

        item.TrySetCanceled();

        var valueTask = new ValueTask((IValueTaskSource)item, item.Version);
        Func<Task> awaitCall = async () => await valueTask;
        await awaitCall.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task PooledValueWorkItem_TrySetCanceled_ShouldProduceCanceledValueTaskAsync()
    {
        var item = PooledValueExecutionWorkItem<TestSession, int>.Rent(
            static (_, _) => 42,
            ExecutionRequestOptions.Default,
            CancellationToken.None);

        item.TrySetCanceled();

        var valueTask = new ValueTask<int>(item, item.Version);
        Func<Task> awaitCall = async () => await valueTask;
        await awaitCall.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void PooledValueWorkItem_GetResultWithMismatchedToken_ShouldDelegateToCoreWithoutRecycling()
    {
        var item = PooledValueExecutionWorkItem<TestSession, int>.Rent(
            static (_, _) => 123,
            ExecutionRequestOptions.Default,
            CancellationToken.None);

        item.Execute(new TestSession(1, Environment.CurrentManagedThreadId));

        // Any token != Version triggers the stale-token guard. The core
        // throws InvalidOperationException per the ManualResetValueTaskSourceCore
        // spec, and the item is NOT recycled so a legitimate awaiter can still
        // observe the result afterward.
        Action call = () => item.GetResult(unchecked((short)(item.Version + 1)));

        call.Should().Throw<InvalidOperationException>();

        item.GetResult(item.Version).Should().Be(123);
    }

    [Fact]
    public async Task PooledValueWorkItem_AsNonGenericValueTaskSource_ShouldCompleteSuccessfullyAsync()
    {
        var item = PooledValueExecutionWorkItem<TestSession, int>.Rent(
            static (_, _) => 7,
            ExecutionRequestOptions.Default,
            CancellationToken.None);

        item.Execute(new TestSession(1, Environment.CurrentManagedThreadId));

        var source = (IValueTaskSource)item;

        source.GetStatus(item.Version).Should().Be(ValueTaskSourceStatus.Succeeded);

        var continuationRan = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        source.OnCompleted(
            static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true),
            continuationRan,
            item.Version,
            ValueTaskSourceOnCompletedFlags.None);

        (await continuationRan.Task).Should().BeTrue();

        // Final observation via the non-generic GetResult recycles the item.
        source.GetResult(item.Version);
    }

    [Fact]
    public void PooledVoidWorkItem_Options_ShouldReturnInjectedInstance()
    {
        var options = new ExecutionRequestOptions(recycleSessionOnFailure: true);

        var item = PooledVoidExecutionWorkItem<TestSession>.Rent(
            static (_, _) => { },
            options,
            CancellationToken.None);

        item.Options.Should().BeSameAs(options);
        item.CancellationToken.Should().Be(CancellationToken.None);
    }

    [Fact]
    public void PooledValueWorkItem_Options_ShouldReturnInjectedInstance()
    {
        var options = new ExecutionRequestOptions(recycleSessionOnFailure: true);

        var item = PooledValueExecutionWorkItem<TestSession, int>.Rent(
            static (_, _) => 0,
            options,
            CancellationToken.None);

        item.Options.Should().BeSameAs(options);
        item.CancellationToken.Should().Be(CancellationToken.None);
    }

    [Fact]
    public async Task PooledVoidWorkItem_ExecuteWithNullAction_ShouldFaultValueTaskWithInvalidOperationAsync()
    {
        var item = PooledVoidExecutionWorkItem<TestSession>.Rent(
            action: null!,
            ExecutionRequestOptions.Default,
            CancellationToken.None);

        item.Execute(new TestSession(1, Environment.CurrentManagedThreadId));

        var valueTask = new ValueTask((IValueTaskSource)item, item.Version);
        Func<Task> awaitCall = async () => await valueTask;
        (await awaitCall.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("Work item action is unavailable.");
    }

    [Fact]
    public async Task PooledValueWorkItem_ExecuteWithNullAction_ShouldFaultValueTaskWithInvalidOperationAsync()
    {
        var item = PooledValueExecutionWorkItem<TestSession, int>.Rent(
            action: null!,
            ExecutionRequestOptions.Default,
            CancellationToken.None);

        item.Execute(new TestSession(1, Environment.CurrentManagedThreadId));

        var valueTask = new ValueTask<int>(item, item.Version);
        Func<Task<int>> awaitCall = async () => await valueTask;
        (await awaitCall.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("Work item action is unavailable.");
    }

    private sealed class TestSession(int sessionId, int ownerThreadId)
    {
        public int SessionId { get; } = sessionId;

        public int OwnerThreadId { get; } = ownerThreadId;
    }
}
