#if !NET8_0_OR_GREATER
using System.Reflection;
using System.Runtime.Serialization;
#endif
using AwesomeAssertions;
using Xunit;

#pragma warning disable IDISP013, VSTHRD003, AsyncFixer04

namespace AdaskoTheBeAsT.Interop.Execution.Test;

public sealed class ExecutionAdmissionRejectedExceptionTest
{
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(10);

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public void Constructors_ShouldPopulatePropertiesAndValidateQueueCapacity()
    {
        var defaultEx = new ExecutionAdmissionRejectedException();
        defaultEx.Should().BeAssignableTo<InvalidOperationException>();
        defaultEx.Message.Should().Be(ExecutionAdmissionRejectedException.DefaultMessage);
        defaultEx.WorkerName.Should().BeNull();
        defaultEx.QueueCapacity.Should().Be(0);

        var messageEx = new ExecutionAdmissionRejectedException("custom-msg");
        messageEx.Message.Should().Be("custom-msg");
        messageEx.WorkerName.Should().BeNull();
        messageEx.QueueCapacity.Should().Be(0);

        var inner = new InvalidOperationException("inner");
        var innerEx = new ExecutionAdmissionRejectedException("outer-msg", inner);
        innerEx.Message.Should().Be("outer-msg");
        innerEx.InnerException.Should().BeSameAs(inner);

        var detailedDefaultMsg = new ExecutionAdmissionRejectedException("Pool #1", queueCapacity: 4);
        detailedDefaultMsg.Message.Should().Be(ExecutionAdmissionRejectedException.DefaultMessage);
        detailedDefaultMsg.WorkerName.Should().Be("Pool #1");
        detailedDefaultMsg.QueueCapacity.Should().Be(4);

        var detailedCustomMsg = new ExecutionAdmissionRejectedException("Pool #2", queueCapacity: 8, message: "queue-full");
        detailedCustomMsg.Message.Should().Be("queue-full");
        detailedCustomMsg.WorkerName.Should().Be("Pool #2");
        detailedCustomMsg.QueueCapacity.Should().Be(8);

        Action negativeCapacity = () => _ = new ExecutionAdmissionRejectedException("bad", queueCapacity: -1);
        negativeCapacity.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("queueCapacity");
    }

#if !NET8_0_OR_GREATER
    [Fact]
    public void GetObjectData_And_SerializationConstructor_ShouldRoundTripFields()
    {
        var original = new ExecutionAdmissionRejectedException("Worker #3", queueCapacity: 12, message: "saturated");

        Action nullInfo = () => original.GetObjectData(null!, default);
        nullInfo.Should().Throw<ArgumentNullException>().WithParameterName("info");

        var info = new SerializationInfo(typeof(ExecutionAdmissionRejectedException), new FormatterConverter());
        var context = new StreamingContext(StreamingContextStates.All);
        original.GetObjectData(info, context);

#pragma warning disable REFL003, REFL009, S3011
        var ctor = typeof(ExecutionAdmissionRejectedException).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(SerializationInfo), typeof(StreamingContext)],
            modifiers: null);
#pragma warning restore REFL003, REFL009, S3011

        ctor.Should().NotBeNull();
        var rehydrated = (ExecutionAdmissionRejectedException)ctor!.Invoke([info, context]);
        rehydrated.Message.Should().Be("saturated");
        rehydrated.WorkerName.Should().Be("Worker #3");
        rehydrated.QueueCapacity.Should().Be(12);

        var badInfo = new SerializationInfo(typeof(ExecutionAdmissionRejectedException), new FormatterConverter());
        var enumerator = info.GetEnumerator();
        while (enumerator.MoveNext())
        {
            var entry = enumerator.Current;
            var isCap = string.Equals(entry.Name, "QueueCapacity", StringComparison.Ordinal);
            badInfo.AddValue(entry.Name, isCap ? -5 : entry.Value);
        }

        Action deserializeNegative = () => _ = ctor.Invoke([badInfo, context]);
        deserializeNegative.Should().Throw<TargetInvocationException>()
            .WithInnerException<SerializationException>();
    }
