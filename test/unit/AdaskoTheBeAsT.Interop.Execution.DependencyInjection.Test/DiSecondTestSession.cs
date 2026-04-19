namespace AdaskoTheBeAsT.Interop.Execution.DependencyInjection.Test;

public sealed class DiSecondTestSession
{
    public DiSecondTestSession(int sessionId)
    {
        SessionId = sessionId;
    }

    public int SessionId { get; }
}
