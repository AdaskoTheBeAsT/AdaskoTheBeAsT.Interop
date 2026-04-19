using AdaskoTheBeAsT.Interop.Execution;
using AdaskoTheBeAsT.Interop.Execution.DependencyInjection;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Execution.Hosting.Test;

public sealed class ExecutionWorkerHostedServiceTest
{
    [Fact]
    public void Constructor_ShouldThrowWhenWorkerIsNull()
    {
        const IExecutionWorker<HostedTestSession>? worker = null;
        var action = () => new ExecutionWorkerHostedService<HostedTestSession>(worker!);

        action.Should().Throw<ArgumentNullException>().WithParameterName(nameof(worker));
    }

    [Fact]
    public async Task AddExecutionWorkerHostedService_ShouldRegisterHostedServiceAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IExecutionSessionFactory<HostedTestSession>, HostedTestSessionFactory>();
        services.AddExecutionWorkerHostedService<HostedTestSession>(options => options.Name = "Hosted Worker");

        await using var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>().ToArray();

        hostedServices.Should()
            .ContainSingle(hs => hs is ExecutionWorkerHostedService<HostedTestSession>);
    }

    [Fact]
    public async Task HostedService_StartAsync_ShouldInitializeWorkerAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IExecutionSessionFactory<HostedTestSession>, HostedTestSessionFactory>();
        services.AddExecutionWorkerHostedService<HostedTestSession>();

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>()
            .OfType<ExecutionWorkerHostedService<HostedTestSession>>()
            .Single();

        await hostedService.StartAsync(CancellationToken.None);

        var worker = provider.GetRequiredService<IExecutionWorker<HostedTestSession>>();
        var result = await worker.ExecuteAsync(
            static (session, _) => session.SessionId,
            cancellationToken: CancellationToken.None);

        result.Should().BePositive();

        await hostedService.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HostedService_StopAsync_ShouldDisposeWorkerAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IExecutionSessionFactory<HostedTestSession>, HostedTestSessionFactory>();
        services.AddExecutionWorkerHostedService<HostedTestSession>();

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>()
            .OfType<ExecutionWorkerHostedService<HostedTestSession>>()
            .Single();

        await hostedService.StartAsync(CancellationToken.None);
        await hostedService.StopAsync(CancellationToken.None);

        var worker = provider.GetRequiredService<IExecutionWorker<HostedTestSession>>();
        Action submit = () => _ = worker.ExecuteAsync(
            static (session, _) => session.SessionId,
            cancellationToken: CancellationToken.None);

        submit.Should().Throw<ObjectDisposedException>();
    }
}
