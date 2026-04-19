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
            .Which.ParamName.Should().Be("MaxOperationsPerSession");
    }

    [Fact]
    public void ExecutionWorkerPoolOptions_ShouldThrowForNonPositiveWorkerCount()
    {
        Action action = () => _ = new ExecutionWorkerPoolOptions(0);

        action.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("WorkerCount");
    }

    [Fact]
    public void ExecutionWorkerPoolOptions_ShouldThrowForNegativeMaxOperationsPerSession()
    {
        Action action = () => _ = new ExecutionWorkerPoolOptions(1, maxOperationsPerSession: -1);

        action.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("MaxOperationsPerSession");
    }

    [Fact]
    public void ExecutionWorkerOptions_ShouldSupportObjectInitializerForIOptionsBinding()
    {
        var options = new ExecutionWorkerOptions
        {
            Name = "Worker",
            UseStaThread = true,
            MaxOperationsPerSession = 10,
            DisposeTimeout = TimeSpan.FromSeconds(5),
        };

        options.Name.Should().Be("Worker");
        options.UseStaThread.Should().BeTrue();
        options.MaxOperationsPerSession.Should().Be(10);
        options.DisposeTimeout.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ExecutionWorkerPoolOptions_ShouldSupportObjectInitializerForIOptionsBinding()
    {
        var options = new ExecutionWorkerPoolOptions
        {
            WorkerCount = 4,
            Name = "Pool",
            SchedulingStrategy = SchedulingStrategy.RoundRobin,
        };

        options.WorkerCount.Should().Be(4);
        options.Name.Should().Be("Pool");
        options.SchedulingStrategy.Should().Be(SchedulingStrategy.RoundRobin);
        options.DisposeTimeout.Should().Be(Timeout.InfiniteTimeSpan);
    }
}
