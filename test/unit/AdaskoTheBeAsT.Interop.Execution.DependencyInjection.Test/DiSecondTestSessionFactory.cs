using AdaskoTheBeAsT.Interop.Execution;

namespace AdaskoTheBeAsT.Interop.Execution.DependencyInjection.Test;

internal sealed class DiSecondTestSessionFactory : IExecutionSessionFactory<DiSecondTestSession>
{
    private int _nextSessionId;

    public DiSecondTestSession CreateSession(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new DiSecondTestSession(Interlocked.Increment(ref _nextSessionId));
    }

    public void DisposeSession(DiSecondTestSession session)
    {
        _ = session ?? throw new ArgumentNullException(nameof(session));
    }
}
