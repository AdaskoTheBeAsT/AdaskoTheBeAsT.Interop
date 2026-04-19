namespace AdaskoTheBeAsT.Interop.Execution;

/// <summary>
/// Factory that produces and tears down the per-worker session objects consumed
/// by <see cref="IExecutionWorker{TSession}"/> and
/// <see cref="IExecutionWorkerPool{TSession}"/>. Implementations must be safe to
/// invoke from the dedicated worker <see cref="System.Threading.Thread"/>.
/// </summary>
/// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
public interface IExecutionSessionFactory<TSession>
    where TSession : class
{
    /// <summary>
    /// Creates a new session instance for a worker. Called on the worker thread
    /// lazily on first <c>ExecuteAsync</c> and again whenever the session is
    /// recycled (max-operations or recycle-on-failure).
    /// </summary>
    /// <param name="cancellationToken">Token signalled when the worker is disposed.</param>
    /// <returns>A fresh session instance.</returns>
    TSession CreateSession(CancellationToken cancellationToken);

    /// <summary>
    /// Releases native/unmanaged resources held by <paramref name="session"/>.
    /// Called on the worker thread either during recycle or worker shutdown.
    /// </summary>
    /// <param name="session">The session to tear down. Never <see langword="null"/>.</param>
    void DisposeSession(TSession session);
}
