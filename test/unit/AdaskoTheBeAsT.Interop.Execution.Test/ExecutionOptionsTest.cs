using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Execution.Test;

public sealed class ExecutionOptionsTest
{
    [Fact]
    public void ExecutionWorkerOptions_ShouldThrowForNegativeMaxOperationsPerSession()
    {
        Action action = () => _ = new ExecutionWorkerOptions(maxOperationsPerSession: -1);

        action.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("maxOperationsPerSession");
    }

    [Fact]
    public void ExecutionWorkerPoolOptions_ShouldThrowForNonPositiveWorkerCount()
    {
        Action action = () => _ = new ExecutionWorkerPoolOptions(0);

        action.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("workerCount");
    }

    [Fact]
    public void ExecutionWorkerPoolOptions_ShouldThrowForNegativeMaxOperationsPerSession()
    {
        Action action = () => _ = new ExecutionWorkerPoolOptions(1, maxOperationsPerSession: -1);

        action.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("maxOperationsPerSession");
    }
}
