namespace PerfConverter;

/// <summary>
/// Interface for handling frame events in different trace formats
/// </summary>
public unsafe interface ITraceFrameHandler
{
    /// <summary>
    /// Push a new frame onto the stack
    /// </summary>
    void PushFrame(ThreadState threadState, PerfDlFilterSample* sample, TimestampMode mode, void* ctx);
    
    /// <summary>
    /// Pop a frame from the stack
    /// </summary>
    void PopFrame(ThreadState threadState, TimestampMode mode, void* ctx);
    
    /// <summary>
    /// Handle a frame that was already in progress when tracing started
    /// </summary>
    void PopUnknownFrame(ThreadState threadState, PerfDlFilterSample* sample, TimestampMode mode, void* ctx);
    
    /// <summary>
    /// Finish writing the trace
    /// </summary>
    void Finish();
}