using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks.Sources;

namespace AdaskoTheBeAsT.Interop.Execution;

/// <summary>
/// Pooled <see cref="IValueTaskSource{TResult}"/> implementation used to back
/// <see cref="ExecutionWorker{TSession}.ExecuteValueAsync{TResult}"/> and
/// <see cref="ExecutionWorkerPool{TSession}.ExecuteValueAsync{TResult}"/> on
/// modern .NET TFMs. Compared with the <see cref="TaskCompletionSource{TResult}"/>-backed
/// work item, this:
/// <list type="bullet">
/// <item><description>avoids the <see cref="Task{TResult}"/> allocation — a
/// <see cref="ValueTask{TResult}"/> wraps the source + token directly;</description></item>
/// <item><description>reuses a single heap object across submissions through a
/// bounded per-closed-generic pool (see <see cref="Pool"/>);</description></item>
/// <item><description>keeps the <see cref="ManualResetValueTaskSourceCore{TResult}"/>
/// contract intact — callers are expected to observe the <see cref="ValueTask{TResult}"/>
/// exactly once, as required by the framework spec.</description></item>
/// </list>
/// </summary>
/// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
/// <typeparam name="TResult">The result type returned by the submitted delegate.</typeparam>
internal sealed class PooledValueExecutionWorkItem<TSession, TResult>
    : IExecutionWorkItem<TSession>, IValueTaskSource<TResult>, IValueTaskSource
    where TSession : class
{
    private const int MaxPoolSize = 256;

    private static readonly ConcurrentQueue<PooledValueExecutionWorkItem<TSession, TResult>> Pool = new();
    private static int _pooledCount;

    // CC0121 disabled: ManualResetValueTaskSourceCore<TResult> is a mutable
    // struct by design. Marking the field readonly would cause every call that
    // mutates it (SetResult / SetException / Reset / OnCompleted) to operate
    // on a defensive copy instead of the instance field, silently breaking
    // the IValueTaskSource<T> state machine.
#pragma warning disable CC0121
    private ManualResetValueTaskSourceCore<TResult> _core = new() { RunContinuationsAsynchronously = true };
#pragma warning restore CC0121
    private Func<TSession, CancellationToken, TResult>? _action;
    private ExecutionRequestOptions _options = ExecutionRequestOptions.Default;
    private CancellationToken _cancellationToken;
    private TResult? _result;
    private int _completed;
    private bool _canceled;

    private PooledValueExecutionWorkItem()
    {
    }

    public CancellationToken CancellationToken => _cancellationToken;

    public ExecutionRequestOptions Options => _options;

    public ActivityContext ParentContext { get; private set; }

    public short Version => _core.Version;

    public static PooledValueExecutionWorkItem<TSession, TResult> Rent(
        Func<TSession, CancellationToken, TResult> action,
        ExecutionRequestOptions options,
        CancellationToken cancellationToken)
    {
        if (Pool.TryDequeue(out var item))
        {
            Interlocked.Decrement(ref _pooledCount);
        }
        else
        {
            item = new PooledValueExecutionWorkItem<TSession, TResult>();
        }

        item._action = action;
        item._options = options;
        item._cancellationToken = cancellationToken;
        item.ParentContext = Activity.Current?.Context ?? default;
        item._completed = 0;
        item._canceled = false;
        return item;
    }

    public void Execute(TSession session)
    {
        var action = _action;
        if (action is null)
        {
            throw new InvalidOperationException("Work item action is unavailable.");
        }

        // The worker publishes completion only after all session/telemetry work.
        _result = action(session, _cancellationToken);
    }

    public void TrySetResult()
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0)
        {
            _core.SetResult(_result!);
        }
    }

    public void TrySetException(Exception exception)
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0)
        {
            _core.SetException(exception);
        }
    }

    public void TrySetCanceled()
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0)
        {
            _canceled = true;
            _core.SetException(new OperationCanceledException(_cancellationToken));
        }
    }

    public TResult GetResult(short token)
    {
        // Stale-token guard: if the token does not match the current source
        // version, the caller is awaiting a recycled instance. Delegate to
        // MRVTSC (which will throw InvalidOperationException by spec) without
        // recycling the item — another caller still owns it.
        if (token != _core.Version || _core.GetStatus(token) == ValueTaskSourceStatus.Pending)
        {
            return _core.GetResult(token);
        }

        try
        {
            return _core.GetResult(token);
        }
        finally
        {
            Return();
        }
    }

    public ValueTaskSourceStatus GetStatus(short token)
    {
        var status = _core.GetStatus(token);
        return status == ValueTaskSourceStatus.Canceled && !_canceled ? ValueTaskSourceStatus.Faulted : status;
    }

    public void OnCompleted(
        Action<object?> continuation,
        object? state,
        short token,
        ValueTaskSourceOnCompletedFlags flags)
    {
        _core.OnCompleted(continuation, state, token, flags);
    }

    void IValueTaskSource.GetResult(short token)
    {
        if (token != _core.Version || _core.GetStatus(token) == ValueTaskSourceStatus.Pending)
        {
            _core.GetResult(token);
            return;
        }

        try
        {
            _core.GetResult(token);
        }
        finally
        {
            Return();
        }
    }

    ValueTaskSourceStatus IValueTaskSource.GetStatus(short token) => GetStatus(token);

    void IValueTaskSource.OnCompleted(
        Action<object?> continuation,
        object? state,
        short token,
        ValueTaskSourceOnCompletedFlags flags)
    {
        _core.OnCompleted(continuation, state, token, flags);
    }

    private void Return()
    {
        _action = null;
        _options = ExecutionRequestOptions.Default;
        _cancellationToken = default;
        _result = default;
        ParentContext = default;
        _core.Reset();

        if (Interlocked.Increment(ref _pooledCount) > MaxPoolSize)
        {
            Interlocked.Decrement(ref _pooledCount);
            return;
        }

        Pool.Enqueue(this);
    }
}
