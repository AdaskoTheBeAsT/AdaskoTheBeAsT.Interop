namespace AdaskoTheBeAsT.Interop.Execution.IntegrationTest;

/// <summary>
/// Shared shim that exposes a per-test cancellation token.
/// On xUnit v3 (net8.0+) it returns <c>Xunit.TestContext.Current.CancellationToken</c>
/// so tests participate in xUnit's native test-timeout cancellation flow; on xUnit v2
/// (net462..net481) it falls back to <see cref="CancellationToken.None"/> because xUnit v2
/// has no equivalent ambient token. Centralising this preprocessor fork keeps the ~44
/// MA0040 call sites in this project readable and avoids duplicating the
/// <c>#if NET8_0_OR_GREATER</c> guard at every invocation.
/// </summary>
internal static class TestCt
{
    public static CancellationToken Current =>
#if NET8_0_OR_GREATER
        Xunit.TestContext.Current.CancellationToken;
#else
        CancellationToken.None;
#endif
}
