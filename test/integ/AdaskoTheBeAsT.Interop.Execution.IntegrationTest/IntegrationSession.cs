namespace AdaskoTheBeAsT.Interop.Execution.IntegrationTest;

internal sealed class IntegrationSession(int sessionId, int ownerThreadId, ApartmentState ownerApartmentState)
{
    public int SessionId { get; } = sessionId;

    public int OwnerThreadId { get; } = ownerThreadId;

    public ApartmentState OwnerApartmentState { get; } = ownerApartmentState;
}
