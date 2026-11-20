using AdaskoTheBeAsT.Interop.Execution;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Execution.Hosting.Test;

// Null-guard and indexed-overload registration tests for hosting extensions.
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

    [Fact]
    public void AddExecutionWorkerPoolHostedService_IndexedOverload_ShouldValidateArguments()
    {
        const IServiceCollection? nullServices = null;
        var services = new ServiceCollection();

        Action nullSvc = () => nullServices!.AddExecutionWorkerPoolHostedService<HostedTestSession>(
            static (_, _) => new HostedTestSessionFactory());
        Action nullResolver = () => services.AddExecutionWorkerPoolHostedService<HostedTestSession>(
            sessionFactoryResolver: null!);

        nullSvc.Should().Throw<ArgumentNullException>().WithParameterName(nameof(services));
        nullResolver.Should().Throw<ArgumentNullException>().WithParameterName("sessionFactoryResolver");
    }

    [Fact]
    public async Task AddExecutionWorkerPoolHostedService_IndexedOverload_ShouldStartExecuteAndStopAsync()
    {
        var slots = new List<int>();
        var services = new ServiceCollection();
        services.AddExecutionWorkerPoolHostedService<HostedTestSession>(
            (_, workerIndex) =>
            {
                slots.Add(workerIndex);
                return new SlotHostedSessionFactory(workerIndex);
            },
            options =>
            {
                options.WorkerCount = 2;
                options.SchedulingStrategy = SchedulingStrategy.RoundRobin;
            });

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>()
            .OfType<ExecutionWorkerPoolHostedService<HostedTestSession>>()
            .Single();

        await hostedService.StartAsync(CancellationToken.None);

        var pool = provider.GetRequiredService<IExecutionWorkerPool<HostedTestSession>>();
        var first = await pool.ExecuteAsync(static (s, _) => s.SessionId, cancellationToken: CancellationToken.None);
        var second = await pool.ExecuteAsync(static (s, _) => s.SessionId, cancellationToken: CancellationToken.None);

        slots.Should().Equal(0, 1);
        new[] { first, second }.Should().BeEquivalentTo([200, 201]);

        await hostedService.StopAsync(CancellationToken.None);
    }

    private sealed class SlotHostedSessionFactory(int slot) : IExecutionSessionFactory<HostedTestSession>
    {
        public HostedTestSession CreateSession(CancellationToken cancellationToken) => new(200 + slot);

        public void DisposeSession(HostedTestSession session)
        {
        }
    }
}
