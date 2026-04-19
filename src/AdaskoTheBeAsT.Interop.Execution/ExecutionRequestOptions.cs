namespace AdaskoTheBeAsT.Interop.Execution;

/// <summary>
/// Per-call tuning flags passed to <c>ExecuteAsync</c> overrides on
/// <see cref="IExecutionWorker{TSession}"/> and
/// <see cref="IExecutionWorkerPool{TSession}"/>.
/// </summary>
/// <param name="recycleSessionOnFailure">
/// When <see langword="true"/>, instructs the worker to tear down and rebuild
/// its session after the current work item throws. Defaults to
/// <see langword="false"/>.
/// </param>
public sealed class ExecutionRequestOptions(bool recycleSessionOnFailure = false)
{
    /// <summary>
    /// Gets a shared instance representing the default execution request
    /// options (no session recycle on failure).
    /// </summary>
    public static ExecutionRequestOptions Default { get; } = new();

    /// <summary>
    /// Gets a value indicating whether the session should be recycled when the
    /// work item faults.
    /// </summary>
    public bool RecycleSessionOnFailure { get; } = recycleSessionOnFailure;
}
