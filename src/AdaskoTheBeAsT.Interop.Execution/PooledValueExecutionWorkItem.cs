using System.Collections.Concurrent;
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

    private PooledValueExecutionWorkItem()
    {
    }

    public CancellationToken CancellationToken => _cancellationToken;

    public ExecutionRequestOptions Options => _options;

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
        return item;
    }

    public void Execute(TSession session)
    {
        var action = _action;
        if (action is null)
        {
            // Defensive: Rent always sets _action; a null here would indicate
            // double-execution which would corrupt the MRVTSC state machine.
            _core.SetException(new InvalidOperationException("Work item action is unavailable."));
            return;
        }

        try
        {
            var result = action(session, _cancellationToken);
            _core.SetResult(result);
        }
        catch (OperationCanceledException exception) when (_cancellationToken.IsCancellationRequested)
        {
            _core.SetException(exception);
        }
        catch (Exception exception)
        {
            _core.SetException(exception);
        }
    }

    public void TrySetException(Exception exception)
    {
        _core.SetException(exception);
    }

    public void TrySetCanceled()
    {
        _core.SetException(new OperationCanceledException(_cancellationToken));
    }

    public TResult GetResult(short token)
    {
        // Stale-token guard: if the token does not match the current source
        // version, the caller is awaiting a recycled instance. Delegate to
        // MRVTSC (which will throw InvalidOperationException by spec) without
        // recycling the item — another caller still owns it.
        if (token != _core.Version)
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

    public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

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
        if (token != _core.Version)
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

    ValueTaskSourceStatus IValueTaskSource.GetStatus(short token) => _core.GetStatus(token);

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
        _core.Reset();

        if (Interlocked.Increment(ref _pooledCount) > MaxPoolSize)
        {
            Interlocked.Decrement(ref _pooledCount);
            return;
        }

        Pool.Enqueue(this);
    }
}
