using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Execution.Hosting.Test;

public sealed class ExecutionPoolConfigurationTest
{
    [Theory]
    [InlineData(1, 1, 1, 0, true)]
    [InlineData(100, 1, 2147483, 5, true)]
    [InlineData(101, 1, 1, 0, false)]
    [InlineData(0, 1, 1, 0, false)]
    [InlineData(1, 0, 1, 0, false)]
    [InlineData(1, 1, 0, 0, false)]
    [InlineData(1, 1, 2147484, 0, false)]
    [InlineData(1, 1, 1, -1, false)]
    public void ValidatesBounds(int workers, int capacity, int seconds, int recycle, bool valid)
    {
        ExecutionPoolConfiguration.IsValid(workers, capacity, seconds, 100, recycle).Should().Be(valid);
    }

    [Fact]
    public void PresetPreservesWorkerHooksAndStackSize()
    {
        Action before = Before;
        Action<int> onStart = OnStart;
        Func<int, IDisposable?> createScope = CreateScope;
        var options = new ExecutionWorkerPoolOptions
        {
            BeforeThreadStart = before,
            OnWorkerThreadStart = onStart,
            CreateWorkerThreadScope = createScope,
            MaxStackSize = 2_000_000,
        };

        ExecutionPoolConfiguration.ConfigureStaPool(options, "test", 4, 20, 50);

        options.Name.Should().Be("test");
        options.WorkerCount.Should().Be(4);
        options.QueueCapacity.Should().Be(20);
        options.MaxOperationsPerSession.Should().Be(50);
        options.UseStaThread.Should().BeTrue();
        options.ShutdownMode.Should().Be(ExecutionShutdownMode.Drain);
        options.SchedulingStrategy.Should().Be(SchedulingStrategy.LeastQueued);
        options.DisposeTimeout.Should().Be(TimeSpan.FromSeconds(10));
        options.BeforeThreadStart.Should().BeSameAs(before);
        options.OnWorkerThreadStart.Should().BeSameAs(onStart);
        options.CreateWorkerThreadScope.Should().BeSameAs(createScope);
        options.MaxStackSize.Should().Be(2_000_000);

        static void Before()
        {
            // This test only checks that configuring the preset leaves the hook installed.
        }

        static void OnStart(int index)
        {
            _ = index;
        }

        static IDisposable? CreateScope(int index) => index >= 0 ? null : throw new ArgumentOutOfRangeException(nameof(index));
    }
}
