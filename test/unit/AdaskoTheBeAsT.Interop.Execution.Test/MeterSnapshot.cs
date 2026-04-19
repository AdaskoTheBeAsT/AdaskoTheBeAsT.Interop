using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace AdaskoTheBeAsT.Interop.Execution.Test;

internal sealed class MeterSnapshot : IDisposable
{
    private readonly MeterListener _listener;
    private readonly ConcurrentBag<RecordedMeasurement> _measurements = new();

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

        return total;
    }

    public long Last(string instrumentName, params (string Key, string Value)[] requiredTags)
    {
        long? lastValue = null;
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

            lastValue = measurement.Value;
        }

        return lastValue ?? 0;
    }

    public void Dispose()
    {
        _listener.Dispose();
    }

    private static bool MatchesTags(RecordedMeasurement measurement, (string Key, string Value)[] requiredTags)
    {
        foreach (var required in requiredTags)
        {
            var found = false;
            foreach (var tag in measurement.Tags)
            {
                if (string.Equals(tag.Key, required.Key, StringComparison.Ordinal)
                    && string.Equals(tag.Value?.ToString(), required.Value, StringComparison.Ordinal))
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
        _measurements.Add(new RecordedMeasurement(instrument.Name, measurement, tags.ToArray()));
    }

    private void OnIntMeasurement(
        Instrument instrument,
        int measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
    {
        _measurements.Add(new RecordedMeasurement(instrument.Name, measurement, tags.ToArray()));
    }

    private sealed class RecordedMeasurement
    {
        public RecordedMeasurement(string instrumentName, long value, KeyValuePair<string, object?>[] tags)
        {
            InstrumentName = instrumentName;
            Value = value;
            Tags = tags;
        }

        public string InstrumentName { get; }

        public long Value { get; }

        public KeyValuePair<string, object?>[] Tags { get; }
    }
}
