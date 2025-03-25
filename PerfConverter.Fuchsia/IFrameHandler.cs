namespace PerfConverter.Fuchsia;

/// <summary>
/// Interface for handling frame push/pop events
/// </summary>
public unsafe interface IFrameHandler
{
    /// <summary>
    /// Called when a new frame is pushed onto the call stack
    /// </summary>
    void PushFrame(ThreadContext threadState, PerfDlFilterSample* sample, TimestampMode mode, void* ctx);
    
    /// <summary>
    /// Called when a frame is popped from the call stack
    /// </summary>
    void PopFrame(ThreadContext threadState, TimestampMode mode, void* ctx);
    
    /// <summary>
    /// Called when an unknown frame is popped (without a matching push)
    /// </summary>
    void PopUnknownFrame(ThreadContext threadState, PerfDlFilterSample* sample, TimestampMode mode, void* ctx);
    
    /// <summary>
    /// Called when trace processing is complete
    /// </summary>
    void Finish();
}