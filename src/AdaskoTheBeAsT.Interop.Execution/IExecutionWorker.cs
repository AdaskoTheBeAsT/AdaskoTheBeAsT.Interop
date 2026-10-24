namespace AdaskoTheBeAsT.Interop.Execution;

/// <summary>
/// Serializes work items onto a single dedicated <see cref="System.Threading.Thread"/>
/// that owns exclusive access to a <typeparamref name="TSession"/> instance.
/// Designed for wrapping STA COM/native sessions safely from async code across
/// .NET Framework and modern .NET.
/// </summary>
/// <remarks>
/// Delegates must finish synchronously. Do not pass async delegates or wait
/// synchronously for nested work on this worker. STA selection does not provide
/// a COM/UI message pump. Cancellation is cooperative; native calls cannot be
/// forcibly interrupted. External DisposeAsync waits for actual session teardown;
/// synchronous Dispose can abandon its wait at the configured timeout.
/// </remarks>
/// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
public interface IExecutionWorker<TSession> : IExecutionDispatcher<TSession>
    where TSession : class
{
    /// <summary>
    /// Gets a value indicating whether the worker has faulted. Once
    /// <see langword="true"/>, all further <c>ExecuteAsync</c> calls throw the
    /// fault exception synchronously.
    /// </summary>
    bool IsFaulted { get; }

    /// <summary>Gets the fault exception, or <see langword="null"/> if not faulted.</summary>
    Exception? Fault { get; }

    /// <summary>
    /// Captures a coherent point-in-time <see cref="ExecutionWorkerSnapshot"/>
    /// of this worker. All fields on the returned snapshot are read within the
    /// same call so dashboards and health checks observe a consistent view
    /// across <see cref="IExecutionDispatcher{TSession}.QueueDepth"/>, <see cref="IsFaulted"/>, and
    /// <see cref="Fault"/>.
    /// </summary>
    /// <returns>A <see cref="ExecutionWorkerSnapshot"/> describing the current state of the worker.</returns>
    ExecutionWorkerSnapshot GetSnapshot();
}
