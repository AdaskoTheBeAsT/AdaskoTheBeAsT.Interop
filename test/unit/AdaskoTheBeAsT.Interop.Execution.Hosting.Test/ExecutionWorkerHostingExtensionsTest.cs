using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Execution.Hosting.Test;

// Null-guard tests for the hosting registration extensions — the positive
// registration paths are covered by ExecutionWorkerHostedServiceTest and
// ExecutionWorkerPoolHostedServiceTest.
public sealed class ExecutionWorkerHostingExtensionsTest
{
    [Fact]
    public void AddExecutionWorkerHostedService_ShouldThrowWhenServicesIsNull()
    {
        const IServiceCollection? services = null;
        var action = () => services!.AddExecutionWorkerHostedService<HostedTestSession>();

        action.Should().Throw<ArgumentNullException>().WithParameterName(nameof(services));
    }

    [Fact]
    public void AddExecutionWorkerPoolHostedService_ShouldThrowWhenServicesIsNull()
    {
        const IServiceCollection? services = null;
        var action = () => services!.AddExecutionWorkerPoolHostedService<HostedTestSession>();

        action.Should().Throw<ArgumentNullException>().WithParameterName(nameof(services));
    }
}
