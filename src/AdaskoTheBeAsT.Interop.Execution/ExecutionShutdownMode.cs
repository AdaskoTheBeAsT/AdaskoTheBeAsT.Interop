namespace AdaskoTheBeAsT.Interop.Execution;

/// <summary>Controls what happens to admitted, not-yet-running requests on shutdown.</summary>
public enum ExecutionShutdownMode
{
    /// <summary>Execute admitted requests before releasing the session.</summary>
    Drain,

    /// <summary>Cancel queued requests without interrupting the running delegate.</summary>
    CancelPending,
}
