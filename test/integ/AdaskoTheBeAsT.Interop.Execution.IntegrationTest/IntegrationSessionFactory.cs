namespace AdaskoTheBeAsT.Interop.Execution.IntegrationTest;

internal sealed class IntegrationSessionFactory : IExecutionSessionFactory<IntegrationSession>
{
    private int _createCount;
    private int _disposeCount;

    public int CreateCount => Volatile.Read(ref _createCount);

    public int DisposeCount => Volatile.Read(ref _disposeCount);

    public IntegrationSession CreateSession(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sessionId = Interlocked.Increment(ref _createCount);
        var apartmentState =
#if NET5_0_OR_GREATER
            OperatingSystem.IsWindows()
#else
            Environment.OSVersion.Platform == PlatformID.Win32NT
#endif
                ? Thread.CurrentThread.GetApartmentState()
                : ApartmentState.Unknown;

        return new IntegrationSession(
            sessionId,
            Environment.CurrentManagedThreadId,
            apartmentState);
    }

    public void DisposeSession(IntegrationSession session)
    {
        _ = session ?? throw new ArgumentNullException(nameof(session));
        Interlocked.Increment(ref _disposeCount);
    }
}