#endif

    [Fact]
    public async Task Worker_Submit_ShouldFaultWithExecutionAdmissionRejectedExceptionAcrossTaskAndValueTaskAsync()
    {
        using var enterActive = new ManualResetEventSlim();
        using var releaseActive = new ManualResetEventSlim();
        var factory = new DelegateExecutionSessionFactory<object>(static _ => new object());
        await using var worker = new ExecutionWorker<object>(
            factory,
            new ExecutionWorkerOptions(name: "AdmissionWorker") { QueueCapacity = 1 });

        await worker.InitializeAsync(TestToken);

        var activeTask = worker.ExecuteAsync(
            (_, _) =>
            {
                enterActive.Set();
                releaseActive.Wait(GateTimeout, TestToken).Should().BeTrue();
                return 1;
            },
            cancellationToken: TestToken);

        Task<int>? queuedTask = null;
        try
        {
            enterActive.Wait(GateTimeout, TestToken).Should().BeTrue();

            queuedTask = worker.ExecuteAsync(static (_, _) => 2, cancellationToken: TestToken);

            Func<Task<int>> taskOverflow = () => worker.ExecuteAsync(static (_, _) => 3, cancellationToken: TestToken);
            var taskEx = (await taskOverflow.Should().ThrowAsync<ExecutionAdmissionRejectedException>()).Which;
            taskEx.WorkerName.Should().Be("AdmissionWorker");
            taskEx.QueueCapacity.Should().Be(1);
            taskEx.Message.Should().Be(ExecutionAdmissionRejectedException.DefaultMessage);

            Func<Task> voidValueOverflow = async () =>
                await worker.ExecuteValueAsync(static (_, _) => { }, cancellationToken: TestToken);
            var voidEx = (await voidValueOverflow.Should().ThrowAsync<ExecutionAdmissionRejectedException>()).Which;
            voidEx.WorkerName.Should().Be("AdmissionWorker");
            voidEx.QueueCapacity.Should().Be(1);

            Func<Task<int>> resultValueOverflow = async () =>
                await worker.ExecuteValueAsync(static (_, _) => 4, cancellationToken: TestToken);
            var resultEx = (await resultValueOverflow.Should().ThrowAsync<ExecutionAdmissionRejectedException>()).Which;
            resultEx.WorkerName.Should().Be("AdmissionWorker");
            resultEx.QueueCapacity.Should().Be(1);
        }
        finally
        {
            releaseActive.Set();
        }

        (await activeTask).Should().Be(1);
        (await queuedTask).Should().Be(2);
    }

    [Fact]
    public async Task Pool_Submit_ShouldFaultWithExecutionAdmissionRejectedExceptionAsync()
    {
        using var enterActive = new ManualResetEventSlim();
        using var releaseActive = new ManualResetEventSlim();
        var factory = new DelegateExecutionSessionFactory<object>(static _ => new object());
        await using var pool = new ExecutionWorkerPool<object>(
            factory,
            new ExecutionWorkerPoolOptions(1, name: "AdmissionPool") { QueueCapacity = 1 });

        await pool.InitializeAsync(TestToken);

        var activeTask = pool.ExecuteAsync(
            (_, _) =>
            {
                enterActive.Set();
                releaseActive.Wait(GateTimeout, TestToken).Should().BeTrue();
                return 10;
            },
            cancellationToken: TestToken);

        Task<int>? queuedTask = null;
        try
        {
            enterActive.Wait(GateTimeout, TestToken).Should().BeTrue();
            queuedTask = pool.ExecuteAsync(static (_, _) => 20, cancellationToken: TestToken);

            Func<Task<int>> overflow = async () =>
                await pool.ExecuteValueAsync(static (_, _) => 30, cancellationToken: TestToken);
            var rejected = (await overflow.Should().ThrowAsync<ExecutionAdmissionRejectedException>()).Which;
            rejected.WorkerName.Should().Be("AdmissionPool #1");
            rejected.QueueCapacity.Should().Be(1);
        }
        finally
        {
            releaseActive.Set();
        }

        (await activeTask).Should().Be(10);
        (await queuedTask).Should().Be(20);
    }
}
