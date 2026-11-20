namespace AdaskoTheBeAsT.Interop.Execution;

/// <summary>
/// Context-aware extension of <see cref="IExecutionSessionFactory{TSession}"/>
/// that supplies stable worker slot identity and session generation metadata on
/// both creation and disposal.
/// </summary>
/// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
public interface IContextualExecutionSessionFactory<TSession> : IExecutionSessionFactory<TSession>
    where TSession : class
{
    /// <summary>
    /// Creates a new session instance for the worker slot described by
    /// <paramref name="context"/>. Invoked on the dedicated worker thread.
    /// </summary>
    /// <param name="context">Stable worker slot and session generation metadata.</param>
    /// <param name="cancellationToken">Token signalled when the worker is disposed or the triggering request cancels.</param>
    /// <returns>A fresh session instance.</returns>
    TSession CreateSession(SessionLifecycleContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Releases native or unmanaged resources held by <paramref name="session"/>.
    /// Invoked on the dedicated worker thread with the same slot and generation
    /// metadata passed when <paramref name="session"/> was created.
    /// </summary>
    /// <param name="session">The session to tear down. Never <see langword="null"/>.</param>
    /// <param name="context">Slot and generation metadata for <paramref name="session"/>.</param>
    void DisposeSession(TSession session, SessionLifecycleContext context);
}
