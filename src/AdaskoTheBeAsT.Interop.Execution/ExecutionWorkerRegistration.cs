namespace AdaskoTheBeAsT.Interop.Execution;

internal sealed class ExecutionWorkerRegistration
{
    private readonly TryGetQueueDepthCallback _tryGetQueueDepth;

    public ExecutionWorkerRegistration(string? name, Func<int> queueDepthAccessor)
    {
        _ = queueDepthAccessor ?? throw new ArgumentNullException(nameof(queueDepthAccessor));
        Name = name;
        _tryGetQueueDepth = new StrongDepthSource(queueDepthAccessor).TryGetQueueDepth;
    }

    private ExecutionWorkerRegistration(string? name, TryGetQueueDepthCallback tryGetQueueDepth)
    {
        Name = name;
        _tryGetQueueDepth = tryGetQueueDepth;
    }

    private delegate bool TryGetQueueDepthCallback(out int queueDepth);

    public string? Name { get; }

    public static ExecutionWorkerRegistration Create<TWorker>(
        string? name,
        TWorker worker,
        Func<TWorker, int> queueDepthAccessor)
        where TWorker : class
    {
        _ = worker ?? throw new ArgumentNullException(nameof(worker));
        _ = queueDepthAccessor ?? throw new ArgumentNullException(nameof(queueDepthAccessor));

        var source = new WeakDepthSource<TWorker>(new WeakReference<TWorker>(worker), queueDepthAccessor);
        return new ExecutionWorkerRegistration(name, source.TryGetQueueDepth);
    }

    public int GetQueueDepth()
    {
        return _tryGetQueueDepth(out var queueDepth) ? queueDepth : 0;
    }

    public bool TryGetQueueDepth(out int queueDepth)
    {
        return _tryGetQueueDepth(out queueDepth);
    }

    private sealed class StrongDepthSource(Func<int> queueDepthAccessor)
    {
        private readonly Func<int> _queueDepthAccessor = queueDepthAccessor;

        public bool TryGetQueueDepth(out int queueDepth)
        {
            queueDepth = _queueDepthAccessor();
            return true;
        }
    }

    private sealed class WeakDepthSource<TWorker>(
        WeakReference<TWorker> weakWorker,
        Func<TWorker, int> queueDepthAccessor)
        where TWorker : class
    {
        private readonly WeakReference<TWorker> _weakWorker = weakWorker;
        private readonly Func<TWorker, int> _queueDepthAccessor = queueDepthAccessor;

        public bool TryGetQueueDepth(out int queueDepth)
        {
            if (_weakWorker.TryGetTarget(out var target))
            {
                queueDepth = _queueDepthAccessor(target);
                return true;
            }

            queueDepth = 0;
            return false;
        }
    }
}
