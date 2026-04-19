namespace AdaskoTheBeAsT.Interop.Execution;

internal sealed class ExecutionWorkerRegistration
{
    private readonly Func<int> _queueDepthAccessor;

    public ExecutionWorkerRegistration(string? name, Func<int> queueDepthAccessor)
    {
        Name = name;
        _queueDepthAccessor = queueDepthAccessor ?? throw new ArgumentNullException(nameof(queueDepthAccessor));
    }

    public string? Name { get; }

    public int GetQueueDepth()
    {
        return _queueDepthAccessor();
    }
}
