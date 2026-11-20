using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace AdaskoTheBeAsT.Interop.Execution;

/// <summary>
/// Execution deadline extension overloads for <see cref="IExecutionDispatcher{TSession}"/>,
/// <see cref="IExecutionWorker{TSession}"/>, and <see cref="IExecutionWorkerPool{TSession}"/>.
/// </summary>
/// <remarks>
/// Implemented as extension methods rather than abstract interface members so
/// custom <see cref="IExecutionDispatcher{TSession}"/> implementations across
/// all target frameworks (including .NET Framework) remain source- and
/// binary-compatible.
/// </remarks>
public static class ExecutionDispatcherExtensions
{
    /// <summary>
    /// Enqueues a void work item bounded by <paramref name="timeout"/> across
    /// startup, queueing, and execution wait.
    /// </summary>
    /// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
    /// <param name="dispatcher">The target worker or worker pool.</param>
    /// <param name="action">Synchronous callback invoked with the session and effective cancellation token.</param>
    /// <param name="timeout">
    /// Maximum caller wait duration, or <see cref="Timeout.InfiniteTimeSpan"/> for no deadline.
    /// </param>
    /// <param name="cancellationToken">Caller cancellation token.</param>
    /// <returns>
    /// A task that completes with the operation outcome, faults with
    /// <see cref="TimeoutException"/> if <paramref name="timeout"/> expires first,
    /// or cancels with <paramref name="cancellationToken"/> if the caller cancels.
    /// </returns>
    public static Task ExecuteAsync<TSession>(
        this IExecutionDispatcher<TSession> dispatcher,
        Action<TSession, CancellationToken> action,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        where TSession : class
    {
        return ExecuteAsync(dispatcher, action, timeout, options: null, cancellationToken);
    }

    /// <summary>
    /// Enqueues a void work item bounded by <paramref name="timeout"/> across
    /// startup, queueing, and execution wait.
    /// </summary>
    /// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
    /// <param name="dispatcher">The target worker or worker pool.</param>
    /// <param name="action">Synchronous callback invoked with the session and effective cancellation token.</param>
    /// <param name="timeout">
    /// Maximum caller wait duration, or <see cref="Timeout.InfiniteTimeSpan"/> for no deadline.
    /// </param>
    /// <param name="options">Optional per-call tuning.</param>
    /// <param name="cancellationToken">Caller cancellation token.</param>
    /// <returns>
    /// A task that completes with the operation outcome, faults with
    /// <see cref="TimeoutException"/> if <paramref name="timeout"/> expires first,
    /// or cancels with <paramref name="cancellationToken"/> if the caller cancels.
    /// </returns>
    public static Task ExecuteAsync<TSession>(
        this IExecutionDispatcher<TSession> dispatcher,
        Action<TSession, CancellationToken> action,
        TimeSpan timeout,
        ExecutionRequestOptions? options,
        CancellationToken cancellationToken = default)
        where TSession : class
    {
        ThrowIfNull(dispatcher, nameof(dispatcher));
        ThrowIfNull(action, nameof(action));
        ValidateTimeout(timeout);

        if (timeout == Timeout.InfiniteTimeSpan)
        {
            return dispatcher.ExecuteAsync(action, options, cancellationToken);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        if (timeout == TimeSpan.Zero)
        {
            ValidateDispatcherCanSubmit(dispatcher);
            return Task.FromException(CreateTimeoutException(timeout));
        }

        var executionAction = action;
        return ExecuteAsync<TSession, object?>(
            dispatcher,
            (session, token) =>
            {
                executionAction(session, token);
                return null;
            },
            timeout,
            options,
            cancellationToken);
    }

    /// <summary>
    /// Enqueues a value-returning work item bounded by <paramref name="timeout"/>
    /// across startup, queueing, and execution wait.
    /// </summary>
    /// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
    /// <typeparam name="TResult">The result type returned by <paramref name="action"/>.</typeparam>
    /// <param name="dispatcher">The target worker or worker pool.</param>
    /// <param name="action">Synchronous callback invoked with the session and effective cancellation token.</param>
    /// <param name="timeout">
    /// Maximum caller wait duration, or <see cref="Timeout.InfiniteTimeSpan"/> for no deadline.
    /// </param>
    /// <param name="cancellationToken">Caller cancellation token.</param>
    /// <returns>
    /// A task producing the result, faulting with <see cref="TimeoutException"/>
    /// if <paramref name="timeout"/> expires first, or canceling with
    /// <paramref name="cancellationToken"/> if the caller cancels.
    /// </returns>
    public static Task<TResult> ExecuteAsync<TSession, TResult>(
        this IExecutionDispatcher<TSession> dispatcher,
        Func<TSession, CancellationToken, TResult> action,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        where TSession : class
    {
        return ExecuteAsync(dispatcher, action, timeout, options: null, cancellationToken);
    }

    /// <summary>
    /// Enqueues a value-returning work item bounded by <paramref name="timeout"/>
    /// across startup, queueing, and execution wait.
    /// </summary>
    /// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
    /// <typeparam name="TResult">The result type returned by <paramref name="action"/>.</typeparam>
    /// <param name="dispatcher">The target worker or worker pool.</param>
    /// <param name="action">Synchronous callback invoked with the session and effective cancellation token.</param>
    /// <param name="timeout">
    /// Maximum caller wait duration, or <see cref="Timeout.InfiniteTimeSpan"/> for no deadline.
    /// </param>
    /// <param name="options">Optional per-call tuning.</param>
    /// <param name="cancellationToken">Caller cancellation token.</param>
    /// <returns>
    /// A task producing the result, faulting with <see cref="TimeoutException"/>
    /// if <paramref name="timeout"/> expires first, or canceling with
    /// <paramref name="cancellationToken"/> if the caller cancels.
    /// </returns>
    public static Task<TResult> ExecuteAsync<TSession, TResult>(
        this IExecutionDispatcher<TSession> dispatcher,
        Func<TSession, CancellationToken, TResult> action,
        TimeSpan timeout,
        ExecutionRequestOptions? options,
        CancellationToken cancellationToken = default)
        where TSession : class
    {
        ThrowIfNull(dispatcher, nameof(dispatcher));
        ThrowIfNull(action, nameof(action));
        ValidateTimeout(timeout);

        if (timeout == Timeout.InfiniteTimeSpan)
        {
            return dispatcher.ExecuteAsync(action, options, cancellationToken);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<TResult>(cancellationToken);
        }

        if (timeout == TimeSpan.Zero)
        {
            ValidateDispatcherCanSubmit(dispatcher);
            return Task.FromException<TResult>(CreateTimeoutException(timeout));
        }

        // IDISP001 / CA2000: DeadlineState owns the rented/linked CTS lifetime and
        // disposes or returns them only after the underlying worker operation finishes.
#pragma warning disable CA2000, IDISP001
        var state = new ValueDeadlineState<TSession, TResult>(action, timeout, cancellationToken);
#pragma warning restore CA2000, IDISP001
        return state.StartTaskAsync(dispatcher, options);
    }

    /// <summary>
    /// Pooled <see cref="ValueTask"/> overload bounded by <paramref name="timeout"/>
    /// across startup, queueing, and execution wait.
    /// </summary>
    /// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
    /// <param name="dispatcher">The target worker or worker pool.</param>
    /// <param name="action">Synchronous callback invoked with the session and effective cancellation token.</param>
    /// <param name="timeout">
    /// Maximum caller wait duration, or <see cref="Timeout.InfiniteTimeSpan"/> for no deadline.
    /// </param>
    /// <param name="cancellationToken">Caller cancellation token.</param>
    /// <returns>
    /// A <see cref="ValueTask"/> that completes with the operation outcome,
    /// faults with <see cref="TimeoutException"/> if <paramref name="timeout"/>
    /// expires first, or cancels with <paramref name="cancellationToken"/> if
    /// the caller cancels.
    /// </returns>
    public static ValueTask ExecuteValueAsync<TSession>(
        this IExecutionDispatcher<TSession> dispatcher,
        Action<TSession, CancellationToken> action,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        where TSession : class
    {
        return ExecuteValueAsync(dispatcher, action, timeout, options: null, cancellationToken);
    }

    /// <summary>
    /// Pooled <see cref="ValueTask"/> overload bounded by <paramref name="timeout"/>
    /// across startup, queueing, and execution wait.
    /// </summary>
    /// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
    /// <param name="dispatcher">The target worker or worker pool.</param>
    /// <param name="action">Synchronous callback invoked with the session and effective cancellation token.</param>
    /// <param name="timeout">
    /// Maximum caller wait duration, or <see cref="Timeout.InfiniteTimeSpan"/> for no deadline.
    /// </param>
    /// <param name="options">Optional per-call tuning.</param>
    /// <param name="cancellationToken">Caller cancellation token.</param>
    /// <returns>
    /// A <see cref="ValueTask"/> that completes with the operation outcome,
    /// faults with <see cref="TimeoutException"/> if <paramref name="timeout"/>
    /// expires first, or cancels with <paramref name="cancellationToken"/> if
    /// the caller cancels.
    /// </returns>
    public static ValueTask ExecuteValueAsync<TSession>(
        this IExecutionDispatcher<TSession> dispatcher,
        Action<TSession, CancellationToken> action,
        TimeSpan timeout,
        ExecutionRequestOptions? options,
        CancellationToken cancellationToken = default)
        where TSession : class
    {
        ThrowIfNull(dispatcher, nameof(dispatcher));
        ThrowIfNull(action, nameof(action));
        ValidateTimeout(timeout);

        if (timeout == Timeout.InfiniteTimeSpan)
        {
            return dispatcher.ExecuteValueAsync(action, options, cancellationToken);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return new ValueTask(Task.FromCanceled(cancellationToken));
        }

        if (timeout == TimeSpan.Zero)
        {
            ValidateDispatcherCanSubmit(dispatcher);
            return new ValueTask(Task.FromException(CreateTimeoutException(timeout)));
        }

#pragma warning disable CA2000, IDISP001
        var state = new VoidDeadlineState<TSession>(action, timeout, cancellationToken);
#pragma warning restore CA2000, IDISP001
        return state.StartValueTaskAsync(dispatcher, options);
    }

    /// <summary>
    /// Pooled <see cref="ValueTask{TResult}"/> overload bounded by
    /// <paramref name="timeout"/> across startup, queueing, and execution wait.
    /// </summary>
    /// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
    /// <typeparam name="TResult">The result type returned by <paramref name="action"/>.</typeparam>
    /// <param name="dispatcher">The target worker or worker pool.</param>
    /// <param name="action">Synchronous callback invoked with the session and effective cancellation token.</param>
    /// <param name="timeout">
    /// Maximum caller wait duration, or <see cref="Timeout.InfiniteTimeSpan"/> for no deadline.
    /// </param>
    /// <param name="cancellationToken">Caller cancellation token.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> producing the result, faulting with
    /// <see cref="TimeoutException"/> if <paramref name="timeout"/> expires first,
    /// or canceling with <paramref name="cancellationToken"/> if the caller cancels.
    /// </returns>
    public static ValueTask<TResult> ExecuteValueAsync<TSession, TResult>(
        this IExecutionDispatcher<TSession> dispatcher,
        Func<TSession, CancellationToken, TResult> action,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        where TSession : class
    {
        return ExecuteValueAsync(dispatcher, action, timeout, options: null, cancellationToken);
    }

    /// <summary>
    /// Pooled <see cref="ValueTask{TResult}"/> overload bounded by
    /// <paramref name="timeout"/> across startup, queueing, and execution wait.
    /// </summary>
    /// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
    /// <typeparam name="TResult">The result type returned by <paramref name="action"/>.</typeparam>
    /// <param name="dispatcher">The target worker or worker pool.</param>
    /// <param name="action">Synchronous callback invoked with the session and effective cancellation token.</param>
    /// <param name="timeout">
    /// Maximum caller wait duration, or <see cref="Timeout.InfiniteTimeSpan"/> for no deadline.
    /// </param>
    /// <param name="options">Optional per-call tuning.</param>
    /// <param name="cancellationToken">Caller cancellation token.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> producing the result, faulting with
    /// <see cref="TimeoutException"/> if <paramref name="timeout"/> expires first,
    /// or canceling with <paramref name="cancellationToken"/> if the caller cancels.
    /// </returns>
    public static ValueTask<TResult> ExecuteValueAsync<TSession, TResult>(
        this IExecutionDispatcher<TSession> dispatcher,
        Func<TSession, CancellationToken, TResult> action,
        TimeSpan timeout,
        ExecutionRequestOptions? options,
        CancellationToken cancellationToken = default)
        where TSession : class
    {
        ThrowIfNull(dispatcher, nameof(dispatcher));
        ThrowIfNull(action, nameof(action));
        ValidateTimeout(timeout);

        if (timeout == Timeout.InfiniteTimeSpan)
        {
            return dispatcher.ExecuteValueAsync(action, options, cancellationToken);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return new ValueTask<TResult>(Task.FromCanceled<TResult>(cancellationToken));
        }

        if (timeout == TimeSpan.Zero)
        {
            ValidateDispatcherCanSubmit(dispatcher);
            return new ValueTask<TResult>(Task.FromException<TResult>(CreateTimeoutException(timeout)));
        }

#pragma warning disable CA2000, IDISP001
        var state = new ValueDeadlineState<TSession, TResult>(action, timeout, cancellationToken);
#pragma warning restore CA2000, IDISP001
        return state.StartValueTaskAsync(dispatcher, options);
    }

    private static void ThrowIfNull<T>(T? argument, string paramName)
        where T : class
    {
        if (argument is null)
        {
            throw new ArgumentNullException(paramName);
        }
    }

    private static void ValidateTimeout(TimeSpan timeout)
    {
        if ((timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan) ||
            timeout.TotalMilliseconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
    }

    private static void ValidateDispatcherCanSubmit<TSession>(IExecutionDispatcher<TSession> dispatcher)
        where TSession : class
    {
        if (dispatcher is ExecutionWorker<TSession> worker)
        {
            worker.ValidateCanSubmit();
        }
        else if (dispatcher is ExecutionWorkerPool<TSession> pool)
        {
            pool.ValidateCanSubmit();
        }
    }

    private static TimeoutException CreateTimeoutException(TimeSpan timeout) =>
        new(string.Format(
            CultureInfo.InvariantCulture,
            "The execution operation timed out after {0}.",
            timeout));

    private static class DeadlineCtsPool
    {
#if NET6_0_OR_GREATER
        private const int MaxPoolSize = 256;
        private static readonly ConcurrentQueue<CancellationTokenSource> Pool = new();
        private static int _pooledCount;
#endif

        public static CancellationTokenSource Rent(TimeSpan timeout)
        {
#if NET6_0_OR_GREATER
            if (Pool.TryDequeue(out var cts))
            {
                Interlocked.Decrement(ref _pooledCount);
                cts.CancelAfter(timeout);
                return cts;
            }
#endif
            return new CancellationTokenSource(timeout);
        }

        public static void Return(CancellationTokenSource cts)
        {
#if NET6_0_OR_GREATER
            if (!cts.IsCancellationRequested)
            {
                cts.CancelAfter(Timeout.InfiniteTimeSpan);
                if (!cts.IsCancellationRequested && cts.TryReset())
                {
                    if (Interlocked.Increment(ref _pooledCount) <= MaxPoolSize)
                    {
                        Pool.Enqueue(cts);
                        return;
                    }

                    Interlocked.Decrement(ref _pooledCount);
                }
            }
#endif

            // IDISP007: DeadlineCtsPool owns CTS instances rented through Rent(timeout)
            // and disposes instances that have fired or exceed the bounded pool capacity.
#pragma warning disable IDISP007
            cts.Dispose();
#pragma warning restore IDISP007
        }
    }

    // CA1001 / IDISP003 / IDISP006: lifetime is tied to the asynchronous completion
    // of the underlying worker operation, not lexical scope. CleanupResources runs
    // strictly once after the worker finishes observing the cancellation tokens.
#pragma warning disable CA1001, IDISP003, IDISP006
    private abstract class DeadlineStateBase
    {
        private const int StatePending = 0;
        private const int StateCompletedByInner = 1;
        private const int StateCompletedEarly = 2;

        private readonly TimeSpan _timeout;
        private readonly CancellationToken _callerToken;
        private readonly CancellationTokenSource _deadlineCts;
        private readonly CancellationTokenSource? _linkedCts;

        private CancellationTokenRegistration _callerRegistration;
        private CancellationTokenRegistration _deadlineRegistration;
        private int _callerSettled;
        private int _cleanedUp;

        protected DeadlineStateBase(TimeSpan timeout, CancellationToken callerToken)
        {
            _timeout = timeout;
            _callerToken = callerToken;
            _deadlineCts = DeadlineCtsPool.Rent(timeout);
            if (callerToken.CanBeCanceled)
            {
                _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(callerToken, _deadlineCts.Token);
                EffectiveToken = _linkedCts.Token;
            }
            else
            {
                EffectiveToken = _deadlineCts.Token;
            }
        }

        protected CancellationToken EffectiveToken { get; }

        protected void ThrowIfExpiredBeforeStart(CancellationToken token)
        {
            if (Volatile.Read(ref _callerSettled) != StatePending || token.IsCancellationRequested)
            {
                throw new OperationCanceledException(token);
            }
        }

        protected void ArmRegistrations()
        {
            if (_callerToken.CanBeCanceled)
            {
                _callerRegistration = _callerToken.Register(
                    static state =>
                    {
                        if (state is DeadlineStateBase deadlineState)
                        {
                            deadlineState.OnCallerCanceled();
                        }
                    },
                    this);
            }

            _deadlineRegistration = _deadlineCts.Token.Register(
                static state =>
                {
                    if (state is DeadlineStateBase deadlineState)
                    {
                        deadlineState.OnDeadlineExpired();
                    }
                },
                this);
        }

        protected bool TryClaimInnerCompletion()
        {
            return Interlocked.CompareExchange(ref _callerSettled, StateCompletedByInner, StatePending) == StatePending;
        }

        protected void SettleInnerFailure(Exception exception, bool wasCanceled)
        {
            if (!TryClaimInnerCompletion())
            {
                return;
            }

            if (wasCanceled || (exception is OperationCanceledException && EffectiveToken.IsCancellationRequested))
            {
                SettleCancellation();
            }
            else
            {
                TrySetCallerException(exception);
            }
        }

        protected void SettleCancellation()
        {
            if (_callerToken.IsCancellationRequested)
            {
                TrySetCallerCanceled(_callerToken);
            }
            else if (_deadlineCts.IsCancellationRequested)
            {
                TrySetCallerException(CreateTimeoutException(_timeout));
            }
            else
            {
                TrySetCallerCanceled(CancellationToken.None);
            }
        }

        protected void CleanupResources()
        {
            if (Interlocked.Exchange(ref _cleanedUp, 1) != 0)
            {
                return;
            }

            _callerRegistration.Dispose();
            _deadlineRegistration.Dispose();
            _linkedCts?.Dispose();
            DeadlineCtsPool.Return(_deadlineCts);
        }

        protected abstract void TrySetCallerCanceled(CancellationToken token);

        protected abstract void TrySetCallerException(Exception exception);

        private void OnCallerCanceled()
        {
            if (Interlocked.CompareExchange(ref _callerSettled, StateCompletedEarly, StatePending) != StatePending)
            {
                return;
            }

            TrySetCallerCanceled(_callerToken);
        }

        private void OnDeadlineExpired()
        {
            if (Interlocked.CompareExchange(ref _callerSettled, StateCompletedEarly, StatePending) != StatePending)
            {
                return;
            }

            if (_callerToken.IsCancellationRequested)
            {
                TrySetCallerCanceled(_callerToken);
            }
            else
            {
                TrySetCallerException(CreateTimeoutException(_timeout));
            }
        }
    }

    private sealed class ValueDeadlineState<TSession, TResult>(
        Func<TSession, CancellationToken, TResult> action,
        TimeSpan timeout,
        CancellationToken callerToken) : DeadlineStateBase(timeout, callerToken)
        where TSession : class
    {
        private readonly Func<TSession, CancellationToken, TResult> _action = action;
        private readonly TaskCompletionSource<TResult> _callerTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private ValueTask<TResult> _innerValueTask;
        private ConfiguredValueTaskAwaitable<TResult>.ConfiguredValueTaskAwaiter _innerAwaiter;

        public Task<TResult> StartTaskAsync(
            IExecutionDispatcher<TSession> dispatcher,
            ExecutionRequestOptions? options)
        {
            Task<TResult> innerTask;
            try
            {
                innerTask = dispatcher.ExecuteAsync(ExecuteCallback, options, EffectiveToken);
            }
            catch
            {
                CleanupResources();
                throw;
            }

            if (innerTask.IsCompleted)
            {
                CompleteFromCompletedTask(innerTask);
                CleanupResources();
#pragma warning disable VSTHRD003
                return _callerTcs.Task;
#pragma warning restore VSTHRD003
            }

            ArmRegistrations();
            _ = innerTask.ContinueWith(
                static (completed, state) =>
                {
                    if (state is ValueDeadlineState<TSession, TResult> deadlineState)
                    {
                        deadlineState.OnInnerTaskCompleted(completed);
                    }
                },
                this,
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);

#pragma warning disable VSTHRD003
            return _callerTcs.Task;
#pragma warning restore VSTHRD003
        }

        public ValueTask<TResult> StartValueTaskAsync(
            IExecutionDispatcher<TSession> dispatcher,
            ExecutionRequestOptions? options)
        {
            ValueTask<TResult> innerValueTask;
            try
            {
                innerValueTask = dispatcher.ExecuteValueAsync(ExecuteCallback, options, EffectiveToken);
            }
            catch
            {
                CleanupResources();
                throw;
            }

            if (innerValueTask.IsCompleted)
            {
                _innerValueTask = innerValueTask;
                _innerAwaiter = innerValueTask.ConfigureAwait(false).GetAwaiter();
                CompleteFromInnerAwaiter();
                CleanupResources();
#pragma warning disable VSTHRD003
                return new ValueTask<TResult>(_callerTcs.Task);
#pragma warning restore VSTHRD003
            }

            ArmRegistrations();
            _innerValueTask = innerValueTask;
            _innerAwaiter = innerValueTask.ConfigureAwait(false).GetAwaiter();
            _innerAwaiter.OnCompleted(OnInnerValueTaskCompleted);

#pragma warning disable VSTHRD003
            return new ValueTask<TResult>(_callerTcs.Task);
#pragma warning restore VSTHRD003
        }

        protected override void TrySetCallerCanceled(CancellationToken token)
        {
            _callerTcs.TrySetCanceled(token);
        }

        protected override void TrySetCallerException(Exception exception)
        {
            _callerTcs.TrySetException(exception);
        }

        private static Exception UnwrapFault(AggregateException aggregateException)
        {
            return aggregateException.InnerExceptions.Count == 1
                ? aggregateException.InnerExceptions[0]
                : aggregateException;
        }

        private TResult ExecuteCallback(TSession session, CancellationToken token)
        {
            ThrowIfExpiredBeforeStart(token);
            return _action(session, token);
        }

        private void OnInnerTaskCompleted(Task<TResult> completedTask)
        {
            try
            {
                CompleteFromCompletedTask(completedTask);
            }
            finally
            {
                CleanupResources();
            }
        }

        private void CompleteFromCompletedTask(Task<TResult> completedTask)
        {
            if (completedTask.IsFaulted && completedTask.Exception is { } aggregateException)
            {
                SettleInnerFailure(UnwrapFault(aggregateException), wasCanceled: false);
                return;
            }

            if (completedTask.IsCanceled)
            {
                if (TryClaimInnerCompletion())
                {
                    SettleCancellation();
                }

                return;
            }

#pragma warning disable VSTHRD002
            var result = completedTask.GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
            if (TryClaimInnerCompletion())
            {
                _callerTcs.TrySetResult(result);
            }
        }

        private void OnInnerValueTaskCompleted()
        {
            try
            {
                CompleteFromInnerAwaiter();
            }
            finally
            {
                CleanupResources();
            }
        }

        private void CompleteFromInnerAwaiter()
        {
            var wasCanceled = _innerValueTask.IsCanceled;
            try
            {
                // VSTHRD002: _innerValueTask is guaranteed completed before this call
                // (either via IsCompleted fast-path or OnCompleted callback).
#pragma warning disable VSTHRD002
                var result = _innerAwaiter.GetResult();
#pragma warning restore VSTHRD002
                if (TryClaimInnerCompletion())
                {
                    _callerTcs.TrySetResult(result);
                }
            }
            catch (Exception exception)
            {
                SettleInnerFailure(exception, wasCanceled);
            }
        }
    }

    private sealed class VoidDeadlineState<TSession>(
        Action<TSession, CancellationToken> action,
        TimeSpan timeout,
        CancellationToken callerToken) : DeadlineStateBase(timeout, callerToken)
        where TSession : class
    {
        private readonly Action<TSession, CancellationToken> _action = action;
        private readonly TaskCompletionSource<byte> _callerTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private ValueTask _innerValueTask;
        private ConfiguredValueTaskAwaitable.ConfiguredValueTaskAwaiter _innerAwaiter;

        public ValueTask StartValueTaskAsync(
            IExecutionDispatcher<TSession> dispatcher,
            ExecutionRequestOptions? options)
        {
            ValueTask innerValueTask;
            try
            {
                innerValueTask = dispatcher.ExecuteValueAsync(ExecuteCallback, options, EffectiveToken);
            }
            catch
            {
                CleanupResources();
                throw;
            }

            if (innerValueTask.IsCompleted)
            {
                _innerValueTask = innerValueTask;
                _innerAwaiter = innerValueTask.ConfigureAwait(false).GetAwaiter();
                CompleteFromInnerAwaiter();
                CleanupResources();
#pragma warning disable VSTHRD003
                return new ValueTask(_callerTcs.Task);
#pragma warning restore VSTHRD003
            }

            ArmRegistrations();
            _innerValueTask = innerValueTask;
            _innerAwaiter = innerValueTask.ConfigureAwait(false).GetAwaiter();
            _innerAwaiter.OnCompleted(OnInnerValueTaskCompleted);

#pragma warning disable VSTHRD003
            return new ValueTask(_callerTcs.Task);
#pragma warning restore VSTHRD003
        }

        protected override void TrySetCallerCanceled(CancellationToken token)
        {
            _callerTcs.TrySetCanceled(token);
        }

        protected override void TrySetCallerException(Exception exception)
        {
            _callerTcs.TrySetException(exception);
        }

        private void ExecuteCallback(TSession session, CancellationToken token)
        {
            ThrowIfExpiredBeforeStart(token);
            _action(session, token);
        }

        private void OnInnerValueTaskCompleted()
        {
            try
            {
                CompleteFromInnerAwaiter();
            }
            finally
            {
                CleanupResources();
            }
        }

        private void CompleteFromInnerAwaiter()
        {
            var wasCanceled = _innerValueTask.IsCanceled;
            try
            {
                // VSTHRD002: _innerValueTask is guaranteed completed before this call.
#pragma warning disable VSTHRD002
                _innerAwaiter.GetResult();
#pragma warning restore VSTHRD002
                if (TryClaimInnerCompletion())
                {
                    _callerTcs.TrySetResult(default);
                }
            }
            catch (Exception exception)
            {
                SettleInnerFailure(exception, wasCanceled);
            }
        }
    }
#pragma warning restore CA1001, IDISP003, IDISP006
}
