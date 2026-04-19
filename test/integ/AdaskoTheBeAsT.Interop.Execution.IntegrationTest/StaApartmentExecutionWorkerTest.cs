using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Execution.IntegrationTest;

public sealed class StaApartmentExecutionWorkerTest
{
    [Fact]
    public async Task Worker_WithUseStaThreadOnWindows_ShouldRunSessionOnStaThreadAsync()
    {
#if NET5_0_OR_GREATER
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
#else
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            return;
        }
#endif

        var factory = new IntegrationSessionFactory();
        var options = new ExecutionWorkerOptions(useStaThread: true);
        await using var worker = new ExecutionWorker<IntegrationSession>(factory, options);

        var observedApartmentState = await worker.ExecuteAsync(
            (_, _) => Thread.CurrentThread.GetApartmentState());

        observedApartmentState.Should().Be(
            ApartmentState.STA,
            "UseStaThread=true on Windows must configure the dedicated worker thread as STA");

        var observedSession = await worker.ExecuteAsync(
            (session, _) => session);

        observedSession.OwnerApartmentState.Should().Be(
            ApartmentState.STA,
            "the session must also have been created on the STA worker thread");
    }

    [Fact]
    public async Task Pool_WithUseStaThreadOnWindows_ShouldRunEveryWorkerOnStaThreadAsync()
    {
#if NET5_0_OR_GREATER
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
#else
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            return;
        }
#endif

        const int WorkerCount = 3;
        var factories = Enumerable
            .Range(0, WorkerCount)
            .Select(_ => new IntegrationSessionFactory())
            .ToArray();

        var poolOptions = new ExecutionWorkerPoolOptions(
            workerCount: WorkerCount,
            useStaThread: true,
            schedulingStrategy: SchedulingStrategy.RoundRobin);

        await using var pool = new ExecutionWorkerPool<IntegrationSession>(
            workerIndex => factories[workerIndex],
            poolOptions);

        await pool.InitializeAsync();

        var observedApartmentStates = new List<ApartmentState>();
        for (var submissionIndex = 0; submissionIndex < WorkerCount * 4; submissionIndex++)
        {
            var observed = await pool.ExecuteAsync(
                (_, _) => Thread.CurrentThread.GetApartmentState());
            observedApartmentStates.Add(observed);
        }

        observedApartmentStates.Should().OnlyContain(
            state => state == ApartmentState.STA,
            "every pool worker must expose an STA dedicated thread when UseStaThread=true on Windows");
    }

    [Fact]
    public async Task Worker_WithoutUseStaThread_ShouldUseDefaultApartmentAsync()
    {
        var factory = new IntegrationSessionFactory();
        await using var worker = new ExecutionWorker<IntegrationSession>(factory);

        var observedApartmentState = await worker.ExecuteAsync(
            (_, _) => Thread.CurrentThread.GetApartmentState());

        observedApartmentState.Should().NotBe(
            ApartmentState.STA,
            "UseStaThread=false must not force an STA apartment");
    }
}
