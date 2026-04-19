namespace AdaskoTheBeAsT.Interop.Execution;

/// <summary>
/// <see cref="ValueTask"/> / <see cref="ValueTask{TResult}"/> hot-path overloads
/// over <see cref="IExecutionWorker{TSession}"/> and
/// <see cref="IExecutionWorkerPool{TSession}"/> <c>ExecuteAsync</c> methods.
/// </summary>
/// <remarks>
/// Hot callers that already expose a <see cref="ValueTask"/>-shaped surface
/// (for example async COM-interop wrappers) can use these overloads to stay in
/// the <see cref="ValueTask"/> domain without manually wrapping the returned
/// <see cref="Task"/>. When the receiver is the concrete
/// <see cref="ExecutionWorker{TSession}"/> / <see cref="ExecutionWorkerPool{TSession}"/>
/// shipped by this library and the runtime is <c>NET8_0_OR_GREATER</c>, these
/// extensions dispatch to the built-in instance methods which use a pooled
/// <see cref="System.Threading.Tasks.Sources.IValueTaskSource{TResult}"/> —
/// no <see cref="Task{TResult}"/> is allocated on the hot path. For custom
/// <see cref="IExecutionWorker{TSession}"/> implementations and on older
/// TFMs, the extension falls back to wrapping the returned <see cref="Task"/>
/// in a <see cref="ValueTask"/>.
/// </remarks>
public static class ExecutionWorkerValueTaskExtensions
{
    /// <summary>
    /// <see cref="ValueTask"/> overload of
    /// <see cref="IExecutionWorker{TSession}.ExecuteAsync(Action{TSession, CancellationToken}, ExecutionRequestOptions?, CancellationToken)"/>.
    /// </summary>
    /// <typeparam name="TSession">The session type exposed to the submitted work item.</typeparam>
    /// <param name="worker">The target worker.</param>
    /// <param name="action">Callback invoked with the session and the effective cancellation token.</param>
    /// <param name="options">Optional per-call tuning.</param>
    /// <param name="cancellationToken">Token observed during enqueue and during execution.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when <paramref name="action"/> finishes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="worker"/> is <see langword="null"/>.</exception>
    public static ValueTask ExecuteValueAsync<TSession>(
        this IExecutionWorker<TSession> worker,
        Action<TSession, CancellationToken> action,
        ExecutionRequestOptions? options = null,
        CancellationToken cancellationToken = default)
        where TSession : class
    {
        if (worker is null)
        {
            throw new ArgumentNullException(nameof(worker));
        }

#if NET8_0_OR_GREATER
        if (worker is ExecutionWorker<TSession> concrete)
        {
            return concrete.ExecuteValueAsync(action, options, cancellationToken);
        }
#endif

        var task = worker.ExecuteAsync(action, options, cancellationToken);
        return new ValueTask(task);
    }

    /// <summary>
    /// <see cref="ValueTask{TResult}"/> overload of
    /// <see cref="IExecutionWorker{TSession}.ExecuteAsync{TResult}(Func{TSession, CancellationToken, TResult}, ExecutionRequestOptions?, CancellationToken)"/>.
    /// </summary>
    /// <typeparam name="TSession">The session type exposed to the submitted work item.</typeparam>
    /// <typeparam name="TResult">The result type returned by <paramref name="action"/>.</typeparam>
    /// <param name="worker">The target worker.</param>
    /// <param name="action">Callback invoked with the session and the effective cancellation token.</param>
    /// <param name="options">Optional per-call tuning.</param>
    /// <param name="cancellationToken">Token observed during enqueue and during execution.</param>
    /// <returns>A <see cref="ValueTask{TResult}"/> producing the value returned by <paramref name="action"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="worker"/> is <see langword="null"/>.</exception>
    public static ValueTask<TResult> ExecuteValueAsync<TSession, TResult>(
        this IExecutionWorker<TSession> worker,
        Func<TSession, CancellationToken, TResult> action,
        ExecutionRequestOptions? options = null,
        CancellationToken cancellationToken = default)
        where TSession : class
    {
        if (worker is null)
        {
            throw new ArgumentNullException(nameof(worker));
        }

#if NET8_0_OR_GREATER
        if (worker is ExecutionWorker<TSession> concrete)
        {
            return concrete.ExecuteValueAsync(action, options, cancellationToken);
        }
#endif

        var task = worker.ExecuteAsync(action, options, cancellationToken);
        return new ValueTask<TResult>(task);
    }

    /// <summary>
    /// <see cref="ValueTask"/> overload of
    /// <see cref="IExecutionWorkerPool{TSession}.ExecuteAsync(Action{TSession, CancellationToken}, ExecutionRequestOptions?, CancellationToken)"/>.
    /// </summary>
    /// <typeparam name="TSession">The session type exposed to the submitted work item.</typeparam>
    /// <param name="pool">The target pool.</param>
    /// <param name="action">Callback invoked with the session and the effective cancellation token.</param>
    /// <param name="options">Optional per-call tuning.</param>
    /// <param name="cancellationToken">Token observed during enqueue and during execution.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when <paramref name="action"/> finishes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pool"/> is <see langword="null"/>.</exception>
    public static ValueTask ExecuteValueAsync<TSession>(
        this IExecutionWorkerPool<TSession> pool,
        Action<TSession, CancellationToken> action,
        ExecutionRequestOptions? options = null,
        CancellationToken cancellationToken = default)
        where TSession : class
    {
        if (pool is null)
        {
            throw new ArgumentNullException(nameof(pool));
        }

#if NET8_0_OR_GREATER
        if (pool is ExecutionWorkerPool<TSession> concrete)
        {
            return concrete.ExecuteValueAsync(action, options, cancellationToken);
        }
#endif

        var task = pool.ExecuteAsync(action, options, cancellationToken);
        return new ValueTask(task);
    }

    /// <summary>
    /// <see cref="ValueTask{TResult}"/> overload of
    /// <see cref="IExecutionWorkerPool{TSession}.ExecuteAsync{TResult}(Func{TSession, CancellationToken, TResult}, ExecutionRequestOptions?, CancellationToken)"/>.
    /// </summary>
    /// <typeparam name="TSession">The session type exposed to the submitted work item.</typeparam>
    /// <typeparam name="TResult">The result type returned by <paramref name="action"/>.</typeparam>
    /// <param name="pool">The target pool.</param>
    /// <param name="action">Callback invoked with the session and the effective cancellation token.</param>
    /// <param name="options">Optional per-call tuning.</param>
    /// <param name="cancellationToken">Token observed during enqueue and during execution.</param>
    /// <returns>A <see cref="ValueTask{TResult}"/> producing the value returned by <paramref name="action"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pool"/> is <see langword="null"/>.</exception>
    public static ValueTask<TResult> ExecuteValueAsync<TSession, TResult>(
        this IExecutionWorkerPool<TSession> pool,
        Func<TSession, CancellationToken, TResult> action,
        ExecutionRequestOptions? options = null,
        CancellationToken cancellationToken = default)
        where TSession : class
    {
        if (pool is null)
        {
            throw new ArgumentNullException(nameof(pool));
        }

#if NET8_0_OR_GREATER
        if (pool is ExecutionWorkerPool<TSession> concrete)
        {
            return concrete.ExecuteValueAsync(action, options, cancellationToken);
        }
#endif

        var task = pool.ExecuteAsync(action, options, cancellationToken);
        return new ValueTask<TResult>(task);
    }
}
