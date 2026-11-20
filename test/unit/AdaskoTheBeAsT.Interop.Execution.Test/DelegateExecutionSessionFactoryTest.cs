using System.Collections.Concurrent;
using AwesomeAssertions;
using Xunit;

#pragma warning disable IDISP013, VSTHRD003, AsyncFixer04

namespace AdaskoTheBeAsT.Interop.Execution.Test;

public sealed class DelegateExecutionSessionFactoryTest
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public void Constructors_And_DisposeSession_ShouldValidateNullArguments()
    {
        Action nullSimpleCreate = () => _ = new DelegateExecutionSessionFactory<PlainSession>(
            createSession: (Func<CancellationToken, PlainSession>)null!);
        Action nullContextualCreate = () => _ = new DelegateExecutionSessionFactory<PlainSession>(
            createSession: (Func<SessionLifecycleContext, CancellationToken, PlainSession>)null!);

        nullSimpleCreate.Should().Throw<ArgumentNullException>().WithParameterName("createSession");
        nullContextualCreate.Should().Throw<ArgumentNullException>().WithParameterName("createSession");

        var factory = new DelegateExecutionSessionFactory<PlainSession>(static _ => new PlainSession(1));
        Action nullDispose = () => factory.DisposeSession(null!, default);
        Action nullLegacyDispose = () => ((IExecutionSessionFactory<PlainSession>)factory).DisposeSession(null!);

        nullDispose.Should().Throw<ArgumentNullException>().WithParameterName("session");
        nullLegacyDispose.Should().Throw<ArgumentNullException>().WithParameterName("session");
    }

    [Fact]
    public void CreateSession_ShouldThrowBeforeInvokingDelegateWhenPreCanceled()
    {
        var invoked = false;
        var factory = new DelegateExecutionSessionFactory<PlainSession>(_ =>
        {
            invoked = true;
            return new PlainSession(1);
        });
        using var cts = new CancellationTokenSource();
#pragma warning disable VSTHRD103, MA0042
        cts.Cancel();
#pragma warning restore VSTHRD103, MA0042

        Action create = () => ((IExecutionSessionFactory<PlainSession>)factory).CreateSession(cts.Token);

        create.Should().Throw<OperationCanceledException>();
        invoked.Should().BeFalse();
    }

    [Fact]
    public void DisposeSession_ShouldSupportDefaultDisposableNonDisposableAndCustomCallbacks()
    {
        var defaultDisposableFactory = new DelegateExecutionSessionFactory<DisposableSession>(
            static _ => new DisposableSession(7));
        var legacyInterface = (IExecutionSessionFactory<DisposableSession>)defaultDisposableFactory;
        using var disposable = legacyInterface.CreateSession(CancellationToken.None);
        disposable.DisposedCount.Should().Be(0);
        legacyInterface.DisposeSession(disposable);
        disposable.DisposedCount.Should().Be(1);

        var nonDisposableFactory = new DelegateExecutionSessionFactory<PlainSession>(static _ => new PlainSession(9));
        var plain = nonDisposableFactory.CreateSession(default, CancellationToken.None);
        nonDisposableFactory.DisposeSession(plain, default);
        plain.Id.Should().Be(9);

        var customSimpleDisposed = 0;
        var customSimpleFactory = new DelegateExecutionSessionFactory<DisposableSession>(
            static _ => new DisposableSession(11),
            _ => Interlocked.Increment(ref customSimpleDisposed));
        using var customSession = customSimpleFactory.CreateSession(default, CancellationToken.None);
        customSimpleFactory.DisposeSession(customSession, default);
        customSimpleDisposed.Should().Be(1);
        customSession.DisposedCount.Should().Be(0);
    }

    [Fact]
    public async Task Pool_WithContextualDelegateExecutionSessionFactory_ShouldCreateAndRecycleSessionsAsync()
    {
        var disposedGenerations = new ConcurrentBag<(int WorkerIndex, long Generation)>();

        var factory = new DelegateExecutionSessionFactory<DisposableSession>(
            static (ctx, _) => new DisposableSession((ctx.WorkerIndex * 100) + (int)ctx.SessionGeneration));
        var customContextualFactory = new DelegateExecutionSessionFactory<DisposableSession>(
            static (ctx, _) => new DisposableSession((ctx.WorkerIndex * 100) + (int)ctx.SessionGeneration),
            (session, ctx) =>
            {
                disposedGenerations.Add((ctx.WorkerIndex, ctx.SessionGeneration));
                session.Dispose();
            });

        await using (var defaultPool = new ExecutionWorkerPool<DisposableSession>(
            factory,
            new ExecutionWorkerPoolOptions(1) { MaxOperationsPerSession = 1 }))
        {
            var firstId = await defaultPool.ExecuteAsync(static (s, _) => s.Id, cancellationToken: TestToken);
            var secondId = await defaultPool.ExecuteAsync(static (s, _) => s.Id, cancellationToken: TestToken);
            firstId.Should().Be(0);
            secondId.Should().Be(1);
        }

        await using (var customPool = new ExecutionWorkerPool<DisposableSession>(
            customContextualFactory,
            new ExecutionWorkerPoolOptions(2)
            {
                MaxOperationsPerSession = 1,
                SchedulingStrategy = SchedulingStrategy.RoundRobin,
            }))
        {
            _ = await customPool.ExecuteAsync(static (s, _) => s.Id, cancellationToken: TestToken);
            _ = await customPool.ExecuteAsync(static (s, _) => s.Id, cancellationToken: TestToken);
            _ = await customPool.ExecuteAsync(static (s, _) => s.Id, cancellationToken: TestToken);
        }

        disposedGenerations.Should().Contain((0, 0L));
        disposedGenerations.Should().Contain((1, 0L));
    }

    private sealed class PlainSession(int id)
    {
        public int Id { get; } = id;
    }

    private sealed class DisposableSession(int id) : IDisposable
    {
        private int _disposedCount;

        public int Id { get; } = id;

        public int DisposedCount => Volatile.Read(ref _disposedCount);

        public void Dispose() => Interlocked.Increment(ref _disposedCount);
    }
}
