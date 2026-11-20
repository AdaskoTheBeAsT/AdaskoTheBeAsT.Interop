using System.Text.Json.Serialization;
using AdaskoTheBeAsT.Interop.Execution;

namespace AdaskoTheBeAsT.Interop.Execution.AspNetCore;

/// <summary>
/// AOT-safe, reflection-free diagnostics DTO for a single
/// <see cref="IExecutionWorker{TSession}"/>. Exposes only <see cref="FaultType"/>
/// rather than a raw <see cref="Exception"/> so source-generated <c>System.Text.Json</c>
/// never encounters <c>MethodBase</c> (<c>Exception.TargetSite</c>) or leaks internal stack traces.
/// </summary>
public sealed class ExecutionWorkerDiagnosticsDto
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExecutionWorkerDiagnosticsDto"/> class.
    /// </summary>
    /// <param name="name">Optional worker display name.</param>
    /// <param name="queueDepth">Instantaneous queue depth at capture time.</param>
    /// <param name="isInitialized">Whether initial session creation completed.</param>
    /// <param name="isDisposed">Whether disposal has been requested.</param>
    /// <param name="isFaulted">Whether the worker is in a terminal faulted state.</param>
    /// <param name="faultType">Short CLR type name of the terminal fault, or <see langword="null"/> when healthy.</param>
    [JsonConstructor]
    public ExecutionWorkerDiagnosticsDto(
        string? name,
        int queueDepth,
        bool isInitialized,
        bool isDisposed,
        bool isFaulted,
        string? faultType)
    {
        Name = name;
        QueueDepth = queueDepth;
        IsInitialized = isInitialized;
        IsDisposed = isDisposed;
        IsFaulted = isFaulted;
        FaultType = faultType;
    }

    /// <summary>Gets the worker display name, or <see langword="null"/> when unnamed.</summary>
    public string? Name { get; }

    /// <summary>Gets the instantaneous queue depth captured with this snapshot.</summary>
    public int QueueDepth { get; }

    /// <summary>Gets a value indicating whether initial session startup completed.</summary>
    public bool IsInitialized { get; }

    /// <summary>Gets a value indicating whether disposal had been requested.</summary>
    public bool IsDisposed { get; }

    /// <summary>Gets a value indicating whether the worker had faulted.</summary>
    public bool IsFaulted { get; }

    /// <summary>
    /// Gets the short CLR type name (<c>Exception.GetType().Name</c>) of the terminal fault,
    /// or <see langword="null"/> when healthy.
    /// </summary>
    public string? FaultType { get; }

    /// <summary>
    /// Projects an <see cref="ExecutionWorkerSnapshot"/> into a leak-free
    /// <see cref="ExecutionWorkerDiagnosticsDto"/>.
    /// </summary>
    /// <param name="snapshot">The point-in-time worker snapshot.</param>
    /// <returns>An immutable diagnostics DTO safe for source-generated JSON serialization.</returns>
    public static ExecutionWorkerDiagnosticsDto FromSnapshot(ExecutionWorkerSnapshot snapshot) =>
        new(
            snapshot.Name,
            snapshot.QueueDepth,
            snapshot.IsInitialized,
            snapshot.IsDisposed,
            snapshot.IsFaulted,
            snapshot.Fault?.GetType().Name);
}
