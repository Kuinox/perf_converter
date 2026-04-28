using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace PerfConverter;

static class PerfConverterMetrics
{
    static readonly Meter Meter = new("PerfConverter");
    static readonly ConcurrentDictionary<string, FileTelemetryState> Files = new(StringComparer.Ordinal);

    static long _processedEvents;

    static readonly ObservableCounter<long> ProcessedEventsCounter =
        Meter.CreateObservableCounter(
            name: "perfconverter.events.processed",
            observeValue: () => Interlocked.Read(ref _processedEvents),
            unit: "{event}",
            description: "Total perf events processed by PerfConverter.");

    static readonly ObservableCounter<long> FlushedEntriesCounter =
        Meter.CreateObservableCounter(
            name: "perfconverter.file.entries.flushed",
            observeValues: ObserveFlushedEntries,
            unit: "{entry}",
            description: "Total entries flushed per output file.");

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

    public static void FileFlushed(string key, int flushedCount)
    {
        if (flushedCount <= 0)
            return;

        var state = Files.GetOrAdd(key, static _ => new FileTelemetryState());
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
        public long FlushedEntries;
        public FileTelemetryStatus Status = FileTelemetryStatus.Buffering;
    }

    enum FileTelemetryStatus
    {
        Buffering = 1,
        Closed = 2
    }
}
