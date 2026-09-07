namespace AdaskoTheBeAsT.Interop.Execution.Hosting;

// Cleanup belongs to the dedicated worker, not the host's synchronization
// context. Joining that externally completed task is this helper's purpose.
#pragma warning disable VSTHRD003
internal static class ShutdownWait
{
    internal static Task WaitAsync(Task cleanup, CancellationToken cancellationToken)
    {
#if NET8_0_OR_GREATER
        return cleanup.WaitAsync(cancellationToken);
#else
        return WaitCoreAsync(cleanup, cancellationToken);
#endif
    }

#if !NET8_0_OR_GREATER
    private static async Task WaitCoreAsync(Task cleanup, CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled || cleanup.IsCompleted)
        {
            await cleanup.ConfigureAwait(false);
            return;
        }

        var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (cancellationToken.Register(() => canceled.TrySetResult(true)))
        {
            if (await Task.WhenAny(cleanup, canceled.Task).ConfigureAwait(false) != cleanup)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        await cleanup.ConfigureAwait(false);
    }
#endif
}
#pragma warning restore VSTHRD003
