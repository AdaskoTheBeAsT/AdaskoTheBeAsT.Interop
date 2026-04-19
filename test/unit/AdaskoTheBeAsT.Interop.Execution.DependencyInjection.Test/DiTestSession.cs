namespace AdaskoTheBeAsT.Interop.Execution.DependencyInjection.Test;

public sealed class DiTestSession(int sessionId)
{
    public int SessionId { get; } = sessionId;
}
