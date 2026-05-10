using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using PropertyChanged;

namespace CLI;

[AddINotifyPropertyChangedInterface]
public class PerfMonitorViewModel
{
    static readonly TimeSpan HistoryWindow = TimeSpan.FromMinutes(1);

    readonly Lock _rateHistorySync = new();
    readonly Queue<RateSample> _totalRateHistory = new();
    readonly Lock _eventTimingSync = new();
    readonly Lock _pipelineSync = new();
    readonly Dictionary<string, PipelineStageState> _pipelineStages = new(StringComparer.Ordinal);
    readonly Dictionary<string, PipelineQueueState> _pipelineQueues = new(StringComparer.Ordinal);
    DateTime? _firstEventObservedUtc;
    DateTime? _lastEventObservedUtc;

    public long EventCount { get; set; }
    public TimeSpan Elapsed { get; set; }
    public int CurrentRate { get; set; }
    public long? FirstTraceTimestampNs { get; set; }
    public long? LastTraceTimestampNs { get; set; }
    public DateTime LastCurrentRateUpdateUtc { get; set; } = DateTime.MinValue;
    public DateTime LastGcEvent { get; set; } = DateTime.MinValue;
    public bool GcActive { get; set; }
    public long TotalMemory { get; set; }
    public long Gen0Count { get; set; }
    public long Gen1Count { get; set; }
    public long Gen2Count { get; set; }
    public bool IsComplete { get; set; }
    public bool ShutdownRequested { get; set; }
    public bool ExitMessageReceived { get; set; }
    public bool ProcessHasExited { get; set; }
    public bool PipesDrained { get; set; }

    [DependsOn(nameof(ShutdownRequested), nameof(ExitMessageReceived), nameof(ProcessHasExited), nameof(PipesDrained))]
    public string Status
    {
        get
        {
            if (ProcessHasExited && PipesDrained)
                return "Process exited";

            if (ShutdownRequested || ExitMessageReceived || ProcessHasExited)
                return "Exiting";

            return "Running";
        }
    }

    public double TotalGcTimeMs { get; set; }
    public DateTime ProcessStartTime { get; set; } = DateTime.UtcNow;
    public string StatusMessage { get; set; } = string.Empty;
    public string PipelineBottleneck { get; set; } = "No pipeline metrics yet";
    public string PipelineQueues { get; set; } = "-";

    public ConcurrentDictionary<string, FileStatus> FileStatuses { get; } = new();
    public ConcurrentQueue<string> OutputLines { get; } = new();
    public ConcurrentQueue<string> ErrorLines { get; } = new();
    public ConcurrentQueue<string> RawErrorLines { get; } = new();
    public double MemoryMB => TotalMemory / 1024.0 / 1024.0;

    public int OverallRate
    {
        get
        {
            lock (_eventTimingSync)
            {
                if (!_firstEventObservedUtc.HasValue)
                    return 0;

                var endUtc = _lastEventObservedUtc ?? DateTime.UtcNow;
                var elapsedSeconds = Math.Max((endUtc - _firstEventObservedUtc.Value).TotalSeconds, 0);
                if (elapsedSeconds <= 0)
                    return CurrentRate;

                return (int)Math.Round(EventCount / elapsedSeconds);
            }
        }
    }

    [DependsOn(nameof(TotalGcTimeMs), nameof(Elapsed))]
    public double GcPercentage
    {
        get
        {
            if (Elapsed.TotalMilliseconds <= 0)
                return 0;

            return (TotalGcTimeMs / Elapsed.TotalMilliseconds) * 100;
        }
    }

    [DependsOn(nameof(GcActive), nameof(LastGcEvent))]
    public string GcStatus
    {
        get
        {
            if (GcActive)
                return "GC ACTIVE";

            if (LastGcEvent != DateTime.MinValue)
            {
                var timeSinceLastGc = DateTime.UtcNow - LastGcEvent;
                return $"Last GC: {timeSinceLastGc.TotalSeconds:F0}s ago";
            }

            return "No GC activity yet";
        }
    }

