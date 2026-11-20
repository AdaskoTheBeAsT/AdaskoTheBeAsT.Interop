namespace AdaskoTheBeAsT.Interop.Execution;

/// <summary>
/// Delegate-backed <see cref="ContextualExecutionSessionFactory{TSession}"/> that
/// creates and disposes per-worker sessions without requiring a bespoke factory class.
/// </summary>
/// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
public sealed class DelegateExecutionSessionFactory<TSession> : ContextualExecutionSessionFactory<TSession>
    where TSession : class
{
    private readonly Func<SessionLifecycleContext, CancellationToken, TSession> _createSession;
    private readonly Action<TSession, SessionLifecycleContext>? _disposeSession;

    /// <summary>
    /// Initializes a new instance of the <see cref="DelegateExecutionSessionFactory{TSession}"/> class
    /// using non-contextual creation and optional disposal delegates.
    /// </summary>
    /// <param name="createSession">Delegate invoked on the dedicated worker thread to create a session.</param>
    /// <param name="disposeSession">
    /// Optional delegate invoked on the dedicated worker thread to dispose a session.
    /// When <see langword="null"/>, <see cref="IDisposable.Dispose"/> is called if
    /// <typeparamref name="TSession"/> implements <see cref="IDisposable"/>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="createSession"/> is <see langword="null"/>.</exception>
    public DelegateExecutionSessionFactory(
        Func<CancellationToken, TSession> createSession,
        Action<TSession>? disposeSession = null)
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(createSession);
#else
        if (createSession is null)
        {
            throw new ArgumentNullException(nameof(createSession));
        }
#endif

        var create = createSession;
        var dispose = disposeSession;
        _createSession = (_, cancellationToken) => create(cancellationToken);
        _disposeSession = dispose is null ? null : (session, _) => dispose(session);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DelegateExecutionSessionFactory{TSession}"/> class
    /// using contextual creation and optional disposal delegates.
    /// </summary>
    /// <param name="createSession">
    /// Delegate invoked on the dedicated worker thread with the worker's <see cref="SessionLifecycleContext"/>
    /// to create a session.
    /// </param>
    /// <param name="disposeSession">
    /// Optional delegate invoked on the dedicated worker thread with the matching
    /// <see cref="SessionLifecycleContext"/> to dispose a session. When <see langword="null"/>,
    /// <see cref="IDisposable.Dispose"/> is called if <typeparamref name="TSession"/> implements
    /// <see cref="IDisposable"/>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="createSession"/> is <see langword="null"/>.</exception>
    public DelegateExecutionSessionFactory(
        Func<SessionLifecycleContext, CancellationToken, TSession> createSession,
        Action<TSession, SessionLifecycleContext>? disposeSession = null)
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(createSession);
#else
        if (createSession is null)
        {
            throw new ArgumentNullException(nameof(createSession));
        }
#endif

        _createSession = createSession;
        _disposeSession = disposeSession;
    }

    /// <inheritdoc />
    public override TSession CreateSession(SessionLifecycleContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _createSession(context, cancellationToken);
    }

    /// <inheritdoc />
    public override void DisposeSession(TSession session, SessionLifecycleContext context)
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(session);
#else
        if (session is null)
        {
            throw new ArgumentNullException(nameof(session));
        }
#endif

        if (_disposeSession is not null)
        {
            _disposeSession(session, context);
            return;
        }

        // IDISP007 disabled: session ownership belongs to ExecutionWorker, which
        // delegates lifecycle teardown exclusively through DisposeSession.
#pragma warning disable IDISP007
        (session as IDisposable)?.Dispose();
#pragma warning restore IDISP007
    }
}
