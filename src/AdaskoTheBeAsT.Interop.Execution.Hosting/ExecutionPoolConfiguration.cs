using AdaskoTheBeAsT.Interop.Execution;

namespace AdaskoTheBeAsT.Interop.Execution.Hosting;

/// <summary>Validates and applies the common settings for a hosted execution pool.</summary>
public static class ExecutionPoolConfiguration
{
    /// <summary>Largest whole-second execution deadline supported by the timer.</summary>
    public const int MaximumTimeoutSeconds = int.MaxValue / 1000;

    /// <summary>Checks pool settings and the caller's worker limit.</summary>
    /// <param name="workerCount">Configured number of workers.</param>
    /// <param name="queueCapacity">Configured per-worker queue capacity.</param>
    /// <param name="timeoutSeconds">Positive execution deadline in seconds.</param>
    /// <param name="maximumWorkers">Maximum workers supported by the caller.</param>
    /// <param name="maxOperationsPerSession">Recycle threshold, or zero to disable it.</param>
    /// <returns>Whether all values are in range.</returns>
    public static bool IsValid(
        int workerCount,
        int queueCapacity,
        int timeoutSeconds,
        int maximumWorkers = int.MaxValue,
        int maxOperationsPerSession = 0) =>
        workerCount >= 1
        && workerCount <= maximumWorkers
        && queueCapacity > 0
        && timeoutSeconds is > 0 and <= MaximumTimeoutSeconds
        && maxOperationsPerSession >= 0;

    /// <summary>Applies common settings without replacing caller-specific hooks or stack size.</summary>
    /// <param name="options">The pool options being configured.</param>
    /// <param name="name">The pool's diagnostic name.</param>
    /// <param name="workerCount">Configured number of workers.</param>
    /// <param name="queueCapacity">Configured per-worker queue capacity.</param>
    /// <param name="maxOperationsPerSession">Recycle threshold, or zero to disable it.</param>
    public static void ConfigureStaPool(
        ExecutionWorkerPoolOptions options,
        string name,
        int workerCount,
        int queueCapacity,
        int maxOperationsPerSession = 0)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        options.Name = name;
        options.WorkerCount = workerCount;
        options.QueueCapacity = queueCapacity;
        options.MaxOperationsPerSession = maxOperationsPerSession;
        options.UseStaThread = true;
        options.DisposeTimeout = TimeSpan.FromSeconds(10);
        options.SchedulingStrategy = SchedulingStrategy.LeastQueued;
        options.ShutdownMode = ExecutionShutdownMode.Drain;
    }
}
