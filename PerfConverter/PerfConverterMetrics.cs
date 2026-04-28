using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PerfConverter;

static class PerfConverterMetrics
{
    static readonly Meter Meter = new("PerfConverter");
    static readonly ConcurrentDictionary<string, FileTelemetryState> Files = new(StringComparer.Ordinal);
    static readonly Lock RateLock = new();

    static long _processedEvents;
    static long _lastObservedProcessedEvents;
    static long _lastObservedTimestamp = Stopwatch.GetTimestamp();

    static readonly ObservableGauge<long> ProcessedEventsGauge =
        Meter.CreateObservableGauge(
            name: "perfconverter.events.total",
            observeValue: () => Interlocked.Read(ref _processedEvents),
            unit: "{event}",
            description: "Total perf events processed by PerfConverter.");

    static readonly ObservableGauge<double> CurrentRateGauge =
        Meter.CreateObservableGauge(
            name: "perfconverter.events.current_rate",
            observeValue: ObserveCurrentRate,
            unit: "{event}/s",
            description: "Current observed event processing rate.");

    static readonly ObservableGauge<long> FlushedEntriesGauge =
        Meter.CreateObservableGauge(
            name: "perfconverter.file.entries.flushed",
            observeValues: ObserveFlushedEntries,
            unit: "{entry}",
            description: "Total entries flushed per output file.");

    static readonly ObservableGauge<long> BufferedEntriesGauge =
        Meter.CreateObservableGauge(
            name: "perfconverter.file.entries.buffered",
            observeValues: ObserveBufferedEntries,
            unit: "{entry}",
            description: "Current buffered entries per output file.");

    static readonly ObservableGauge<int> FileStatusGauge =
        Meter.CreateObservableGauge(
            name: "perfconverter.file.status",
            observeValues: ObserveFileStatuses,
            unit: "{state}",
            description: "Current output file status. 1=buffering, 2=closed.");

    public static void IncrementProcessedEvents()
    {
        Interlocked.Increment(ref _processedEvents);
    }

    public static void FileOpened(string key)
    {
        Files.AddOrUpdate(
            key,
            static _ => new FileTelemetryState(),
            static (_, existing) =>
            {
                existing.Status = FileTelemetryStatus.Buffering;
                return existing;
            });
    }

    public static void FileBuffered(string key, int entryCount)
    {
        if (entryCount <= 0)
            return;

        var state = Files.GetOrAdd(key, static _ => new FileTelemetryState());
        Interlocked.Add(ref state.BufferedEntries, entryCount);
        state.Status = FileTelemetryStatus.Buffering;
    }

    public static void FileFlushed(string key, int flushedCount)
    {
        if (flushedCount <= 0)
            return;

        var state = Files.GetOrAdd(key, static _ => new FileTelemetryState());
        Interlocked.Add(ref state.BufferedEntries, -flushedCount);
        Interlocked.Add(ref state.FlushedEntries, flushedCount);
        state.Status = FileTelemetryStatus.Buffering;
    }

    public static void FileClosed(string key)
    {
        var state = Files.GetOrAdd(key, static _ => new FileTelemetryState());
        state.Status = FileTelemetryStatus.Closed;
    }

    static IEnumerable<Measurement<long>> ObserveFlushedEntries()
    {
        foreach (var (key, state) in Files)
        {
            yield return new Measurement<long>(
                Interlocked.Read(ref state.FlushedEntries),
                new KeyValuePair<string, object?>("file", key));
        }
    }

    static IEnumerable<Measurement<long>> ObserveBufferedEntries()
    {
        foreach (var (key, state) in Files)
        {
            yield return new Measurement<long>(
                Math.Max(0, Interlocked.Read(ref state.BufferedEntries)),
                new KeyValuePair<string, object?>("file", key));
        }
    }

    static IEnumerable<Measurement<int>> ObserveFileStatuses()
    {
        foreach (var (key, state) in Files)
        {
            yield return new Measurement<int>(
                (int)state.Status,
                new KeyValuePair<string, object?>("file", key));
        }
    }

    sealed class FileTelemetryState
    {
        public long BufferedEntries;
        public long FlushedEntries;
        public FileTelemetryStatus Status = FileTelemetryStatus.Buffering;
    }

    enum FileTelemetryStatus
    {
        Buffering = 1,
        Closed = 2
    }

    static double ObserveCurrentRate()
    {
        lock (RateLock)
        {
            var nowTimestamp = Stopwatch.GetTimestamp();
            var currentProcessedEvents = Interlocked.Read(ref _processedEvents);
            var elapsedSeconds = (nowTimestamp - _lastObservedTimestamp) / (double)Stopwatch.Frequency;

            if (elapsedSeconds <= 0)
                return 0;

            var deltaEvents = currentProcessedEvents - _lastObservedProcessedEvents;
            _lastObservedProcessedEvents = currentProcessedEvents;
            _lastObservedTimestamp = nowTimestamp;

            return deltaEvents / elapsedSeconds;
        }
    }
}
