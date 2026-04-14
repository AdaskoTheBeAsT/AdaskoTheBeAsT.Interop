namespace AdaskoTheBeAsT.Interop.Execution;

public interface IExecutionSessionFactory<TSession>
    where TSession : class
{
    TSession CreateSession(CancellationToken cancellationToken);

    void DisposeSession(TSession session);
}
