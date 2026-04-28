namespace CLI;

public class FileStatus
{
    static readonly TimeSpan HistoryWindow = TimeSpan.FromMinutes(1);

    readonly Lock _sync = new();
    readonly Queue<RateSample> _rateHistory = new();

    DateTime? _lastRateTimestampUtc;
    long _lastFlushedCount;

    public string FileName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long EntryCount { get; set; }
    public long BufferedCount { get; set; }
    public long FlushedCount { get; set; }
    public long CurrentRate { get; private set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }
    public DateTime? LastActivity { get; set; }

    public void RecordFlushedCount(long flushedCount, DateTime observedAtUtc)
    {
        lock (_sync)
        {
            if (_lastRateTimestampUtc.HasValue)
            {
                var elapsedSeconds = Math.Max((observedAtUtc - _lastRateTimestampUtc.Value).TotalSeconds, 0);
                if (elapsedSeconds > 0)
                {
                    var delta = Math.Max(0, flushedCount - _lastFlushedCount);
                    CurrentRate = (long)Math.Round(delta / elapsedSeconds);
                    AddSampleUnsafe(CurrentRate, observedAtUtc);
                }
            }

            _lastRateTimestampUtc = observedAtUtc;
            _lastFlushedCount = flushedCount;
            FlushedCount = flushedCount;
        }
    }

    public void ResetCurrentRate()
    {
        lock (_sync)
        {
            CurrentRate = 0;
            AddSampleUnsafe(0, DateTime.UtcNow);
        }
    }

    public double[] GetRateHistorySnapshot()
    {
        lock (_sync)
        {
            TrimHistoryUnsafe(DateTime.UtcNow);
            return _rateHistory.Select(x => x.Value).ToArray();
        }
    }

    void AddSampleUnsafe(double value, DateTime observedAtUtc)
    {
        _rateHistory.Enqueue(new RateSample(observedAtUtc, value));
        TrimHistoryUnsafe(observedAtUtc);
    }

    void TrimHistoryUnsafe(DateTime nowUtc)
    {
        while (_rateHistory.Count > 0 && (nowUtc - _rateHistory.Peek().TimestampUtc) > HistoryWindow)
        {
            _rateHistory.Dequeue();
        }
    }

    readonly record struct RateSample(DateTime TimestampUtc, double Value);
}
