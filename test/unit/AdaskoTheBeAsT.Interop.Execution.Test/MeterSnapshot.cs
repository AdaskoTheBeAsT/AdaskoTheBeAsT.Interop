using System.Diagnostics.Metrics;

namespace AdaskoTheBeAsT.Interop.Execution.Test;

internal sealed class MeterSnapshot : IDisposable
{
    /*
     * Intentionally NOT a ConcurrentBag<T>:
     * ConcurrentBag is thread-local-stack-backed, so its enumeration order is NOT insertion order —
     * it is whatever order the thread-local slots happen to be walked in. That makes `Last()`
     * non-deterministic under parallel producers and hides flaky telemetry assertions.
     * We instead append under a lock to a plain List<T>; the list is iterated in strict
     * insertion order, which is exactly the "last observed measurement" contract callers expect.
     * The listener callbacks are not on the hot path of the library under test (tests typically
     * produce tens of measurements, not millions), so a coarse lock is cheaper than maintaining a
     * per-(instrument,tag-key) dictionary.
     */
#if NET9_0_OR_GREATER
    private readonly Lock _syncRoot = new();
#else
    private readonly object _syncRoot = new();
#endif
    private readonly List<RecordedMeasurement> _measurements = [];
    private readonly MeterListener _listener;

    public MeterSnapshot(string meterName)
    {
        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (string.Equals(instrument.Meter.Name, meterName, StringComparison.Ordinal))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };

        _listener.SetMeasurementEventCallback<long>(OnLongMeasurement);
        _listener.SetMeasurementEventCallback<int>(OnIntMeasurement);
        _listener.Start();
    }

    public void Collect()
    {
        _listener.RecordObservableInstruments();
    }

    public long Sum(string instrumentName, params (string Key, string Value)[] requiredTags)
    {
        long total = 0;
        lock (_syncRoot)
        {
            foreach (var measurement in _measurements)
            {
                if (!string.Equals(measurement.InstrumentName, instrumentName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!MatchesTags(measurement, requiredTags))
                {
                    continue;
                }

                total += measurement.Value;
            }
        }

        return total;
    }

    public long Last(string instrumentName, params (string Key, string Value)[] requiredTags)
    {
        long? lastValue = null;
        lock (_syncRoot)
        {
            for (var i = _measurements.Count - 1; i >= 0; i--)
            {
                var measurement = _measurements[i];
                if (!string.Equals(measurement.InstrumentName, instrumentName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!MatchesTags(measurement, requiredTags))
                {
                    continue;
                }

                lastValue = measurement.Value;
                break;
            }
        }

        return lastValue ?? 0;
    }

    public void Dispose()
    {
        _listener.Dispose();
    }

    private static bool MatchesTags(RecordedMeasurement measurement, (string Key, string Value)[] requiredTags)
    {
        foreach (var (requiredKey, requiredValue) in requiredTags)
        {
            var found = false;
            foreach (var tag in measurement.Tags)
            {
                if (string.Equals(tag.Key, requiredKey, StringComparison.Ordinal)
                    && string.Equals(tag.Value?.ToString(), requiredValue, StringComparison.Ordinal))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return false;
            }
        }

        return true;
    }

    private void OnLongMeasurement(
        Instrument instrument,
        long measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
    {
        var recorded = new RecordedMeasurement(instrument.Name, measurement, tags.ToArray());
        lock (_syncRoot)
        {
            _measurements.Add(recorded);
        }
    }

    private void OnIntMeasurement(
        Instrument instrument,
        int measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
    {
        var recorded = new RecordedMeasurement(instrument.Name, measurement, tags.ToArray());
        lock (_syncRoot)
        {
            _measurements.Add(recorded);
        }
    }

    private sealed class RecordedMeasurement(string instrumentName, long value, KeyValuePair<string, object?>[] tags)
    {
        public string InstrumentName { get; } = instrumentName;

        public long Value { get; } = value;

        public KeyValuePair<string, object?>[] Tags { get; } = tags;
    }
}
