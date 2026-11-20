namespace AdaskoTheBeAsT.Interop.Execution;

/// <summary>
/// Identifies why the previous worker session was torn down before creating a
/// replacement session.
/// </summary>
public enum SessionRecycleReason
{
    /// <summary>
    /// The previous session reached <see cref="ExecutionWorkerOptions.MaxOperationsPerSession"/>.
    /// </summary>
    MaxOperationsReached = 0,

    /// <summary>
    /// A work item threw an unhandled exception while
    /// <see cref="ExecutionRequestOptions.RecycleSessionOnFailure"/> was enabled.
    /// </summary>
    OperationFaulted = 1,
}
