using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Execution.IntegrationTest;

public sealed class ValueTaskExecutionWorkerTest
{
    [Fact]
    public async Task ExecuteValueAsync_ShouldRunVoidDelegateAsync()
    {
        var factory = new IntegrationSessionFactory();
        await using var worker = new ExecutionWorker<IntegrationSession>(factory);

        var executed = 0;
        await worker.ExecuteValueAsync(
            (_, _) => Interlocked.Increment(ref executed),
            cancellationToken: TestCt.Current);

        Volatile.Read(ref executed).Should().Be(1);
    }

    [Fact]
    public async Task ExecuteValueAsyncOfTResult_ShouldReturnResultAsync()
    {
        var factory = new IntegrationSessionFactory();
        await using var worker = new ExecutionWorker<IntegrationSession>(factory);

        var sessionId = await worker.ExecuteValueAsync(
            (session, _) => session.SessionId,
            cancellationToken: TestCt.Current);

        sessionId.Should().Be(1);
    }

    [Fact]
    public async Task Pool_ExecuteValueAsync_ShouldRunVoidDelegateAsync()
    {
        var factory = new IntegrationSessionFactory();
        await using var pool = new ExecutionWorkerPool<IntegrationSession>(
            _ => factory,
            new ExecutionWorkerPoolOptions(workerCount: 2));

        var executed = 0;
        await pool.ExecuteValueAsync(
            (_, _) => Interlocked.Increment(ref executed),
            cancellationToken: TestCt.Current);

        Volatile.Read(ref executed).Should().Be(1);
    }

    [Fact]
    public async Task Pool_ExecuteValueAsyncOfTResult_ShouldReturnResultAsync()
    {
        var factory = new IntegrationSessionFactory();
        await using var pool = new ExecutionWorkerPool<IntegrationSession>(
            _ => factory,
            new ExecutionWorkerPoolOptions(workerCount: 2));

        var result = await pool.ExecuteValueAsync(
            (session, _) => session.SessionId * 10,
            cancellationToken: TestCt.Current);

        result.Should().BePositive();
    }

    [Fact]
    public async Task ExecuteValueAsync_ShouldPropagateExceptionAsync()
    {
        var factory = new IntegrationSessionFactory();
        await using var worker = new ExecutionWorker<IntegrationSession>(factory);

        Func<Task> failing = async () => await worker.ExecuteValueAsync(
            (_, _) => throw new InvalidOperationException("boom"),
            cancellationToken: TestCt.Current);

        await failing.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }
}
