namespace AdaskoTheBeAsT.Interop.Execution;

/// <summary>
/// Built-in <see cref="IWorkerScheduler{TSession}"/> that dispatches submissions
/// in strict round-robin order across the pool. Faulted workers are skipped
/// while at least one healthy worker remains.
/// </summary>
/// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
public sealed class RoundRobinWorkerScheduler<TSession> : IWorkerScheduler<TSession>
    where TSession : class
{
    private int _nextIndex = -1;

    /// <inheritdoc />
    public IExecutionWorker<TSession> SelectWorker(IReadOnlyList<IExecutionWorker<TSession>> workers)
    {
        if (workers is null)
        {
            throw new ArgumentNullException(nameof(workers));
        }

        if (workers.Count == 0)
        {
            throw new ArgumentException("The workers snapshot must contain at least one worker.", nameof(workers));
        }

        if (workers.Count == 1)
        {
            return workers[0];
        }

        var start = NextRollingIndex(workers.Count);
        for (var offset = 0; offset < workers.Count; offset++)
        {
            var candidate = (start + offset) % workers.Count;
            if (!workers[candidate].IsFaulted)
            {
                return workers[candidate];
            }
        }

        // Every worker is faulted: return the round-robin candidate so the caller
        // gets a deterministic ObjectDisposedException from ExecuteAsync rather
        // than silent blocking.
        return workers[start];
    }

    private int NextRollingIndex(int workerCount)
    {
        return unchecked(Interlocked.Increment(ref _nextIndex) & int.MaxValue) % workerCount;
    }
}
