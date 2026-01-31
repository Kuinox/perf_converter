using System.Collections.Concurrent;
using System.ComponentModel;
using PropertyChanged;

namespace CLI;

[AddINotifyPropertyChangedInterface]
public class PerfMonitorViewModel
{
    public long EventCount { get; set; }
    public TimeSpan Elapsed { get; set; }
    public int OverallRate { get; set; }
    public int CurrentRate { get; set; }
    public DateTime LastGcEvent { get; set; } = DateTime.MinValue;
    public bool GcActive { get; set; } = false;
    public long TotalMemory { get; set; }
    public long Gen0Count { get; set; }
    public long Gen1Count { get; set; }
    public long Gen2Count { get; set; }
    public bool IsComplete { get; set; }
    public bool ExitMessageReceived { get; set; }
    public bool ProcessHasExited { get; set; }
    public bool PipesDrained { get; set; }

    [DependsOn(nameof(ExitMessageReceived), nameof(ProcessHasExited), nameof(PipesDrained))]
    public string Status => (ExitMessageReceived || (ProcessHasExited && PipesDrained)) ? "[red]Process exited[/]" : "[green]Running[/]";

    public double TotalGcTimeMs { get; set; }
    public DateTime ProcessStartTime { get; set; } = DateTime.UtcNow;
    public string StatusMessage { get; set; } = string.Empty;


    public ConcurrentDictionary<string, FileStatus> FileStatuses { get; } = new();
    public ConcurrentQueue<string> OutputLines { get; } = new();
    public ConcurrentQueue<string> ErrorLines { get; } = new();

    public double MemoryMB => TotalMemory / 1024.0 / 1024.0;
    
    [DependsOn(nameof(TotalGcTimeMs), nameof(Elapsed))]
    public double GcPercentage
    {
        get
        {
            if (Elapsed.TotalMilliseconds <= 0) return 0;
            return (TotalGcTimeMs / Elapsed.TotalMilliseconds) * 100;
        }
    }

    [DependsOn(nameof(GcActive), nameof(LastGcEvent))]
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