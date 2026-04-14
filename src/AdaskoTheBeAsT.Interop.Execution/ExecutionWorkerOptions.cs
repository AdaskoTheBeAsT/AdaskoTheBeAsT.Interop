namespace AdaskoTheBeAsT.Interop.Execution;

public sealed class ExecutionWorkerOptions
{
    public ExecutionWorkerOptions(
        string? name = null,
        bool useStaThread = false,
        int maxOperationsPerSession = 0)
    {
        if (maxOperationsPerSession < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxOperationsPerSession));
        }

        Name = name;
        UseStaThread = useStaThread;
        MaxOperationsPerSession = maxOperationsPerSession;
    }

    public static ExecutionWorkerOptions Default { get; } = new();

    public string? Name { get; }

    public bool UseStaThread { get; }

    public int MaxOperationsPerSession { get; }
}
