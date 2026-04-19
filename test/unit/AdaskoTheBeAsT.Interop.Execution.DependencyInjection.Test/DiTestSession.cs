namespace AdaskoTheBeAsT.Interop.Execution.DependencyInjection.Test;

public sealed class DiTestSession
{
    public DiTestSession(int sessionId)
    {
        SessionId = sessionId;
    }

    public int SessionId { get; }
}
