namespace PerfConverter;

/// <summary>
/// Represents data for a specific call stack frame
/// </summary>
public class StackFrame
{
    // Counters at the start of this frame
    public ulong StartInsnCount { get; set; }
    public ulong StartCycCount { get; set; }
    public ulong StartTimestamp { get; set; }
    
    // Symbol information
    public string? SymbolName { get; set; }
    public ulong Address { get; set; }
    
    // Cache footprint for this frame
    public HashSet<ulong> Footprint { get; set; } = new HashSet<ulong>();
    
    public StackFrame(string? symbolName, ulong address, ulong timestamp, ulong insnCount, ulong cycCount)
    {
        SymbolName = symbolName;
        Address = address;
        StartTimestamp = timestamp;
        StartInsnCount = insnCount;
        StartCycCount = cycCount;
    }
    
    // Create a default frame with zeroed counters
    public StackFrame()
    {
        SymbolName = "ROOT";
        Address = 0;
        StartTimestamp = 0;
        StartInsnCount = 0;
        StartCycCount = 0;
    }
}

/// <summary>
/// Manages the state of a thread including call stack and performance counters
/// </summary>
public class ThreadContext
{
    // Thread identification
    public (ulong ProcessId, ulong ThreadId) PidTid { get; }
    
    public ulong InsnCount { get; set; }
    public ulong CycCount { get; set; }
    public ulong LastSeenTime { get; set; }
    
    // Call stack frames
    private readonly List<StackFrame> _stack = new();
    
    public ThreadContext((ulong, ulong) pidTid)
    {
        PidTid = pidTid;
        
        // Initialize with root frame (for entire trace) and segment frame (for current trace segment)
        _stack.Add(new StackFrame()); // Root frame
        _stack.Add(new StackFrame()); // Current segment frame
    }
    
    /// <summary>
    /// Pushes a new frame onto the call stack
    /// </summary>
    public void PushFrame(string? symbolName, ulong address, ulong timestamp, ulong insnCount, ulong cycCount)
    {
        var frame = new StackFrame(symbolName, address, timestamp, insnCount, cycCount);
        _stack.Add(frame);
    }
    
    /// <summary>
    /// Pops a frame from the call stack
    /// </summary>
    public StackFrame? PopFrame()
    {
        if (_stack.Count <= 1)
            return null;
            
        var frame = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        return frame;
    }
    
    /// <summary>
    /// Gets the current (top) frame in the stack
    /// </summary>
    public StackFrame? CurrentFrame => _stack.Count > 0 ? _stack[^1] : null;
    
    /// <summary>
    /// Gets the parent of the current frame
    /// </summary>
    public StackFrame? ParentFrame => _stack.Count > 1 ? _stack[^2] : null;
    
    /// <summary>
    /// Gets the number of frames in the stack
    /// </summary>
    public int StackDepth => _stack.Count;
    
    /// <summary>
    /// Gets a copy of the current stack frames (newest to oldest)
    /// </summary>
    public IEnumerable<StackFrame> GetStackFrames()
    {
        return _stack.AsEnumerable().Reverse();
    }
    
    /// <summary>
    /// Merges a popped frame's footprint with its parent frame
    /// </summary>
    public void MergeFootprints(StackFrame poppedFrame)
    {
        if (_stack.Count > 0)
        {
            var parent = _stack[^1];
            foreach (var cacheLine in poppedFrame.Footprint)
            {
                parent.Footprint.Add(cacheLine);
            }
        }
    }
    
    /// <summary>
    /// Calculate metrics for the current frame relative to when it started
    /// </summary>
    public (ulong InsnDelta, ulong CycDelta, ulong Duration) GetFrameMetrics(StackFrame frame)
    {
        var insnDelta = InsnCount - frame.StartInsnCount;
        var cycDelta = CycCount - frame.StartCycCount;
        var duration = LastSeenTime - frame.StartTimestamp;
        
        return (insnDelta, cycDelta, duration);
    }
}