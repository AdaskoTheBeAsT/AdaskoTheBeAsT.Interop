using System.Diagnostics.Metrics;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Execution.IntegrationTest;

public sealed class ScopedDiagnosticsTest
{
    [Fact]
    public async Task CustomDiagnosticsScope_ShouldEmitToIsolatedMeterAsync()
    {
        var scopedName = $"{ExecutionDiagnosticNames.SourceName}.IntegrationTest.{Guid.NewGuid():N}";
        var workerName = $"Scoped Worker {Guid.NewGuid():N}";
        using var scope = new ExecutionDiagnostics(scopedName);

        var scopedOperations = 0L;
        using var listener = BuildOperationsListener(
            scopedName,
            workerName,
            v => Interlocked.Add(ref scopedOperations, v));
        listener.Start();

        var factory = new IntegrationSessionFactory();
        await using (var worker = new ExecutionWorker<IntegrationSession>(
            factory,
            new ExecutionWorkerOptions(name: workerName, diagnostics: scope)))
        {
            for (var i = 0; i < 3; i++)
            {
                await worker.ExecuteAsync(
                    (session, _) => session.SessionId,
                    cancellationToken: TestCt.Current);
            }
        }

        Interlocked.Read(ref scopedOperations).Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task CustomDiagnosticsScope_ShouldNotLeakIntoSharedMeterAsync()
    {
        var scopedName = $"{ExecutionDiagnosticNames.SourceName}.IntegrationTest.{Guid.NewGuid():N}";
        var workerName = $"Scoped Only Worker {Guid.NewGuid():N}";
        using var scope = new ExecutionDiagnostics(scopedName);

        var sharedLeak = 0L;
        using var listener = BuildOperationsListener(
            ExecutionDiagnosticNames.SourceName,
            workerName,
            v => Interlocked.Add(ref sharedLeak, v));
        listener.Start();

        var factory = new IntegrationSessionFactory();
        await using (var scopedWorker = new ExecutionWorker<IntegrationSession>(
            factory,
            new ExecutionWorkerOptions(name: workerName, diagnostics: scope)))
        {
            for (var i = 0; i < 5; i++)
            {
                await scopedWorker.ExecuteAsync(
                    (session, _) => session.SessionId,
                    cancellationToken: TestCt.Current);
            }
        }

        Interlocked.Read(ref sharedLeak).Should().Be(
            0,
            "operations from a scoped worker must never surface on the shared meter");
    }

    [Fact]
    public void ExecutionDiagnostics_Shared_ShouldBeSingletonAndDisposeAsNoOp()
    {
        var first = ExecutionDiagnostics.Shared;
        var second = ExecutionDiagnostics.Shared;

        first.Should().BeSameAs(second);
        first.SourceName.Should().Be(ExecutionDiagnosticNames.SourceName);

#pragma warning disable IDISP007, IDISP016
        // Disposing the shared singleton is defined as a no-op — this test
        // exists precisely to lock that contract. The IDisposableAnalyzers
        // rules would otherwise reject the redundant Dispose call and the
        // subsequent use of the singleton.
        first.Dispose();
#pragma warning restore IDISP007, IDISP016

        ExecutionDiagnostics.Shared.Should().BeSameAs(first);
        ExecutionDiagnostics.Shared.SourceName.Should().Be(ExecutionDiagnosticNames.SourceName);
    }

    [Fact]
    public void ExecutionDiagnostics_NewInstance_ShouldRejectNullOrWhitespaceName()
    {
        // IDISP004 / CA1806 suppressed: the test asserts that the constructor throws
        // before the IDisposable instance is fully constructed, so there is nothing
        // to dispose. Using `_ =` makes the discard explicit for CA1806.
#pragma warning disable IDISP004, CA1806
        Action nullName = () => _ = new ExecutionDiagnostics(null!);
        Action whitespaceName = () => _ = new ExecutionDiagnostics("   ");
#pragma warning restore IDISP004, CA1806

        nullName.Should().Throw<ArgumentException>().And.ParamName.Should().Be("sourceName");
        whitespaceName.Should().Throw<ArgumentException>().And.ParamName.Should().Be("sourceName");
    }

    private static MeterListener BuildOperationsListener(
        string meterName,
        string workerNameFilter,
        Action<long> onMeasurement)
    {
        _ = onMeasurement ?? throw new ArgumentNullException(nameof(onMeasurement));

        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (string.Equals(instrument.Meter.Name, meterName, StringComparison.Ordinal) &&
                    string.Equals(instrument.Name, ExecutionDiagnosticNames.MetricOperations, StringComparison.Ordinal))
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };

        listener.SetMeasurementEventCallback<long>(
            (instrument, measurement, tags, state) =>
            {
                _ = instrument;
                _ = state;
                for (var i = 0; i < tags.Length; i++)
                {
                    var tag = tags[i];
                    if (string.Equals(tag.Key, ExecutionDiagnosticNames.TagWorkerName, StringComparison.Ordinal) &&
                        string.Equals(tag.Value as string, workerNameFilter, StringComparison.Ordinal))
                    {
                        onMeasurement.Invoke(measurement);
                        return;
                    }
                }
            });
        return listener;
    }
}
