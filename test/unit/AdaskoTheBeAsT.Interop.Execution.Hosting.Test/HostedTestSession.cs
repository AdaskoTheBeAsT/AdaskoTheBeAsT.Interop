namespace AdaskoTheBeAsT.Interop.Execution.Hosting.Test;

public sealed class HostedTestSession
{
    public HostedTestSession(int sessionId)
    {
        SessionId = sessionId;
    }

    public int SessionId { get; }
}
