using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Execution.IntegrationTest;

public sealed class DiagnosticNamesTest
{
    [Fact]
    public void SourceName_ShouldBeStableContractValue()
    {
        ExecutionDiagnosticNames.SourceName.Should().Be("AdaskoTheBeAsT.Interop.Execution");
    }

    [Fact]
    public void ActivityName_ShouldBeStableContractValue()
    {
        ExecutionDiagnosticNames.ActivityExecute.Should().Be("ExecutionWorker.Execute");
    }

    [Fact]
    public void MetricNames_ShouldMatchDocumentedIdentifiers()
    {
        ExecutionDiagnosticNames.MetricOperations.Should().Be("execution.worker.operations");
        ExecutionDiagnosticNames.MetricSessionRecycles.Should().Be("execution.worker.session_recycles");
        ExecutionDiagnosticNames.MetricQueueDepth.Should().Be("execution.worker.queue_depth");
    }

    [Fact]
    public void Tags_ShouldMatchDocumentedIdentifiers()
    {
        ExecutionDiagnosticNames.TagWorkerName.Should().Be("worker.name");
        ExecutionDiagnosticNames.TagOutcome.Should().Be("outcome");
        ExecutionDiagnosticNames.TagRecycleReason.Should().Be("reason");
    }

    [Fact]
    public void OutcomeValues_ShouldMatchDocumentedIdentifiers()
    {
        ExecutionDiagnosticNames.OutcomeSuccess.Should().Be("success");
        ExecutionDiagnosticNames.OutcomeFaulted.Should().Be("faulted");
        ExecutionDiagnosticNames.OutcomeCancelled.Should().Be("cancelled");
    }

    [Fact]
    public void RecycleReasons_ShouldMatchDocumentedIdentifiers()
    {
        ExecutionDiagnosticNames.RecycleMaxOperations.Should().Be("max_operations");
        ExecutionDiagnosticNames.RecycleFailure.Should().Be("failure");
    }
}
