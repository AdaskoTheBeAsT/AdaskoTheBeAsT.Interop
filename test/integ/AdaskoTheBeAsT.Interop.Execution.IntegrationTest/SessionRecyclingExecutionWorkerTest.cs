using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Execution.IntegrationTest;

public sealed class SessionRecyclingExecutionWorkerTest
{
    [Fact]
    public async Task Worker_ShouldRecycleSessionAfterMaxOperationsPerSessionAsync()
    {
        const int MaxOperationsPerSession = 5;
        const int TotalSubmissions = 23;

        var factory = new IntegrationSessionFactory();
        var options = new ExecutionWorkerOptions(
            maxOperationsPerSession: MaxOperationsPerSession);

        await using var worker = new ExecutionWorker<IntegrationSession>(factory, options);

        var observedSessionIds = new List<int>(capacity: TotalSubmissions);
        for (var submissionIndex = 0; submissionIndex < TotalSubmissions; submissionIndex++)
        {
            var sessionId = await worker.ExecuteAsync(
                (session, _) => session.SessionId);
            observedSessionIds.Add(sessionId);
        }

        var expectedSessionCount = (TotalSubmissions + MaxOperationsPerSession - 1) / MaxOperationsPerSession;

        factory.CreateCount.Should().Be(
            expectedSessionCount,
            "a new session must be created after every MaxOperationsPerSession work items");
        factory.DisposeCount.Should().BeGreaterThanOrEqualTo(
            expectedSessionCount - 1,
            "every recycled session must be disposed on the worker thread");

        observedSessionIds.Distinct().Should().HaveCount(expectedSessionCount);

        for (var submissionIndex = 0; submissionIndex < TotalSubmissions; submissionIndex++)
        {
            var expectedSessionId = (submissionIndex / MaxOperationsPerSession) + 1;
            observedSessionIds[submissionIndex].Should().Be(
                expectedSessionId,
                "submissions within the same batch of MaxOperationsPerSession must share the same session id");
        }
    }

    [Fact]
    public async Task Worker_ShouldRecycleSessionOnFailureWhenRequestedAsync()
    {
        var factory = new IntegrationSessionFactory();
        await using var worker = new ExecutionWorker<IntegrationSession>(factory);

        var firstSessionId = await worker.ExecuteAsync(
            (session, _) => session.SessionId);

        var recyclingOptions = new ExecutionRequestOptions(recycleSessionOnFailure: true);

        Func<Task> faultingCall = async () =>
        {
            await worker.ExecuteAsync(
                new Func<IntegrationSession, CancellationToken, int>(
                    (_, _) => throw new InvalidOperationException("boom")),
                recyclingOptions);
        };

        await faultingCall.Should().ThrowAsync<InvalidOperationException>();

        var recycledSessionId = await worker.ExecuteAsync(
            (session, _) => session.SessionId);

        factory.CreateCount.Should().BeGreaterThanOrEqualTo(
            2,
            "RecycleSessionOnFailure=true must force a fresh session creation after the faulting work item");
        recycledSessionId.Should().NotBe(
            firstSessionId,
            "post-failure submissions must run on a newly-created session");
    }

    [Fact]
    public async Task Worker_ShouldNotRecycleSessionOnFailureByDefaultAsync()
    {
        var factory = new IntegrationSessionFactory();
        await using var worker = new ExecutionWorker<IntegrationSession>(factory);

        var firstSessionId = await worker.ExecuteAsync(
            (session, _) => session.SessionId);

        Func<Task> faultingCall = async () =>
        {
            await worker.ExecuteAsync(
                new Func<IntegrationSession, CancellationToken, int>(
                    (_, _) => throw new InvalidOperationException("boom")));
        };

        await faultingCall.Should().ThrowAsync<InvalidOperationException>();

        var sameSessionId = await worker.ExecuteAsync(
            (session, _) => session.SessionId);

        factory.CreateCount.Should().Be(
            1,
            "without RecycleSessionOnFailure, the session must survive a faulting work item");
        sameSessionId.Should().Be(firstSessionId);
    }
}
