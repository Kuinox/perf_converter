namespace PerfConverter;

/// <summary>
/// Maintains the state for a single trace processing session
/// </summary>
public class TraceState(FuchsiaFrameHandler frameHandler, TimestampMode mode) : IDisposable
{
    public FuchsiaFrameHandler FrameHandler { get; } = frameHandler;
    public Dictionary<ulong, ThreadContext> Threads { get; } = new Dictionary<ulong, ThreadContext>();
    public Dictionary<IntPtr, string?> SymbolCache { get; } = new Dictionary<IntPtr, string?>();
    public CacheFootprintTracker CacheTracker { get; } = new CacheFootprintTracker();
    public long EventCount { get; set; }
    public bool HasInstructionEvents { get; set; }
    public TimestampMode TimestampMode { get; } = mode;

    public void Dispose()
    {
        if (FrameHandler is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
