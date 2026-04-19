namespace AdaskoTheBeAsT.Interop.Execution.Hosting.Test;

public sealed class HostedTestSession(int sessionId)
{
    public int SessionId { get; } = sessionId;
}
