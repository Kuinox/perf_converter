namespace PerfConverter.Fuchsia;

/// <summary>
/// Stores state for the current trace processing session
/// </summary>
public class TraceState
{
    // Cache for string symbols
    public Dictionary<IntPtr, string?> SymbolCache { get; } = new();
    
    // Thread state cache
    private readonly Dictionary<(ulong, ulong), ThreadContext> _threadStates = new();
    
    /// <summary>
    /// Gets or creates a thread context for the given process and thread ID
    /// </summary>
    public ThreadContext GetThreadState(ulong processId, ulong threadId)
    {
        var key = (processId, threadId);
        
        if (!_threadStates.TryGetValue(key, out var threadState))
        {
            threadState = new ThreadContext(key);
            _threadStates[key] = threadState;
        }
        
        return threadState;
    }
    
    /// <summary>
    /// Gets all thread contexts
    /// </summary>
    public IEnumerable<ThreadContext> GetThreadStates()
    {
        return _threadStates.Values;
    }
}