namespace AdaskoTheBeAsT.Interop.Execution;

/// <summary>
/// Contract shared by every work item enqueued onto
/// <see cref="ExecutionWorker{TSession}"/>. Kept internal because the item
/// instance lifecycle (including pooled recycling for the
/// <c>ExecuteValueAsync</c> hot path) is owned entirely by the library; the
/// public surface exposes only the returned <see cref="Task"/> or
/// <see cref="ValueTask"/>.
/// </summary>
/// <typeparam name="TSession">The session type exposed to the work item.</typeparam>
internal interface IExecutionWorkItem<in TSession>
    where TSession : class
{
    CancellationToken CancellationToken { get; }

    ExecutionRequestOptions Options { get; }

    void Execute(TSession session);

    void TrySetException(Exception exception);

    void TrySetCanceled();
}
