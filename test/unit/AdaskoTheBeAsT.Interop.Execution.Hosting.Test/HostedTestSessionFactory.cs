using AdaskoTheBeAsT.Interop.Execution;

namespace AdaskoTheBeAsT.Interop.Execution.Hosting.Test;

internal sealed class HostedTestSessionFactory : IExecutionSessionFactory<HostedTestSession>
{
    private int _nextSessionId;

    public HostedTestSession CreateSession(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new HostedTestSession(Interlocked.Increment(ref _nextSessionId));
    }

    public void DisposeSession(HostedTestSession session)
    {
        _ = session ?? throw new ArgumentNullException(nameof(session));
    }
}
