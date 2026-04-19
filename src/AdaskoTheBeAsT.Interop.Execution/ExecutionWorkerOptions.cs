namespace AdaskoTheBeAsT.Interop.Execution;

/// <summary>
/// Tuning surface for <see cref="ExecutionWorker{TSession}"/>. Designed to be
/// bindable via <c>IOptions&lt;ExecutionWorkerOptions&gt;</c> (parameterless
/// constructor plus public setters) while retaining a positional constructor
/// for plain <see langword="new"/> usage.
/// </summary>
public sealed class ExecutionWorkerOptions
{
    /// <summary>
    /// Initializes a new instance with default values. Intended for
    /// <c>Microsoft.Extensions.Options</c>-style configuration where properties
    /// are assigned after construction.
    /// </summary>
    public ExecutionWorkerOptions()
    {
    }

    // S3427 disabled: the positional-parameter constructor deliberately accepts
    // defaults for all arguments so existing callers keep working while the new
    // parameterless + init-only surface enables IOptions<T> binding. The two
    // overloads are disambiguated by C# overload resolution (the zero-argument
    // call site always binds to the parameterless ctor); analyser warning is
    // aesthetic only.
#pragma warning disable S3427

    /// <summary>
    /// Initializes a new instance with explicit values. All arguments are
    /// optional so existing call sites that only set a subset continue to bind.
    /// </summary>
    /// <param name="name">Optional display name used by telemetry and event payloads.</param>
    /// <param name="useStaThread">When <see langword="true"/> and the current
    /// OS is Windows, the dedicated worker thread is marked
    /// <see cref="System.Threading.ApartmentState.STA"/>.</param>
    /// <param name="maxOperationsPerSession">Session is recycled after this
    /// many operations. <c>0</c> (default) means unlimited.</param>
    /// <param name="disposeTimeout">Upper bound applied by the synchronous
    /// <see cref="ExecutionWorker{TSession}.Dispose"/>. Defaults to
    /// <see cref="Timeout.InfiniteTimeSpan"/>.</param>
    /// <param name="diagnostics">Optional scope into which the worker emits
    /// activities, counters, and queue-depth measurements. When
    /// <see langword="null"/> (default), <see cref="ExecutionDiagnostics.Shared"/>
    /// is used.</param>
    /// <exception cref="ArgumentOutOfRangeException">An argument violates the
    /// options invariants enforced by <see cref="Validate"/>.</exception>
    public ExecutionWorkerOptions(
        string? name = null,
        bool useStaThread = false,
        int maxOperationsPerSession = 0,
        TimeSpan? disposeTimeout = null,
        ExecutionDiagnostics? diagnostics = null)
#pragma warning restore S3427
    {
        Name = name;
        UseStaThread = useStaThread;
        MaxOperationsPerSession = maxOperationsPerSession;
        DisposeTimeout = disposeTimeout ?? Timeout.InfiniteTimeSpan;
        Diagnostics = diagnostics;
        Validate();
    }

    /// <summary>
    /// Gets a fresh <see cref="ExecutionWorkerOptions"/> instance populated with the default
    /// values for every setting. A new instance is returned on every access so that callers
    /// who mutate the result cannot poison subsequent fallback resolutions — historically
    /// this was a shared <c>static readonly</c> singleton, which meant any accidental
    /// mutation (e.g. <c>ExecutionWorkerOptions.Default.DisposeTimeout = TimeSpan.Zero</c>)
    /// leaked into every worker that fell through to the default.
    /// </summary>
    public static ExecutionWorkerOptions Default => new();

    /// <summary>Gets or sets the optional worker display name surfaced to telemetry and event payloads.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the dedicated worker thread is
    /// marked <see cref="System.Threading.ApartmentState.STA"/>. Honoured only
    /// on Windows; ignored on other platforms.
    /// </summary>
    public bool UseStaThread { get; set; }

    /// <summary>
    /// Gets or sets the number of operations after which the session is
    /// recycled. <c>0</c> means unlimited; must not be negative.
    /// </summary>
    public int MaxOperationsPerSession { get; set; }

    /// <summary>
    /// Gets or sets the bound applied by synchronous <c>Dispose</c>. Defaults
    /// to <see cref="Timeout.InfiniteTimeSpan"/>. Async <c>DisposeAsync</c>
    /// always waits for full drain.
    /// </summary>
    public TimeSpan DisposeTimeout { get; set; } = Timeout.InfiniteTimeSpan;

    /// <summary>
    /// Gets or sets the diagnostics scope this worker emits telemetry into.
    /// When <see langword="null"/> (the default), the worker uses
    /// <see cref="ExecutionDiagnostics.Shared"/>. Supply a custom scope to
    /// isolate one worker's activities, counters, and queue-depth
    /// measurements from every other worker in the process — typical uses are
    /// deterministic unit-test telemetry assertions and multi-tenant hosts
    /// that need one <see cref="System.Diagnostics.Metrics.Meter"/> per
    /// tenant.
    /// </summary>
    public ExecutionDiagnostics? Diagnostics { get; set; }

    // S3928 / MA0015 / S3236 disabled: Validate() validates the instance's
    // public properties (it has no parameters). The paramName argument is used
    // to surface the offending property name to callers, matching the
    // ArgumentOutOfRangeException convention applied by the positional ctor —
    // this keeps exception parity across both initialisation paths.
#pragma warning disable S3928, MA0015, S3236
    internal void Validate()
    {
        if (MaxOperationsPerSession < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxOperationsPerSession));
        }

        if (DisposeTimeout < TimeSpan.Zero && DisposeTimeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(DisposeTimeout));
        }
    }
#pragma warning restore S3928, MA0015, S3236
}
