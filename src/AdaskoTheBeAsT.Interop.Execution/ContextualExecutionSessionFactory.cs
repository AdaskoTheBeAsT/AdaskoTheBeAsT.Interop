namespace AdaskoTheBeAsT.Interop.Execution;

/// <summary>
/// Convenience base class for <see cref="IContextualExecutionSessionFactory{TSession}"/>
/// implementations that routes non-contextual <see cref="IExecutionSessionFactory{TSession}"/>
/// calls to the contextual methods with a standalone single-worker context.
/// </summary>
/// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
public abstract class ContextualExecutionSessionFactory<TSession> : IContextualExecutionSessionFactory<TSession>
    where TSession : class
{
    /// <inheritdoc />
    public abstract TSession CreateSession(SessionLifecycleContext context, CancellationToken cancellationToken);

    /// <inheritdoc />
    public abstract void DisposeSession(TSession session, SessionLifecycleContext context);

    /// <inheritdoc />
    TSession IExecutionSessionFactory<TSession>.CreateSession(CancellationToken cancellationToken)
    {
        return CreateSession(CreateStandaloneContext(), cancellationToken);
    }

    /// <inheritdoc />
    void IExecutionSessionFactory<TSession>.DisposeSession(TSession session)
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(session);
#else
        if (session is null)
        {
            throw new ArgumentNullException(nameof(session));
        }
#endif

        DisposeSession(session, CreateStandaloneContext());
    }

    private static SessionLifecycleContext CreateStandaloneContext() =>
        new()
        {
            WorkerIndex = 0,
            WorkerCount = 1,
            ManagedThreadId = Environment.CurrentManagedThreadId,
        };
}
