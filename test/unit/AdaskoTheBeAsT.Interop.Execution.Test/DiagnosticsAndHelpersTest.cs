using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Execution.Test;

// Unit tests for internal collaborators that are otherwise only touched
// transitively via ExecutionWorker. Surfacing them directly keeps the
// defensive branches testable even when the worker-level tests skip them.
public sealed class DiagnosticsAndHelpersTest
{
    [Fact]
    public void ExecutionHelpers_TryIgnore_ShouldThrowForNullAction()
    {
        const Action? action = null;
        Action call = () => ExecutionHelpers.TryIgnore(action!);

        call.Should().Throw<ArgumentNullException>().WithParameterName(nameof(action));
    }

    [Fact]
    public void ExecutionHelpers_TryIgnore_ShouldSwallowExceptionsThrownByAction()
    {
        Action call = () => ExecutionHelpers.TryIgnore(static () => throw new InvalidOperationException("x"));

        call.Should().NotThrow();
    }

    [Fact]
    public void ExecutionWorkerRegistration_ShouldThrowForNullAccessor()
    {
        const Func<int>? queueDepthAccessor = null;
        Action action = () => _ = new ExecutionWorkerRegistration("w", queueDepthAccessor!);

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName(nameof(queueDepthAccessor));
    }

    [Fact]
    public void ExecutionWorkerRegistration_ShouldExposeNameAndInvokeAccessor()
    {
        var registration = new ExecutionWorkerRegistration("w", () => 42);

        registration.Name.Should().Be("w");
        registration.GetQueueDepth().Should().Be(42);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ExecutionDiagnostics_ShouldThrowForNullOrWhitespaceSourceName(string? sourceName)
    {
        Action call = () => CreateAndDispose(sourceName!);

        call.Should().Throw<ArgumentException>().WithParameterName(nameof(sourceName));

        // The throw happens before the IDisposable instance is constructed, so
        // this helper never actually returns one to dispose.
        static void CreateAndDispose(string name)
        {
#pragma warning disable CA2000, IDISP001, IDISP004
            _ = new ExecutionDiagnostics(name);
#pragma warning restore CA2000, IDISP001, IDISP004
        }
    }

    [Fact]
    public void ExecutionDiagnostics_Dispose_ShouldBeIdempotent()
    {
        using var diagnostics = new ExecutionDiagnostics("coverage-test-scope-idempotent");

        Action disposeTwice = () =>
        {
            diagnostics.Dispose();
            diagnostics.Dispose();
        };

        disposeTwice.Should().NotThrow();
    }

    // IDISP007 / IDISP016 disabled: disposing Shared is the exact contract
    // under test — the Dispose implementation is documented to no-op on the
    // lazy Shared singleton so the remaining workers in the process are not
    // torn down by an overeager caller. After the no-op Dispose the instance
    // must still accept registrations, which is what the post-assertion
    // verifies. Both analyzer warnings are therefore intended by the test.
#pragma warning disable IDISP007, IDISP016
    [Fact]
    public void ExecutionDiagnostics_DisposeOnShared_ShouldBeNoOp()
    {
        var shared = ExecutionDiagnostics.Shared;

        shared.Dispose();

        var registration = new ExecutionWorkerRegistration("noop", () => 0);
        Action call = () => shared.RegisterWorker(registration);

        call.Should().NotThrow();
        shared.UnregisterWorker(registration);
    }
#pragma warning restore IDISP007, IDISP016

    [Fact]
    public void ExecutionDiagnostics_RegisterWorker_ShouldThrowForNullRegistration()
    {
        using var diagnostics = new ExecutionDiagnostics("coverage-test-scope-register-null");
        const ExecutionWorkerRegistration? registration = null;

        Action action = () => diagnostics.RegisterWorker(registration!);

        action.Should().Throw<ArgumentNullException>().WithParameterName(nameof(registration));
    }

    [Fact]
    public void ExecutionDiagnostics_UnregisterWorker_ShouldIgnoreNullRegistration()
    {
        using var diagnostics = new ExecutionDiagnostics("coverage-test-scope-unregister-null");

        Action action = () => diagnostics.UnregisterWorker(null!);

        action.Should().NotThrow();
    }
}
