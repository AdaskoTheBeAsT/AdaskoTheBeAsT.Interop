namespace AdaskoTheBeAsT.Interop.Execution;

public sealed class ExecutionRequestOptions
{
    public ExecutionRequestOptions(bool recycleSessionOnFailure = false)
    {
        RecycleSessionOnFailure = recycleSessionOnFailure;
    }

    public static ExecutionRequestOptions Default { get; } = new();

    public bool RecycleSessionOnFailure { get; }
}
