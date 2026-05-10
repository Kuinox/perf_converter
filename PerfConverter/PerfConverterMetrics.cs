using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PerfConverter;

static class PerfConverterMetrics
{
    static readonly Meter Meter = new("PerfConverter");
    static readonly ConcurrentDictionary<string, FileTelemetryState> Files = new(StringComparer.Ordinal);
    static readonly ConcurrentDictionary<string, PipelineTelemetryState> PipelineStages = new(StringComparer.Ordinal);
    static readonly ConcurrentDictionary<string, PipelineQueueTelemetryState> PipelineQueues = new(StringComparer.Ordinal);
    static readonly Lock RateLock = new();
    static readonly Lock TraceTimeLock = new();

    static long _processedEvents;
    static long _lastObservedProcessedEvents;
    static long _lastObservedTimestamp = Stopwatch.GetTimestamp();
    static long _firstTraceTimestampNs = long.MaxValue;
    static long _lastTraceTimestampNs = long.MinValue;

    static readonly ObservableGauge<long> ProcessedEventsGauge =
        Meter.CreateObservableGauge(
            name: "perfconverter.events.total",
            observeValue: () => Interlocked.Read(ref _processedEvents),
            unit: "{event}",
            description: "Total perf events processed by PerfConverter.");

    static readonly ObservableGauge<double> CurrentRateGauge =
        Meter.CreateObservableGauge(
            name: "perfconverter.events.current_rate",
            observeValue: () => GetSnapshot().CurrentRate,
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

    static readonly ObservableGauge<double> PipelineStageElapsedMsGauge =
        Meter.CreateObservableGauge(
            name: "perfconverter.pipeline.stage.elapsed_ms",
            observeValues: ObservePipelineStageElapsedMs,
            unit: "ms",
            description: "Total elapsed milliseconds spent in each pipeline stage.");

    static readonly ObservableGauge<int> PipelineQueueDepthGauge =
        Meter.CreateObservableGauge(
            name: "perfconverter.pipeline.queue.depth",
            observeValues: ObservePipelineQueueDepth,
            unit: "{item}",
            description: "Current item depth of each pipeline queue.");

    public static void IncrementProcessedEvents(ulong traceTimestampNs)
    {
        Interlocked.Increment(ref _processedEvents);
        ObserveTraceTimestamp(traceTimestampNs);
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

    public static void PipelineStageElapsed(string stage, long elapsedTimestampTicks)
    {
        if (elapsedTimestampTicks <= 0)
            return;

        var state = PipelineStages.GetOrAdd(stage, static _ => new PipelineTelemetryState());
        Interlocked.Increment(ref state.Count);
        Interlocked.Add(ref state.ElapsedTimestampTicks, elapsedTimestampTicks);
    }

    public static void PipelineQueueDepth(string queue, int depth, int capacity)
    {
        var state = PipelineQueues.GetOrAdd(queue, static _ => new PipelineQueueTelemetryState());
        Volatile.Write(ref state.Depth, Math.Max(0, depth));
        Volatile.Write(ref state.Capacity, Math.Max(0, capacity));
    }

    public static MetricsSnapshot GetSnapshot()
    {
        var currentRate = ObserveCurrentRate();
        var (firstTraceTimestampNs, lastTraceTimestampNs) = GetTraceTimeRange();
        var files = new FileMetricsSnapshot[Files.Count];
        var index = 0;

        foreach (var (key, state) in Files)
        {
            files[index++] = new FileMetricsSnapshot(
                FileName: key,
                BufferedEntries: Math.Max(0, Interlocked.Read(ref state.BufferedEntries)),
                FlushedEntries: Interlocked.Read(ref state.FlushedEntries),
                Status: state.Status.ToString().ToUpperInvariant());
        }

        if (index != files.Length)
        {
            Array.Resize(ref files, index);
        }

        var pipelineStages = new PipelineStageMetricsSnapshot[PipelineStages.Count];
        index = 0;
        foreach (var (stage, state) in PipelineStages)
        {
            pipelineStages[index++] = new PipelineStageMetricsSnapshot(
                Stage: stage,
                Count: Interlocked.Read(ref state.Count),
                ElapsedMs: StopwatchTicksToMilliseconds(Interlocked.Read(ref state.ElapsedTimestampTicks)));
        }

        if (index != pipelineStages.Length)
        {
            Array.Resize(ref pipelineStages, index);
        }

        var pipelineQueues = new PipelineQueueMetricsSnapshot[PipelineQueues.Count];
        index = 0;
        foreach (var (queue, state) in PipelineQueues)
        {
            pipelineQueues[index++] = new PipelineQueueMetricsSnapshot(
                Queue: queue,
                Depth: Volatile.Read(ref state.Depth),
                Capacity: Volatile.Read(ref state.Capacity));
        }

        if (index != pipelineQueues.Length)
        {
            Array.Resize(ref pipelineQueues, index);
        }

        return new MetricsSnapshot(
            TotalEvents: Interlocked.Read(ref _processedEvents),
            CurrentRate: currentRate,
            FirstTraceTimestampNs: firstTraceTimestampNs,
            LastTraceTimestampNs: lastTraceTimestampNs,
            Files: files,
            PipelineStages: pipelineStages,
            PipelineQueues: pipelineQueues);
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

    static IEnumerable<Measurement<double>> ObservePipelineStageElapsedMs()
    {
        foreach (var (stage, state) in PipelineStages)
        {
            yield return new Measurement<double>(
                StopwatchTicksToMilliseconds(Interlocked.Read(ref state.ElapsedTimestampTicks)),
                new KeyValuePair<string, object?>("stage", stage));
        }
    }

    static IEnumerable<Measurement<int>> ObservePipelineQueueDepth()
    {
        foreach (var (queue, state) in PipelineQueues)
        {
            yield return new Measurement<int>(
                Volatile.Read(ref state.Depth),
                new KeyValuePair<string, object?>("queue", queue),
                new KeyValuePair<string, object?>("capacity", Volatile.Read(ref state.Capacity)));
        }
    }

    sealed class FileTelemetryState
    {
        public long BufferedEntries;
        public long FlushedEntries;
        public FileTelemetryStatus Status = FileTelemetryStatus.Buffering;
    }

    sealed class PipelineTelemetryState
    {
        public long Count;
        public long ElapsedTimestampTicks;
    }

    sealed class PipelineQueueTelemetryState
    {
        public int Depth;
        public int Capacity;
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

    static void ObserveTraceTimestamp(ulong traceTimestampNs)
    {
        if (traceTimestampNs > long.MaxValue)
            return;

        var timestampNs = (long)traceTimestampNs;
        lock (TraceTimeLock)
        {
            if (timestampNs < _firstTraceTimestampNs)
            {
                _firstTraceTimestampNs = timestampNs;
            }

            if (timestampNs > _lastTraceTimestampNs)
            {
                _lastTraceTimestampNs = timestampNs;
            }
        }
    }

    static (long? FirstTraceTimestampNs, long? LastTraceTimestampNs) GetTraceTimeRange()
    {
        lock (TraceTimeLock)
        {
            if (_firstTraceTimestampNs == long.MaxValue || _lastTraceTimestampNs == long.MinValue)
            {
                return (null, null);
            }

            return (_firstTraceTimestampNs, _lastTraceTimestampNs);
        }
    }

    static double StopwatchTicksToMilliseconds(long timestampTicks)
        => timestampTicks * 1000.0 / Stopwatch.Frequency;

    public sealed record MetricsSnapshot(
        long TotalEvents,
        double CurrentRate,
        long? FirstTraceTimestampNs,
        long? LastTraceTimestampNs,
        FileMetricsSnapshot[] Files,
        PipelineStageMetricsSnapshot[] PipelineStages,
        PipelineQueueMetricsSnapshot[] PipelineQueues);
    public sealed record FileMetricsSnapshot(string FileName, long BufferedEntries, long FlushedEntries, string Status);
    public sealed record PipelineStageMetricsSnapshot(string Stage, long Count, double ElapsedMs);
    public sealed record PipelineQueueMetricsSnapshot(string Queue, int Depth, int Capacity);
}
