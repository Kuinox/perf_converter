using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CLI.ViewModel;

public class PerfMonitorViewModel : INotifyPropertyChanged
{
    private long _eventCount;
    private TimeSpan _elapsed;
    private int _overallRate;
    private int _currentRate;
    private DateTime _lastGcEvent = DateTime.MinValue;
    private bool _gcActive = false;
    private long _totalMemory;
    private long _gen0Count;
    private long _gen1Count;
    private long _gen2Count;
    private bool _isComplete;
    private bool _exitMessageReceived;

    public long EventCount
    {
        get => _eventCount;
        set => SetProperty(ref _eventCount, value);
    }

    public TimeSpan Elapsed
    {
        get => _elapsed;
        set => SetProperty(ref _elapsed, value);
    }

    public int OverallRate
    {
        get => _overallRate;
        set => SetProperty(ref _overallRate, value);
    }

    public int CurrentRate
    {
        get => _currentRate;
        set => SetProperty(ref _currentRate, value);
    }

    public DateTime LastGcEvent
    {
        get => _lastGcEvent;
        set => SetProperty(ref _lastGcEvent, value);
    }

    public bool GcActive
    {
        get => _gcActive;
        set => SetProperty(ref _gcActive, value);
    }

    public long TotalMemory
    {
        get => _totalMemory;
        set => SetProperty(ref _totalMemory, value);
    }

    public long Gen0Count
    {
        get => _gen0Count;
        set => SetProperty(ref _gen0Count, value);
    }

    public long Gen1Count
    {
        get => _gen1Count;
        set => SetProperty(ref _gen1Count, value);
    }

    public long Gen2Count
    {
        get => _gen2Count;
        set => SetProperty(ref _gen2Count, value);
    }

    public bool IsComplete
    {
        get => _isComplete;
        set => SetProperty(ref _isComplete, value);
    }

    public bool ExitMessageReceived
    {
        get => _exitMessageReceived;
        set => SetProperty(ref _exitMessageReceived, value);
    }

    public ConcurrentDictionary<string, FileStatus> FileStatuses { get; } = new();
    public ConcurrentQueue<string> OutputLines { get; } = new();
    public ConcurrentQueue<string> ErrorLines { get; } = new();
    public Queue<(DateTime timestamp, long eventCount)> RateHistory { get; } = new();

    public double MemoryMB => TotalMemory / 1024.0 / 1024.0;

    public string GcStatus
    {
        get
        {
            if (GcActive)
            {
                return "[red]🔥 GC ACTIVE[/]";
            }
            else if (LastGcEvent != DateTime.MinValue)
            {
                var timeSinceLastGc = DateTime.UtcNow - LastGcEvent;
                return $"[dim]Last GC: {timeSinceLastGc.TotalSeconds:F0}s ago[/]";
            }
            return "";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        
        // Trigger GcStatus update when GC-related properties change
        if (propertyName == nameof(GcActive) || propertyName == nameof(LastGcEvent))
        {
            OnPropertyChanged(nameof(GcStatus));
        }
        
        return true;
    }

    public void UpdateRateHistory()
    {
        var now = DateTime.UtcNow;
        RateHistory.Enqueue((now, EventCount));
        
        // Remove entries older than 5 seconds
        while (RateHistory.Count > 0 && (now - RateHistory.Peek().timestamp).TotalSeconds > 5)
        {
            RateHistory.Dequeue();
        }
        
        if (RateHistory.Count >= 2)
        {
            var oldest = RateHistory.First();
            var newest = RateHistory.Last();
            var timeDiff = (newest.timestamp - oldest.timestamp).TotalSeconds;
            if (timeDiff > 0)
            {
                CurrentRate = (int)((newest.eventCount - oldest.eventCount) / timeDiff);
            }
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

    public void TrimOutputLines(int maxLines = 50)
    {
        while (OutputLines.Count > maxLines)
        {
            OutputLines.TryDequeue(out _);
        }
    }

    public void TrimErrorLines(int maxLines = 20)
    {
        while (ErrorLines.Count > maxLines)
        {
            ErrorLines.TryDequeue(out _);
        }
    }
}