    public void CleanupExpiredFiles()
    {
        var now = DateTime.UtcNow;
        var filesToRemove = new List<string>();

        foreach (var kvp in FileStatuses.ToArray())
        {
            var file = kvp.Value;
            if (file.Status == "CLOSED" && file.ClosedAt.HasValue && (now - file.ClosedAt.Value).TotalSeconds > 10)
            {
                filesToRemove.Add(kvp.Key);
            }
        }

        foreach (var key in filesToRemove)
        {
            FileStatuses.TryRemove(key, out _);
        }
    }

    public void UpdateEventCount(long eventCount, DateTime observedAtUtc)
    {
        EventCount = eventCount;

        if (eventCount <= 0)
            return;

        lock (_eventTimingSync)
        {
            _firstEventObservedUtc ??= observedAtUtc;
            _lastEventObservedUtc = observedAtUtc;
        }
    }

    public void RecordTotalRate(double rate)
    {
        lock (_rateHistorySync)
        {
            _totalRateHistory.Enqueue(new RateSample(DateTime.UtcNow, rate));
            TrimTotalRateHistoryUnsafe(DateTime.UtcNow);
        }
    }

    public void UpdatePipelineMetrics(
        IReadOnlyList<PipelineStageMetricsSnapshot> stages,
        IReadOnlyList<PipelineQueueMetricsSnapshot> queues,
        DateTime observedAtUtc)
    {
        lock (_pipelineSync)
        {
            var bottleneckStage = string.Empty;
            var bottleneckMsPerSecond = 0.0;

            foreach (var stage in stages)
            {
                ref var state = ref CollectionsMarshal.GetValueRefOrAddDefault(_pipelineStages, stage.Stage, out var exists);
                state ??= new PipelineStageState();

                var deltaMs = exists ? Math.Max(0, stage.ElapsedMs - state.ElapsedMs) : 0;
                var elapsedSeconds = exists ? Math.Max((observedAtUtc - state.ObservedAtUtc).TotalSeconds, 0) : 0;
                var msPerSecond = elapsedSeconds > 0 ? deltaMs / elapsedSeconds : 0;

                state.ElapsedMs = stage.ElapsedMs;
                state.Count = stage.Count;
                state.ObservedAtUtc = observedAtUtc;

                if (msPerSecond > bottleneckMsPerSecond)
                {
                    bottleneckMsPerSecond = msPerSecond;
                    bottleneckStage = stage.Stage;
                }
            }

            foreach (var queue in queues)
            {
                _pipelineQueues[queue.Queue] = new PipelineQueueState(queue.Depth, queue.Capacity);
            }

            PipelineBottleneck = string.IsNullOrEmpty(bottleneckStage)
                ? "No active pipeline pressure"
                : $"{bottleneckStage} ({bottleneckMsPerSecond:F0} ms/s)";
            PipelineQueues = _pipelineQueues.Count == 0
                ? "-"
                : string.Join(", ", _pipelineQueues
                    .OrderBy(static x => x.Key)
                    .Select(static x => $"{x.Key} {x.Value.Depth}/{x.Value.Capacity}"));
        }
    }

    public double[] GetTotalRateHistorySnapshot()
    {
        lock (_rateHistorySync)
        {
            TrimTotalRateHistoryUnsafe(DateTime.UtcNow);
            return _totalRateHistory.Select(x => x.Value).ToArray();
        }
    }

    public void TrimOutputLines(int maxLines = 50)
    {
        while (OutputLines.Count > maxLines)
        {
            OutputLines.TryDequeue(out _);
        }
    }

    public void TrimErrorLines(int maxLines = 200)
    {
        while (ErrorLines.Count > maxLines)
        {
            ErrorLines.TryDequeue(out _);
        }
    }

    void TrimTotalRateHistoryUnsafe(DateTime nowUtc)
    {
        while (_totalRateHistory.Count > 0 && (nowUtc - _totalRateHistory.Peek().TimestampUtc) > HistoryWindow)
        {
            _totalRateHistory.Dequeue();
        }
    }

    readonly record struct RateSample(DateTime TimestampUtc, double Value);
    sealed class PipelineStageState
    {
        public long Count;
        public double ElapsedMs;
        public DateTime ObservedAtUtc;
    }

    readonly record struct PipelineQueueState(int Depth, int Capacity);
    public readonly record struct PipelineStageMetricsSnapshot(string Stage, long Count, double ElapsedMs);
    public readonly record struct PipelineQueueMetricsSnapshot(string Queue, int Depth, int Capacity);
}
