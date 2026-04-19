namespace AdaskoTheBeAsT.Interop.Execution;

internal sealed class ExecutionWorkerRegistration(string? name, Func<int> queueDepthAccessor)
{
    private readonly Func<int> _queueDepthAccessor = queueDepthAccessor ?? throw new ArgumentNullException(nameof(queueDepthAccessor));

    public string? Name { get; } = name;

    public int GetQueueDepth()
    {
        return _queueDepthAccessor();
    }
}
