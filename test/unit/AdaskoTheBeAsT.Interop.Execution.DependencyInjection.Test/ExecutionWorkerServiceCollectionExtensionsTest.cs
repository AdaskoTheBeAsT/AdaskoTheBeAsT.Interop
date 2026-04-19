using AdaskoTheBeAsT.Interop.Execution;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Execution.DependencyInjection.Test;

public sealed class ExecutionWorkerServiceCollectionExtensionsTest
{
    [Fact]
    public void AddExecutionWorker_ShouldThrowWhenServicesIsNull()
    {
        const IServiceCollection? services = null;
        var action = () => services!.AddExecutionWorker<DiTestSession>();

        action.Should().Throw<ArgumentNullException>().WithParameterName(nameof(services));
    }

    [Fact]
    public void AddExecutionWorkerPool_ShouldThrowWhenServicesIsNull()
    {
        const IServiceCollection? services = null;
        var action = () => services!.AddExecutionWorkerPool<DiTestSession>();

        action.Should().Throw<ArgumentNullException>().WithParameterName(nameof(services));
    }

    [Fact]
    public async Task AddExecutionWorker_ShouldResolveAndExecuteAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IExecutionSessionFactory<DiTestSession>, DiTestSessionFactory>();
        services.AddExecutionWorker<DiTestSession>(options => options.Name = "DI Worker");

        await using var provider = services.BuildServiceProvider();
        var worker = provider.GetRequiredService<IExecutionWorker<DiTestSession>>();

        var result = await worker.ExecuteAsync(
            static (session, _) => session.SessionId,
            cancellationToken: CancellationToken.None);

        result.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AddExecutionWorker_ShouldBeSingletonAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IExecutionSessionFactory<DiTestSession>, DiTestSessionFactory>();
        services.AddExecutionWorker<DiTestSession>();

        await using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<IExecutionWorker<DiTestSession>>();
        var second = provider.GetRequiredService<IExecutionWorker<DiTestSession>>();

        second.Should().BeSameAs(first);
    }

    [Fact]
    public async Task AddExecutionWorker_ShouldApplyConfigureDelegateAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IExecutionSessionFactory<DiTestSession>, DiTestSessionFactory>();
        services.AddExecutionWorker<DiTestSession>(options =>
        {
            options.Name = "Configured Worker";
            options.MaxOperationsPerSession = 5;
        });

        await using var provider = services.BuildServiceProvider();
        var worker = provider.GetRequiredService<IExecutionWorker<DiTestSession>>();

        // Execute once to ensure the worker is actually wired with the configured options.
        _ = await worker.ExecuteAsync(
            static (session, _) => session.SessionId,
            cancellationToken: CancellationToken.None);

        worker.Should().NotBeNull();
    }

    [Fact]
    public async Task AddExecutionWorkerPool_ShouldResolveAndExecuteAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IExecutionSessionFactory<DiTestSession>, DiTestSessionFactory>();
        services.AddExecutionWorkerPool<DiTestSession>(options =>
        {
            options.WorkerCount = 2;
            options.Name = "DI Pool";
        });

        await using var provider = services.BuildServiceProvider();
        var pool = provider.GetRequiredService<IExecutionWorkerPool<DiTestSession>>();

        var result = await pool.ExecuteAsync(
            static (session, _) => session.SessionId,
            cancellationToken: CancellationToken.None);

        result.Should().BeGreaterThan(0);
        pool.WorkerCount.Should().Be(2);
    }

    [Fact]
    public async Task AddExecutionWorkerPool_ShouldThrowWhenOptionsNotConfiguredAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IExecutionSessionFactory<DiTestSession>, DiTestSessionFactory>();
        services.AddExecutionWorkerPool<DiTestSession>();

        await using var provider = services.BuildServiceProvider();

        Action action = () => _ = provider.GetRequiredService<IExecutionWorkerPool<DiTestSession>>();

        action.Should().Throw<InvalidOperationException>();
    }
}
