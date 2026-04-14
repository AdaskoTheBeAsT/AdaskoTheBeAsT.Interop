using System.Runtime.ExceptionServices;
using System.Threading.Channels;

namespace AdaskoTheBeAsT.Interop.Execution;

public sealed class ExecutionWorker<TSession> : IDisposable
    where TSession : class
{
    private readonly Channel<ExecutionWorkItemBase> _channel;
#if NET9_0_OR_GREATER
    private readonly Lock _syncRoot = new();
#else
    private readonly object _syncRoot = new();
#endif
    private readonly CancellationTokenSource _workerCancellationTokenSource = new();
    private readonly IExecutionSessionFactory<TSession> _sessionFactory;
    private readonly ExecutionWorkerOptions _options;

    private ExceptionDispatchInfo? _fatalFailure;
    private Task? _startupTask;
    private Thread? _workerThread;
    private TSession? _session;
    private int _disposeState;
    private int _operationsProcessed;
    private bool _initialized;

    public ExecutionWorker(
        IExecutionSessionFactory<TSession> sessionFactory,
        ExecutionWorkerOptions? options = null)
    {
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
        _options = options ?? ExecutionWorkerOptions.Default;
        _channel = Channel.CreateUnbounded<ExecutionWorkItemBase>(
            new UnboundedChannelOptions
            {
                AllowSynchronousContinuations = false,
                SingleReader = true,
                SingleWriter = false,
            });
    }

    public void Initialize()
    {
        EnsureInitialized();
    }

    public Task ExecuteAsync(
        Action<TSession, CancellationToken> action,
        ExecutionRequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        var executionAction = action;
        return ExecuteAsync<object?>(
            (session, token) =>
            {
                executionAction(session, token);
                return null;
            },
            options,
            cancellationToken);
    }

    public Task<TResult> ExecuteAsync<TResult>(
        Func<TSession, CancellationToken, TResult> action,
        ExecutionRequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<TResult>(cancellationToken);
        }

        ThrowIfDisposed();
        EnsureInitialized();
        ThrowIfFaulted();

        var effectiveOptions = options ?? ExecutionRequestOptions.Default;
        var workItem = new ExecutionWorkItem<TResult>(action, effectiveOptions, cancellationToken);
        if (!_channel.Writer.TryWrite(workItem))
        {
            return Task.FromException<TResult>(new ObjectDisposedException(nameof(ExecutionWorker<TSession>)));
        }

        return workItem.Task;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _channel.Writer.TryComplete();
        _workerCancellationTokenSource.Cancel();

        Thread? workerThread;
        lock (_syncRoot)
        {
            workerThread = _workerThread;
        }

        if (workerThread is null)
        {
            _workerCancellationTokenSource.Dispose();
            return;
        }

        if (ReferenceEquals(workerThread, Thread.CurrentThread))
        {
            return;
        }

        workerThread.Join();
    }

    private void EnsureInitialized()
    {
        Task startupTask;
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            ThrowIfFaulted();

            if (!_initialized)
            {
                var startupCompletionSource = new TaskCompletionSource<object?>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var thread = new Thread(Process)
                {
                    IsBackground = true,
                    Name = CreateThreadName(),
                };

                ConfigureThread(thread);

                _workerThread = thread;
                _startupTask = startupCompletionSource.Task;
                _initialized = true;
                thread.Start(startupCompletionSource);
            }

            startupTask = _startupTask!;
        }

