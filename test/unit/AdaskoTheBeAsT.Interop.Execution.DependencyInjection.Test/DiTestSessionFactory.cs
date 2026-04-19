using AdaskoTheBeAsT.Interop.Execution;

namespace AdaskoTheBeAsT.Interop.Execution.DependencyInjection.Test;

internal sealed class DiTestSessionFactory : IExecutionSessionFactory<DiTestSession>
{
    private int _nextSessionId;

    public DiTestSession CreateSession(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new DiTestSession(Interlocked.Increment(ref _nextSessionId));
    }

    public void DisposeSession(DiTestSession session)
    {
        _ = session ?? throw new ArgumentNullException(nameof(session));
    }
}
