using System.Text.Json;

namespace PerfConverter;

/// <summary>
/// Handles frame events and serializes them to JSON format
/// </summary>
public unsafe class JsonTraceFrameHandler : ITraceFrameHandler, IDisposable
{
    private readonly StreamWriter _writer;
    private readonly SymbolResolver _symbolResolver = new();
    private readonly byte[] _buffer = new byte[20]; // Buffer for address formatting
    private bool _firstEvent = true;
    
    public JsonTraceFrameHandler(string filename)
    {
        _writer = new StreamWriter(File.Create(filename));
        _writer.WriteLine("["); // Start JSON array
    }
    
    public void PushFrame(ThreadState threadState, PerfDlFilterSample* sample, TimestampMode mode, void* ctx)
    {
        string symbol;
        
        // For outer frames (stack depth 1), use "TRACE" as the symbol name
        if (threadState.StackDepth <= 1)
        {
            symbol = "TRACE";
        }
        else
        {
            symbol = _symbolResolver.ResolveAddr(sample, ctx, _buffer);
        }

        // Get timestamp based on mode
        ulong timestamp = GetTimestamp(threadState, sample, mode);
        
        // Push frame onto thread state
        threadState.PushFrame(symbol, sample->ip, sample->time, threadState.InsnCount, threadState.CycCount);
        
        // Write frame start event to JSON
        WriteEvent(() => $"{{\"type\":\"start\",\"symbol\":\"{EscapeJson(symbol)}\",\"timestamp\":{timestamp}," +
                         $"\"pid\":{threadState.PidTid.ProcessId},\"tid\":{threadState.PidTid.ThreadId}}}");
    }
    
    public void PopFrame(ThreadState threadState, TimestampMode mode, void* ctx)
    {
        var frame = threadState.PopFrame();
        if (frame == null) return;
        
        var (insnDelta, cycDelta, duration) = threadState.GetFrameMetrics(frame);
        var footprintSize = (ulong)frame.Footprint.Count * 64; // 64 bytes per cache line
        
        ulong timestamp = GetTimestamp(threadState, null, mode);
        
        // Merge footprint with parent frame
        threadState.MergeFootprints(frame);
        
        // Write frame end event to JSON
        WriteEvent(() => $"{{\"type\":\"end\",\"timestamp\":{timestamp}," +
                         $"\"pid\":{threadState.PidTid.ProcessId},\"tid\":{threadState.PidTid.ThreadId}," +
                         $"\"instructions\":{insnDelta},\"cycles\":{cycDelta}," +
                         $"\"footprint\":{footprintSize},\"start_ts\":{frame.StartTimestamp}," +
                         $"\"end_ts\":{threadState.LastSeenTime}}}");
    }
    
    public void PopUnknownFrame(ThreadState threadState, PerfDlFilterSample* sample, TimestampMode mode, void* ctx)
    {
        var frame = threadState.CurrentFrame;
        if (frame == null) return;
        
        var symbol = _symbolResolver.ResolveIp(sample, ctx, _buffer);
        var (insnDelta, cycDelta, _) = threadState.GetFrameMetrics(frame);
        var footprintSize = (ulong)frame.Footprint.Count * 64; // 64 bytes per cache line
        
        ulong timestamp = GetTimestamp(threadState, sample, mode);
        
        // Don't pop the frame, just write a point-in-time event for the unknown frame
        WriteEvent(() => $"{{\"type\":\"full\",\"symbol\":\"{EscapeJson(symbol)}\",\"timestamp\":{timestamp}," +
                         $"\"pid\":{threadState.PidTid.ProcessId},\"tid\":{threadState.PidTid.ThreadId}," +
                         $"\"instructions\":{insnDelta},\"cycles\":{cycDelta}," +
                         $"\"footprint\":{footprintSize},\"end_ts\":{timestamp}," +
                         $"\"start_ts\":{frame.StartTimestamp},\"end_full_ts\":{sample->time}}}");
        
        // Pop the frame anyway since we're returning from it
        threadState.PopFrame();
    }
    
    public void Finish()
    {
        // End array and close the JSON file
        _writer.WriteLine("\n]");
        _writer.Flush();
    }
    
    public void Dispose()
    {
        _writer.Dispose();
    }
    
    /// <summary>
    /// Write a JSON event to the output stream
    /// </summary>
    private void WriteEvent(Func<string> eventFormatter)
    {
        if (!_firstEvent)
        {
            _writer.WriteLine(",");
        }
        else
        {
            _firstEvent = false;
        }
        
        _writer.Write(eventFormatter());
    }
    
    /// <summary>
    /// Get appropriate timestamp based on mode
    /// </summary>
    private ulong GetTimestamp(ThreadState threadState, PerfDlFilterSample* sample, TimestampMode mode)
    {
        return mode switch
        {
            TimestampMode.Time => threadState.LastSeenTime,
            TimestampMode.Cycles => threadState.CycCount,
            TimestampMode.Instructions => threadState.InsnCount,
            _ => threadState.LastSeenTime
        };
    }
    
    /// <summary>
    /// Escape special characters in JSON strings
    /// </summary>
    private string EscapeJson(string input)
    {
        return JsonEncodedText.Encode(input).ToString();
    }
}