#pragma warning disable VSTHRD002
        startupTask.GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
    }

    private void Process(object? state)
    {
        var startupCompletionSource = state as TaskCompletionSource<object?>;
        if (startupCompletionSource is null)
        {
            throw new ArgumentException("Invalid worker startup state.", nameof(state));
        }

        Exception? fatalException = null;

        try
        {
            EnsureSessionCreated(_workerCancellationTokenSource.Token);
            startupCompletionSource.TrySetResult(null);
            ProcessChannel();
        }
        catch (OperationCanceledException) when (_workerCancellationTokenSource.IsCancellationRequested)
        {
            startupCompletionSource.TrySetResult(null);
        }
        catch (Exception exception)
        {
            fatalException = exception;
            startupCompletionSource.TrySetException(exception);
        }
        finally
        {
            if (fatalException is not null)
            {
                SetFatalFailure(ExceptionDispatchInfo.Capture(fatalException));
            }

            try
            {
                DisposeSession();
            }
            catch (Exception exception)
            {
                fatalException ??= exception;

                if (fatalException is not null)
                {
                    SetFatalFailure(ExceptionDispatchInfo.Capture(fatalException));
                }
            }

            _channel.Writer.TryComplete(fatalException);
            FailPendingItems(fatalException ?? new ObjectDisposedException(nameof(ExecutionWorker<TSession>)));
            _workerCancellationTokenSource.Dispose();
        }
    }

    private void ProcessChannel()
    {
        while (WaitToRead())
        {
            while (_channel.Reader.TryRead(out var workItem))
            {
                ProcessWorkItem(workItem);
            }
        }
    }

    private void ProcessWorkItem(ExecutionWorkItemBase workItem)
    {
        if (workItem.CancellationToken.IsCancellationRequested)
        {
            workItem.TrySetCanceled();
            return;
        }

        try
        {
            var session = EnsureSessionCreated(workItem.CancellationToken);
            workItem.Execute(session);
            _operationsProcessed++;

            if (_options.MaxOperationsPerSession > 0 &&
                _operationsProcessed >= _options.MaxOperationsPerSession)
            {
                DisposeSession();
            }
        }
        catch (OperationCanceledException) when (workItem.CancellationToken.IsCancellationRequested)
        {
            workItem.TrySetCanceled();
        }
        catch (Exception exception)
        {
            workItem.TrySetException(exception);

            if (workItem.Options.RecycleSessionOnFailure)
            {
                DisposeSession();
            }
        }
    }

    private TSession EnsureSessionCreated(CancellationToken cancellationToken)
    {
        if (_session is not null)
        {
            return _session;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var createdSession = _sessionFactory.CreateSession(cancellationToken);
        if (createdSession is null)
        {
            throw new InvalidOperationException("The session factory returned null.");
        }

        _session = createdSession;
        _operationsProcessed = 0;

        return createdSession;
    }

    private bool WaitToRead()
    {
#pragma warning disable VSTHRD002
        return _channel.Reader.WaitToReadAsync(_workerCancellationTokenSource.Token)
            .AsTask()
            .GetAwaiter()
            .GetResult();
#pragma warning restore VSTHRD002
    }

    private void ConfigureThread(Thread thread)
    {
        if (!_options.UseStaThread || Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            return;
        }

#pragma warning disable CA1416
        thread.SetApartmentState(ApartmentState.STA);
#pragma warning restore CA1416
    }

    private string CreateThreadName()
    {
        var name = _options.Name;
        return string.IsNullOrWhiteSpace(name)
            ? $"{typeof(TSession).Name} Execution Worker"
            : name!;
    }

    private void FailPendingItems(Exception exception)
    {
        while (_channel.Reader.TryRead(out var workItem))
        {
            if (workItem.CancellationToken.IsCancellationRequested)
            {
                workItem.TrySetCanceled();
            }
            else
            {
                workItem.TrySetException(exception);
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            throw new ObjectDisposedException(nameof(ExecutionWorker<TSession>));
        }
    }

    private void ThrowIfFaulted()
    {
        lock (_syncRoot)
        {
            if (_fatalFailure is not null)
            {
                _fatalFailure.Throw();
            }
        }
    }

    private void SetFatalFailure(ExceptionDispatchInfo fatalFailure)
    {
        lock (_syncRoot)
        {
            _fatalFailure = fatalFailure;
        }
    }

    private void DisposeSession()
    {
        var session = _session;
        if (session is null)
        {
            return;
        }

        _session = null;
        _operationsProcessed = 0;
        _sessionFactory.DisposeSession(session);
    }

    private abstract class ExecutionWorkItemBase
    {
        protected ExecutionWorkItemBase(
            ExecutionRequestOptions options,
            CancellationToken cancellationToken)
        {
            Options = options;
            CancellationToken = cancellationToken;
        }

        public CancellationToken CancellationToken { get; }

        public ExecutionRequestOptions Options { get; }

        public abstract void Execute(TSession session);

        public abstract void TrySetException(Exception exception);

        public abstract void TrySetCanceled();
    }

    private sealed class ExecutionWorkItem<TResult> : ExecutionWorkItemBase
    {
        private readonly Func<TSession, CancellationToken, TResult> _action;
        private readonly TaskCompletionSource<TResult> _completionSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ExecutionWorkItem(
            Func<TSession, CancellationToken, TResult> action,
            ExecutionRequestOptions options,
            CancellationToken cancellationToken)
            : base(options, cancellationToken)
        {
            _action = action;
        }

        public Task<TResult> Task => _completionSource.Task;

        public override void Execute(TSession session)
        {
            var result = _action(session, CancellationToken);
            _completionSource.TrySetResult(result);
        }

        public override void TrySetException(Exception exception)
        {
            _completionSource.TrySetException(exception);
        }

        public override void TrySetCanceled()
        {
            _completionSource.TrySetCanceled(CancellationToken);
        }
    }
}
