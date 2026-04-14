namespace AdaskoTheBeAsT.Interop.Execution;

public sealed class ExecutionWorkerPoolOptions
{
    public ExecutionWorkerPoolOptions(
        int workerCount,
        string? name = null,
        bool useStaThread = false,
        int maxOperationsPerSession = 0)
    {
        if (workerCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workerCount));
        }

        if (maxOperationsPerSession < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxOperationsPerSession));
        }

        WorkerCount = workerCount;
        Name = name;
        UseStaThread = useStaThread;
        MaxOperationsPerSession = maxOperationsPerSession;
    }

    public int WorkerCount { get; }

    public string? Name { get; }

    public bool UseStaThread { get; }

    public int MaxOperationsPerSession { get; }
}
