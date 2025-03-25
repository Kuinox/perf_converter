using System.Globalization;
using System.Text;
using PerfConverter.Fuchsia;

namespace PerfConverter;

/// <summary>
/// Handles frame events and serializes them to Fuchsia Trace Format (FTF)
/// </summary>
public unsafe class FuchsiaFrameHandler : IDisposable
{
    private const string PROVIDER_NAME = "PerfConverter";
    private const string CATEGORY_NAME = "Perf";
    
    private readonly BinaryWriter _writer;
    private readonly SymbolResolver _symbolResolver = new();
    private readonly byte[] _buffer = new byte[20]; // Buffer for address formatting
    
    // Cache for strings and thread info
    private readonly StringCache _stringCache = new();
    private readonly ThreadCache _threadCache = new();
    
    public FuchsiaFrameHandler(string filename)
    {
        // Create the output file
        var stream = File.Create(filename);
        _writer = new BinaryWriter(stream);
        
        // Write the FTF header and initialize internal strings
        WriteHeader();
    }

    private void WriteHeader()
    {
        // Write magic number
        _writer.Write(0x0016547846040010UL);
        
        // Provider info metadata
        var providerInfo = new MetadataRecord(
            MetadataType.ProviderInfo,
            0, // Provider ID
            PROVIDER_NAME);
        providerInfo.Write(_writer);
        
        // Provider section metadata
        var providerSection = new MetadataRecord(
            MetadataType.ProviderSection,
            0); // Provider ID
        providerSection.Write(_writer);
        
        // Write internal strings
        foreach (var name in InternalStrings.GetNames())
        {
            var stringRecord = new StringRecord((ushort)InternalStrings.GetIndex(name), name);
            stringRecord.Write(_writer);
        }
    }
    
    public void PushFrame(ThreadContext threadState, PerfDlFilterSample* sample, TimestampMode mode, void* ctx)
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
        
        // Register thread and get reference
        var threadRef = _threadCache.GetThreadRef(_writer, threadState.PidTid);
        
        // Register symbol and get reference
        var symbolRef = _stringCache.GetStringRef(_writer, symbol);
        var categoryRef = _stringCache.GetStringRef(_writer, CATEGORY_NAME);
        
        // Create and write event record
        var eventRecord = new EventRecord(
            EventType.DurationBegin,
            timestamp,
            threadRef,
            categoryRef,
            symbolRef);
        Console.WriteLine($"Pushing frame {symbol} at {timestamp}");
        eventRecord.Write(_writer);
        
        // Push frame onto thread state
        threadState.PushFrame(symbol, sample->ip, sample->time, threadState.InsnCount, threadState.CycCount);
    }
    
    public void PopFrame(ThreadContext threadState, TimestampMode mode, void* ctx)
    {
        var frame = threadState.PopFrame();
        if (frame == null) return;
        
        var (insnDelta, cycDelta, duration) = threadState.GetFrameMetrics(frame);
        var footprintSize = (ulong)frame.Footprint.Count * 64; // 64 bytes per cache line
        
        ulong timestamp = GetTimestamp(threadState, null, mode);
        
        // Register thread and get reference
        var threadRef = _threadCache.GetThreadRef(_writer, threadState.PidTid);
        var categoryRef = _stringCache.GetStringRef(_writer, CATEGORY_NAME);
        var emptyRef = _stringCache.GetStringRef(_writer, "");
        
        // Create arguments for the event
        var instrArg = new NumericArgument(InternalStrings.Instructions, insnDelta);
        var cyclesArg = new NumericArgument(InternalStrings.Cycles, cycDelta);
        var footprintArg = new NumericArgument(InternalStrings.Footprint, footprintSize);
        
        // Create timespan string
        var timespanStr = FormatTimespan(frame.StartTimestamp, threadState.LastSeenTime);
        var timespanArg = new StringArgument(InternalStrings.Timespan, timespanStr);
        
        // Create and write event record
        var eventRecord = new EventRecord(
            EventType.DurationEnd,
            timestamp,
            threadRef,
            categoryRef,
            emptyRef,
            new RecordArgument[] { instrArg, cyclesArg, footprintArg, timespanArg });
        
        eventRecord.Write(_writer);
        
        // Merge footprint with parent frame
        threadState.MergeFootprints(frame);
    }
    
    public void PopUnknownFrame(ThreadContext threadState, PerfDlFilterSample* sample, TimestampMode mode, void* ctx)
    {
        var frame = threadState.CurrentFrame;
        if (frame == null) return;
        
        var symbol = _symbolResolver.ResolveIp(sample, ctx, _buffer);
        var (insnDelta, cycDelta, _) = threadState.GetFrameMetrics(frame);
        var footprintSize = (ulong)frame.Footprint.Count * 64; // 64 bytes per cache line
        
        ulong timestamp = GetTimestamp(threadState, sample, mode);
        
        // Register thread and get reference
        var threadRef = _threadCache.GetThreadRef(_writer, threadState.PidTid);
        var categoryRef = _stringCache.GetStringRef(_writer, CATEGORY_NAME);
        var symbolRef = _stringCache.GetStringRef(_writer, symbol);
        
        // Create arguments for the event
        var instrArg = new NumericArgument(InternalStrings.Instructions, insnDelta);
        var cyclesArg = new NumericArgument(InternalStrings.Cycles, cycDelta);
        var footprintArg = new NumericArgument(InternalStrings.Footprint, footprintSize);
        
        // Create timespan string
        var timespanStr = FormatTimespan(frame.StartTimestamp, sample->time);
        var timespanArg = new StringArgument(InternalStrings.Timespan, timespanStr);
        
        // Create and write event record with additional end timestamp
        var eventRecord = new EventRecord(
            EventType.DurationComplete,
            timestamp,
            threadRef,
            categoryRef,
            symbolRef,
            new RecordArgument[] { instrArg, cyclesArg, footprintArg, timespanArg },
            null, null, null,
            sample->time); // Add end timestamp
        
        eventRecord.Write(_writer);
        
        // Pop the frame anyway since we're returning from it
        threadState.PopFrame();
    }
    
    public void Finish()
    {
        _writer.Flush();
    }
    
    public void Dispose()
    {
        _writer.Dispose();
    }
    
    /// <summary>
    /// Get appropriate timestamp based on mode
    /// </summary>
    private ulong GetTimestamp(ThreadContext threadState, PerfDlFilterSample* sample, TimestampMode mode)
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
    /// Format timespan in the format "start,end" with nanosecond precision
    /// </summary>
    private string FormatTimespan(ulong start, ulong end)
    {
        var startStr = FormatTimestamp(start);
        var endStr = FormatTimestamp(end);
        return $"{startStr},{endStr}";
    }
    
    /// <summary>
    /// Format timestamp with nanosecond precision (1234.567890000)
    /// </summary>
    private string FormatTimestamp(ulong nanos)
    {
        // Format as seconds.nanoseconds
        var seconds = nanos / 1_000_000_000;
        var fraction = nanos % 1_000_000_000;
        return $"{seconds}.{fraction:D9}";
    }
}

