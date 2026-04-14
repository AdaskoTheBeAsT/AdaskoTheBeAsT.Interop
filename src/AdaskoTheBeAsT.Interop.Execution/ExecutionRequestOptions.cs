namespace AdaskoTheBeAsT.Interop.Execution;

public sealed class ExecutionRequestOptions(bool recycleSessionOnFailure = false)
{
    public static ExecutionRequestOptions Default { get; } = new();

    public bool RecycleSessionOnFailure { get; } = recycleSessionOnFailure;
}
