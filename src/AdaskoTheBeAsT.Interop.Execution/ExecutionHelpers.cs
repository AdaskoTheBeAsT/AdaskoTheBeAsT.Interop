namespace AdaskoTheBeAsT.Interop.Execution;

internal static class ExecutionHelpers
{
    internal static void TryIgnore(Action action)
    {
        var safeAction = action ?? throw new ArgumentNullException(nameof(action));

        try
        {
            safeAction();
        }
        catch (Exception)
        {
            GC.KeepAlive(safeAction);
        }
    }

    internal static Task WaitForStartupAsync(Task startupTask, CancellationToken cancellationToken)
    {
        // Safe to return a task started on a different thread (VSTHRD003 disabled): startupTask is
        // the worker's own startup TaskCompletionSource, produced by ExecutionWorker.Process on a
        // dedicated worker Thread. Awaiting it externally is the explicit contract of
        // ExecutionWorker.InitializeAsync, and the TCS is built with RunContinuationsAsynchronously
        // so no caller context is captured or inlined.
#pragma warning disable VSTHRD003
#if NET6_0_OR_GREATER
        return startupTask.WaitAsync(cancellationToken);
#else
        return WaitAsyncPolyfillCoreAsync(startupTask, cancellationToken);
#endif
#pragma warning restore VSTHRD003
    }

#if !NET6_0_OR_GREATER
    private static async Task WaitAsyncPolyfillCoreAsync(Task task, CancellationToken cancellationToken)
    {
        // Safe to await a task produced outside this method (VSTHRD003 disabled): the input `task`
        // is owned and completed by ExecutionWorker's dedicated worker Thread, and awaiting it from
        // InitializeAsync is the designed API contract. The TCS uses RunContinuationsAsynchronously
        // so continuations do not inline back onto the caller context.
#pragma warning disable VSTHRD003
        if (!cancellationToken.CanBeCanceled || task.IsCompleted)
        {
            await task.ConfigureAwait(false);
            return;
        }

        var cancellationTcs = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using (cancellationToken.Register(state => ((TaskCompletionSource<object?>)state!).TrySetCanceled(), cancellationTcs))
        {
            var completed = await Task.WhenAny(task, cancellationTcs.Task).ConfigureAwait(false);
            if (completed != task)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        await task.ConfigureAwait(false);
#pragma warning restore VSTHRD003
    }
#endif
}
