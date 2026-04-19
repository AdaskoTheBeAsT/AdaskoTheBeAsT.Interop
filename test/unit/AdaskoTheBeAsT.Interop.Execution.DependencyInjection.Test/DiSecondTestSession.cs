namespace AdaskoTheBeAsT.Interop.Execution.DependencyInjection.Test;

public sealed class DiSecondTestSession(int sessionId)
{
    public int SessionId { get; } = sessionId;
}
