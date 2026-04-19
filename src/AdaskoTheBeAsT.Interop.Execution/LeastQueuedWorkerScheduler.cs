namespace AdaskoTheBeAsT.Interop.Execution;

/// <summary>
/// Built-in <see cref="IWorkerScheduler{TSession}"/> that dispatches each
/// submission to the worker whose current
/// <see cref="IExecutionWorker{TSession}.QueueDepth"/> is lowest. Ties are
/// broken using a shared rolling index so equal-depth workers are still
/// selected in round-robin order instead of piling on worker 0. Faulted
/// workers are skipped while at least one healthy worker remains.
/// </summary>
/// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
public sealed class LeastQueuedWorkerScheduler<TSession> : IWorkerScheduler<TSession>
    where TSession : class
{
    private int _nextIndex = -1;

    /// <inheritdoc />
    public IExecutionWorker<TSession> SelectWorker(IReadOnlyList<IExecutionWorker<TSession>> workers)
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(workers);
#else
        if (workers is null)
        {
            throw new ArgumentNullException(nameof(workers));
        }
#endif

        if (workers.Count == 0)
        {
            throw new ArgumentException("The workers snapshot must contain at least one worker.", nameof(workers));
        }

        if (workers.Count == 1)
        {
            return workers[0];
        }

        // Start the scan from the rolling index so equal-depth workers fall back to
        // round-robin order rather than always routing to worker 0.
        var start = NextRollingIndex(workers.Count);
        var bestIndex = -1;
        var bestDepth = int.MaxValue;
        for (var offset = 0; offset < workers.Count; offset++)
        {
            var candidate = (start + offset) % workers.Count;
            var worker = workers[candidate];
            if (worker.IsFaulted)
            {
                continue;
            }

            var depth = worker.QueueDepth;
            if (depth < bestDepth)
            {
                bestDepth = depth;
                bestIndex = candidate;
                if (bestDepth == 0)
                {
                    // Cannot beat a depth of zero — early exit to halve the average
                    // scan cost on lightly loaded pools.
                    break;
                }
            }
        }

        return bestIndex >= 0 ? workers[bestIndex] : workers[start];
    }

    private int NextRollingIndex(int workerCount)
    {
        return unchecked(Interlocked.Increment(ref _nextIndex) & int.MaxValue) % workerCount;
    }
}
