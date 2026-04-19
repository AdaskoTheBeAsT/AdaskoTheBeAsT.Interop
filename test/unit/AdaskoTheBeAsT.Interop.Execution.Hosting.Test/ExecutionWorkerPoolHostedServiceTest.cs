using AdaskoTheBeAsT.Interop.Execution;
using AdaskoTheBeAsT.Interop.Execution.DependencyInjection;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Execution.Hosting.Test;

public sealed class ExecutionWorkerPoolHostedServiceTest
{
    [Fact]
    public void Constructor_ShouldThrowWhenPoolIsNull()
    {
        const IExecutionWorkerPool<HostedTestSession>? pool = null;
        var action = () => new ExecutionWorkerPoolHostedService<HostedTestSession>(pool!);

        action.Should().Throw<ArgumentNullException>().WithParameterName(nameof(pool));
    }

    [Fact]
    public async Task AddExecutionWorkerPoolHostedService_ShouldRegisterHostedServiceAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IExecutionSessionFactory<HostedTestSession>, HostedTestSessionFactory>();
        services.AddExecutionWorkerPoolHostedService<HostedTestSession>(options =>
        {
            options.WorkerCount = 2;
            options.Name = "Hosted Pool";
        });

        await using var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>().ToArray();

        hostedServices.Should()
            .ContainSingle(hs => hs is ExecutionWorkerPoolHostedService<HostedTestSession>);
    }

    [Fact]
    public async Task HostedService_StartAsync_ShouldInitializePoolAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IExecutionSessionFactory<HostedTestSession>, HostedTestSessionFactory>();
        services.AddExecutionWorkerPoolHostedService<HostedTestSession>(options => options.WorkerCount = 2);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>()
            .OfType<ExecutionWorkerPoolHostedService<HostedTestSession>>()
            .Single();

        await hostedService.StartAsync(CancellationToken.None);

        var pool = provider.GetRequiredService<IExecutionWorkerPool<HostedTestSession>>();
        var result = await pool.ExecuteAsync(
            static (session, _) => session.SessionId,
            cancellationToken: CancellationToken.None);

        result.Should().BeGreaterThan(0);
        pool.WorkerCount.Should().Be(2);

        await hostedService.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HostedService_StopAsync_ShouldDisposePoolAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IExecutionSessionFactory<HostedTestSession>, HostedTestSessionFactory>();
        services.AddExecutionWorkerPoolHostedService<HostedTestSession>(options => options.WorkerCount = 2);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>()
            .OfType<ExecutionWorkerPoolHostedService<HostedTestSession>>()
            .Single();

        await hostedService.StartAsync(CancellationToken.None);
        await hostedService.StopAsync(CancellationToken.None);

        var pool = provider.GetRequiredService<IExecutionWorkerPool<HostedTestSession>>();
        Action submit = () => _ = pool.ExecuteAsync(
            static (session, _) => session.SessionId,
            cancellationToken: CancellationToken.None);

        submit.Should().Throw<ObjectDisposedException>();
    }
}
