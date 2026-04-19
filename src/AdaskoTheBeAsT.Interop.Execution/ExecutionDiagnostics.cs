using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace AdaskoTheBeAsT.Interop.Execution;

/// <summary>
/// Owns the <see cref="ActivitySource"/>, <see cref="Meter"/>, counters, and
/// observable gauge used by <see cref="ExecutionWorker{TSession}"/> and
/// <see cref="ExecutionWorkerPool{TSession}"/>. Exposes them as a scoped
/// instance so callers can opt out of the process-wide
/// <see cref="Shared"/> singleton — for example in unit tests that want
/// MeterListener isolation, or in multi-tenant hosts that want to route
/// different worker cohorts to different telemetry pipelines.
/// </summary>
/// <remarks>
/// <para>
/// The default singleton <see cref="Shared"/> is lazily created the first
/// time it is observed and uses <see cref="ExecutionDiagnosticNames.SourceName"/>
/// so OpenTelemetry pipelines and third-party <c>ActivityListener</c>s /
/// <c>MeterListener</c>s keep subscribing by the stable public name.
/// </para>
/// <para>
/// A custom scope is constructed with a user-chosen source name. Workers
/// registered to a custom scope do not contribute measurements to
/// <see cref="Shared"/> — this is the mechanism that gives unit tests a
/// deterministic, pollution-free view of their own worker's telemetry when
/// xUnit runs test classes in parallel.
/// </para>
/// </remarks>
public sealed class ExecutionDiagnostics : IDisposable
{
    private static readonly Lazy<ExecutionDiagnostics> LazyShared =
        new(static () => new ExecutionDiagnostics(ExecutionDiagnosticNames.SourceName));

    private readonly ActivitySource _activitySource;
    private readonly Meter _meter;
    private readonly Counter<long> _operationsCounter;
    private readonly Counter<long> _sessionRecyclesCounter;
    private readonly ConcurrentDictionary<ExecutionWorkerRegistration, byte> _workers = new();

#pragma warning disable IDE0052, S1144, S4487
    // QueueDepthGauge is an ObservableGauge registered on the instance Meter.
    // The field keeps the instrument rooted for the lifetime of this scope so
    // the meter keeps reporting. Analyzers flag it as unread because the gauge
    // only interacts with consumers via the Meter pipeline, never via this
    // field.
    private readonly ObservableGauge<int> _queueDepthGauge;
#pragma warning restore IDE0052, S1144, S4487

    private int _disposed;

    /// <summary>
    /// Initializes a new diagnostics scope with its own
    /// <see cref="ActivitySource"/> + <see cref="Meter"/> pair identified by
    /// <paramref name="sourceName"/>.
    /// </summary>
    /// <param name="sourceName">The <see cref="ActivitySource.Name"/> and
    /// <see cref="Meter.Name"/> assigned to this scope. Must not be
    /// <see langword="null"/> or whitespace. Use
    /// <see cref="ExecutionDiagnosticNames.SourceName"/> for the default
    /// telemetry pipeline or a per-test / per-tenant name for isolation.</param>
    /// <exception cref="ArgumentException"><paramref name="sourceName"/> is
    /// <see langword="null"/>, empty, or whitespace.</exception>
    public ExecutionDiagnostics(string sourceName)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            throw new ArgumentException(
                "Source name must not be null or whitespace.",
                nameof(sourceName));
        }

        SourceName = sourceName;
        var version = GetDiagnosticAssemblyVersion();

        _activitySource = new ActivitySource(sourceName, version);
        _meter = new Meter(sourceName, version);

        _operationsCounter = _meter.CreateCounter<long>(
            name: ExecutionDiagnosticNames.MetricOperations,
            unit: "{operation}",
            description: "Number of work items processed by execution workers, tagged by outcome.");

        _sessionRecyclesCounter = _meter.CreateCounter<long>(
            name: ExecutionDiagnosticNames.MetricSessionRecycles,
            unit: "{recycle}",
            description: "Number of session recycles triggered by execution workers, tagged by reason.");

        _queueDepthGauge = _meter.CreateObservableGauge<int>(
            name: ExecutionDiagnosticNames.MetricQueueDepth,
            observeValues: ObserveQueueDepths,
            unit: "{item}",
            description: "Instantaneous queue depth per execution worker, tagged by worker name.");
    }

    /// <summary>
    /// Gets the process-wide default diagnostics scope. Lazily created on
    /// first access and named
    /// <see cref="ExecutionDiagnosticNames.SourceName"/> so the stable public
    /// telemetry contract continues to identify it.
    /// </summary>
    public static ExecutionDiagnostics Shared => LazyShared.Value;

    /// <summary>Gets the source name assigned to this scope.</summary>
    public string SourceName { get; }

    internal ActivitySource ActivitySource => _activitySource;

    internal Counter<long> OperationsCounter => _operationsCounter;

    internal Counter<long> SessionRecyclesCounter => _sessionRecyclesCounter;

    /// <summary>
    /// Disposes the underlying <see cref="ActivitySource"/> and
    /// <see cref="Meter"/>. Calling <see cref="Dispose"/> on
    /// <see cref="Shared"/> is a no-op — the shared scope has process lifetime
    /// by design.
    /// </summary>
    public void Dispose()
    {
        if (LazyShared.IsValueCreated && ReferenceEquals(this, LazyShared.Value))
        {
            return;
        }

        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _activitySource.Dispose();
        _meter.Dispose();
    }

    internal void RegisterWorker(ExecutionWorkerRegistration registration)
    {
        if (registration is null)
        {
            throw new ArgumentNullException(nameof(registration));
        }

        _workers.TryAdd(registration, 0);
    }

    internal void UnregisterWorker(ExecutionWorkerRegistration registration)
    {
        if (registration is null)
        {
            return;
        }

        _workers.TryRemove(registration, out _);
    }

    private static string GetDiagnosticAssemblyVersion()
    {
        return typeof(ExecutionDiagnostics).GetTypeInfo().Assembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    private IEnumerable<Measurement<int>> ObserveQueueDepths()
    {
        foreach (var worker in _workers.Keys)
        {
            yield return new Measurement<int>(
                worker.GetQueueDepth(),
                new KeyValuePair<string, object?>(ExecutionDiagnosticNames.TagWorkerName, worker.Name));
        }
    }
}
