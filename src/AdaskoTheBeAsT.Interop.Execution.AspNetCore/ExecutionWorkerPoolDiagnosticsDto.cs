using System.Text.Json.Serialization;
using AdaskoTheBeAsT.Interop.Execution;

namespace AdaskoTheBeAsT.Interop.Execution.AspNetCore;

/// <summary>
/// AOT-safe, reflection-free diagnostics DTO for an
/// <see cref="IExecutionWorkerPool{TSession}"/>. Preserves aggregate JSON property
/// names (<c>workerCount</c>, <c>queueDepth</c>, <c>isAnyFaulted</c>, etc.) while
/// projecting per-worker faults safely to <see cref="ExecutionWorkerDiagnosticsDto.FaultType"/>.
/// </summary>
/// <param name="name">Optional pool display name.</param>
/// <param name="workerCount">Total number of workers in the pool.</param>
/// <param name="queueDepth">Aggregate queue depth across all workers.</param>
/// <param name="initializedWorkerCount">Number of workers that finished eager initialization.</param>
/// <param name="faultedWorkerCount">Number of workers in a terminal faulted state.</param>
/// <param name="isAnyFaulted">Whether at least one worker in the pool is faulted.</param>
/// <param name="isInitialized">Whether every worker in the pool finished eager initialization.</param>
/// <param name="isDisposed">Whether disposal has been requested on the pool or any worker.</param>
/// <param name="workers">Index-aligned per-worker diagnostics DTOs.</param>
#pragma warning disable S107 // Preserve the public constructor and bind each flat JSON property on deserialization.
[method: JsonConstructor]
public sealed class ExecutionWorkerPoolDiagnosticsDto(
    string? name,
    int workerCount,
    int queueDepth,
    int initializedWorkerCount,
    int faultedWorkerCount,
    bool isAnyFaulted,
    bool isInitialized,
    bool isDisposed,
    IReadOnlyList<ExecutionWorkerDiagnosticsDto>? workers)
#pragma warning restore S107
{
    private static readonly IReadOnlyList<ExecutionWorkerDiagnosticsDto> EmptyWorkers = [];

    /// <summary>Gets the pool display name, or <see langword="null"/> when unnamed.</summary>
    public string? Name { get; } = name;

    /// <summary>Gets the number of workers owned by the pool.</summary>
    public int WorkerCount { get; } = workerCount;

    /// <summary>Gets the aggregate queue depth across all workers.</summary>
    public int QueueDepth { get; } = queueDepth;

    /// <summary>Gets the number of workers whose initial session startup completed.</summary>
    public int InitializedWorkerCount { get; } = initializedWorkerCount;

    /// <summary>Gets the number of workers in a terminal faulted state.</summary>
    public int FaultedWorkerCount { get; } = faultedWorkerCount;

    /// <summary>Gets a value indicating whether any worker in the pool is faulted.</summary>
    public bool IsAnyFaulted { get; } = isAnyFaulted;

    /// <summary>Gets a value indicating whether all workers in the pool finished startup.</summary>
    public bool IsInitialized { get; } = isInitialized;

    /// <summary>Gets a value indicating whether disposal has been requested.</summary>
    public bool IsDisposed { get; } = isDisposed;

    /// <summary>Gets index-aligned per-worker diagnostics DTOs.</summary>
    public IReadOnlyList<ExecutionWorkerDiagnosticsDto> Workers { get; } = workers ?? EmptyWorkers;

    /// <summary>
    /// Projects an <see cref="ExecutionWorkerPoolSnapshot"/> into a leak-free
    /// <see cref="ExecutionWorkerPoolDiagnosticsDto"/>.
    /// </summary>
    /// <param name="snapshot">The point-in-time pool snapshot.</param>
    /// <returns>An immutable diagnostics DTO safe for source-generated JSON serialization.</returns>
    public static ExecutionWorkerPoolDiagnosticsDto FromSnapshot(ExecutionWorkerPoolSnapshot snapshot)
    {
        var sourceWorkers = snapshot.Workers;
        IReadOnlyList<ExecutionWorkerDiagnosticsDto> workers;
        if (sourceWorkers is null || sourceWorkers.Count == 0)
        {
            workers = EmptyWorkers;
        }
        else
        {
            var projected = new ExecutionWorkerDiagnosticsDto[sourceWorkers.Count];
            for (var index = 0; index < sourceWorkers.Count; index++)
            {
                projected[index] = ExecutionWorkerDiagnosticsDto.FromSnapshot(sourceWorkers[index]);
            }

            workers = projected;
        }

        return new ExecutionWorkerPoolDiagnosticsDto(
            snapshot.Name,
            snapshot.WorkerCount,
            snapshot.QueueDepth,
            snapshot.InitializedWorkerCount,
            snapshot.FaultedWorkerCount,
            snapshot.IsAnyFaulted,
            snapshot.IsInitialized,
            snapshot.IsDisposed,
            workers);
    }
}