/// <summary>
/// Cache for string references in Fuchsia Trace Format
/// </summary>
public class StringCache
{
    private readonly Dictionary<string, ushort> _cache = new();
    private ushort _nextIndex;
    
    public StringCache()
    {
        // Reserve space for internal strings
        _nextIndex = (ushort)InternalStrings.Count;
        
        // Add empty string
        _cache[""] = 0;
    }
    
    public ushort GetStringRef(BinaryWriter writer, string str)
    {
        // Check if string is already in cache
        if (_cache.TryGetValue(str, out var index))
        {
            return index;
        }
        
        // Add to cache
        index = _nextIndex++;
        _cache[str] = index;
        
        // Write string record
        var record = new StringRecord(index, str);
        record.Write(writer);
        
        return index;
    }
}

/// <summary>
/// Cache for thread references in Fuchsia Trace Format
/// </summary>
public class ThreadCache
{
    private readonly Dictionary<(ulong, ulong), byte> _cache = new();
    private byte _nextIndex = 1; // 0 is reserved
    
    public byte GetThreadRef(BinaryWriter writer, (ulong ProcessId, ulong ThreadId) pidTid)
    {
        // Check if thread is already in cache
        if (_cache.TryGetValue(pidTid, out var index))
        {
            return index;
        }
        
        // Add to cache
        index = _nextIndex++;
        _cache[pidTid] = index;
        
        // Write thread record
        var record = new ThreadRecord(index, pidTid.ProcessId, pidTid.ThreadId);
        record.Write(writer);
        
        return index;
    }
}

/// <summary>
/// Internal string constants used in Fuchsia Trace Format
/// </summary>
public static class InternalStrings
{
    // String indices (must match order in GetNames)
    public const ushort Empty = 0;
    public const ushort Instructions = 1;
    public const ushort Cycles = 2;
    public const ushort Footprint = 3;
    public const ushort Symbol = 4;
    public const ushort Timespan = 5;
    
    // Total count of internal strings
    public const int Count = 6;
    
    // Get all internal string names
    public static string[] GetNames()
    {
        return new[]
        {
            "",             // Empty
            "Instructions", 
            "Cycles",
            "Footprint",
            "Symbol",
            "Timespan"
        };
    }
    
    // Get the index of a named internal string
    public static ushort GetIndex(string name)
    {
        return name switch
        {
            "" => Empty,
            "Instructions" => Instructions,
            "Cycles" => Cycles,
            "Footprint" => Footprint,
            "Symbol" => Symbol,
            "Timespan" => Timespan,
            _ => 0
        };
    }
}