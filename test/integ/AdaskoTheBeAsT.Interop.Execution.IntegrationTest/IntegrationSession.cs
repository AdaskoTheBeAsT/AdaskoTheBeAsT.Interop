namespace AdaskoTheBeAsT.Interop.Execution.IntegrationTest;

internal sealed class IntegrationSession
{
    public IntegrationSession(int sessionId, int ownerThreadId, ApartmentState ownerApartmentState)
    {
        SessionId = sessionId;
        OwnerThreadId = ownerThreadId;
        OwnerApartmentState = ownerApartmentState;
    }

    public int SessionId { get; }

    public int OwnerThreadId { get; }

    public ApartmentState OwnerApartmentState { get; }
}
