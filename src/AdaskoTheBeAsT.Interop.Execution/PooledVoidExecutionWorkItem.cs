using System.Collections.Concurrent;
using System.Threading.Tasks.Sources;

namespace AdaskoTheBeAsT.Interop.Execution;

/// <summary>
/// Pooled <see cref="IValueTaskSource"/> implementation used to back the void
/// <c>ExecuteValueAsync</c> overloads on modern .NET TFMs. Parallels
/// <see cref="PooledValueExecutionWorkItem{TSession, TResult}"/>; exists as a
/// dedicated type so the void path does not incur an <see cref="object"/> box
/// or a per-call <see cref="Func{T1, T2, TResult}"/> shim when wrapping an
/// <see cref="Action{T1, T2}"/>.
/// </summary>
/// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
internal sealed class PooledVoidExecutionWorkItem<TSession>
    : IExecutionWorkItem<TSession>, IValueTaskSource
    where TSession : class
{
    private const int MaxPoolSize = 256;

    private static readonly ConcurrentQueue<PooledVoidExecutionWorkItem<TSession>> Pool = new();
    private static int _pooledCount;

    // CC0121 disabled: ManualResetValueTaskSourceCore<byte> is a mutable
    // struct by design — see PooledValueExecutionWorkItem for the full
    // rationale. Making the field readonly would break the IValueTaskSource
    // state machine by forcing every mutating call through a defensive copy.
#pragma warning disable CC0121
    private ManualResetValueTaskSourceCore<byte> _core = new() { RunContinuationsAsynchronously = true };
#pragma warning restore CC0121
    private Action<TSession, CancellationToken>? _action;
    private ExecutionRequestOptions _options = ExecutionRequestOptions.Default;
    private CancellationToken _cancellationToken;

    private PooledVoidExecutionWorkItem()
    {
    }

    public CancellationToken CancellationToken => _cancellationToken;

    public ExecutionRequestOptions Options => _options;

    public short Version => _core.Version;

    public static PooledVoidExecutionWorkItem<TSession> Rent(
        Action<TSession, CancellationToken> action,
        ExecutionRequestOptions options,
        CancellationToken cancellationToken)
    {
        if (Pool.TryDequeue(out var item))
        {
            Interlocked.Decrement(ref _pooledCount);
        }
        else
        {
            item = new PooledVoidExecutionWorkItem<TSession>();
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
            _core.SetException(new InvalidOperationException("Work item action is unavailable."));
            return;
        }

        try
        {
            action(session, _cancellationToken);
            _core.SetResult(default);
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

    public void GetResult(short token)
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

    public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

    public void OnCompleted(
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
