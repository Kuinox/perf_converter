using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PerfConverter;

// Main entry point for the application
public class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("PerfConverter - C# Performance Trace Converter");
        Console.WriteLine("Usage: This library is meant to be loaded by perf as a dlfilter");
    }
}

/// <summary>
/// Time units that can be used for trace events
/// </summary>
public enum TimestampMode
{
    Time,       // Wall clock time
    Cycles,     // CPU cycles
    Instructions // Instruction count
}

/// <summary>
/// Main class that implements the perf dlfilter interface
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe class PerfDlFilter
{
    [DllImport("PerfConverter", EntryPoint = "get_perf_dlfilter_fns")]
    public static extern unsafe PerfDlfilterFns* get_perf_dlfilter_fns();

    // Unmanaged exports for perf dlfilter
    [UnmanagedCallersOnly(EntryPoint = "start")]
    public static int Start(void** data, void* ctx)
    {
        try
        {
            return PerfTraceConverter.Instance.Start(data, ctx);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error in Start: {ex.Message}");
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "filter_event_early")]
    public static int FilterEventEarly(void* rawState, PerfDlFilterSample* sample, void* ctx)
    {
        try
        {
            return PerfTraceConverter.Instance.FilterEventEarly(rawState, sample, ctx);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error in FilterEventEarly: {ex.Message}");
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "stop")]
    public static int Stop(void* rawState, void* ctx)
    {
        try
        {
            return PerfTraceConverter.Instance.Stop(rawState, ctx);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error in Stop: {ex.Message}");
            return -1;
        }
    }
}

/// <summary>
/// Main implementation of the performance trace converter
/// </summary>
public unsafe class PerfTraceConverter
{
    private static readonly PerfTraceConverter _instance = new();
    public static PerfTraceConverter Instance => _instance;

    private readonly SymbolResolver _symbolResolver = new();
    private readonly Dictionary<TimestampMode, string> _timeUnitNames = new()
    {
        { TimestampMode.Time, "Time" },
        { TimestampMode.Cycles, "Cycles" },
        { TimestampMode.Instructions, "Instructions" }
    };

    // Parses command line arguments to get output filename and timestamp mode
    private (string filename, TimestampMode mode) ParseArgs(void* ctx)
    {
        var fns = PerfDlFilter.get_perf_dlfilter_fns();
        int argc = 0;
        IntPtr argv = fns->args(ctx, &argc);
        var args = new string[argc];
        
        for (int i = 0; i < argc; i++)
        {
            var argPtr = Marshal.ReadIntPtr(argv, i * IntPtr.Size);
            args[i] = Marshal.PtrToStringUTF8(argPtr) ?? string.Empty;
        }

        var filename = args.Length >= 1 ? args[0] : "out.ftf";
        var mode = TimestampMode.Instructions; // Default mode
        
        if (args.Length >= 2)
        {
            mode = args[1] switch
            {
                "t" => TimestampMode.Time,
                "c" => TimestampMode.Cycles,
                "i" => TimestampMode.Instructions,
                _ => TimestampMode.Instructions
            };
        }
        
        return (filename, mode);
    }

    public int Start(void** data, void* ctx)
    {
        var (filename, mode) = ParseArgs(ctx);
        
        // Create the trace state with an appropriate frame handler
        var frameHandler = CreateFrameHandler(filename);
        var state = new TraceState(frameHandler, mode);

        // Store the state in the GCHandle for later use
        *data = (void*)GCHandle.ToIntPtr(GCHandle.Alloc(state));
        return 0;
    }

    public int FilterEventEarly(void* rawState, PerfDlFilterSample* sample, void* ctx)
    {
        var handle = GCHandle.FromIntPtr((IntPtr)rawState);
        var state = (TraceState)handle.Target!;

        state.EventCount++;
        
        // Extract info from the event
        string eventType = _symbolResolver.InternString(state, sample->@event)!;
        var flag = sample->flags;
        var isCall = (flag & (uint)DLFilterFlag.PERF_DLFILTER_FLAG_CALL) != 0;
        var isBranch = (flag & (uint)DLFilterFlag.PERF_DLFILTER_FLAG_BRANCH) != 0;
        var isReturn = (flag & (uint)DLFilterFlag.PERF_DLFILTER_FLAG_RETURN) != 0;
        
        // Get thread ID
        var pid = (ulong)sample->pid;
        var tid = (ulong)sample->tid;
        var pidTid = (pid, tid);
        
        // Get or create thread state
        if (!state.Threads.TryGetValue(tid, out var threadState))
        {
            threadState = new ThreadState(pidTid);
            state.Threads[tid] = threadState;
        }

        // Update thread state with current event timing
        threadState.LastSeenTime = sample->time;
        
        if (eventType == "b") // 'branches' event
        {
            // Update instruction and cycle counts
            if (!state.HasInstructionEvents)
            {
                threadState.InsnCount += sample->insn_cnt;
            }
            threadState.CycCount += sample->cyc_cnt;
            
            // Handle branch event - if we detect IP changed significantly, consider it a trace break
            const ulong BadJumpHeuristic = 0x1000;
            if (sample->ip > threadState.Ip && sample->ip - threadState.Ip > BadJumpHeuristic)
            {
                threadState.Ip = 0;
            }
            
            if (threadState.Ip != 0 && sample->ip != 0)
            {
                // Normal path - update cache footprint
                state.CacheTracker.UpdateFootprint(threadState, threadState.Ip, sample->ip);
            }
            else
            {
                // Trace segment has ended - close all open stack frames
                while (threadState.StackDepth > 1)
                {
                    state.FrameHandler.PopFrame(threadState, state.TimestampMode, ctx);
                }
                
                state.FrameHandler.PushFrame(threadState, sample, state.TimestampMode, ctx);
            }
            
            threadState.Ip = sample->addr;
            
            if (isCall)
            {
                state.FrameHandler.PushFrame(threadState, sample, state.TimestampMode, ctx);
            }
            else if (isReturn)
            {
                if (threadState.StackDepth > 2)
                {
                    // Return matches a previously seen call
                    state.FrameHandler.PopFrame(threadState, state.TimestampMode, ctx);
                }
                else
                {
                    // Return doesn't match a previous call, so trace fragment started inside the frame
                    state.FrameHandler.PopUnknownFrame(threadState, sample, state.TimestampMode, ctx);
                }
            }
        }
        else
        {
            // This is an 'instructions' event
            state.HasInstructionEvents = true;
            threadState.InsnCount++;
        }
        
        return 0;
    }

    public int Stop(void* rawState, void* ctx)
    {
        var handle = GCHandle.FromIntPtr((IntPtr)rawState);
        var state = (TraceState)handle.Target!;

        // Close all remaining stacks for all threads
        foreach (var (_, threadState) in state.Threads)
        {
            while (threadState.StackDepth > 1)
            {
                state.FrameHandler.PopFrame(threadState, state.TimestampMode, ctx);
            }
        }
        
        // Finalize output and clean up
        state.FrameHandler.Finish();
        state.Dispose();
        handle.Free();
        
        return 0;
    }
    
    // Create appropriate frame handler based on output format/filename
    private ITraceFrameHandler CreateFrameHandler(string filename)
    {
        if (filename.EndsWith(".ftf", StringComparison.OrdinalIgnoreCase))
        {
            return new FuchsiaFrameHandler(filename);
        }
        else if (filename.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return new JsonTraceFrameHandler(filename);
        }
        else
        {
            // Default to Fuchsia Trace Format
            return new FuchsiaFrameHandler(filename);
        }
    }
}

/// <summary>
/// Maintains the state for a single trace processing session
/// </summary>
public class TraceState : IDisposable
{
    public ITraceFrameHandler FrameHandler { get; }
    public Dictionary<ulong, ThreadState> Threads { get; } = new Dictionary<ulong, ThreadState>();
    public Dictionary<IntPtr, string?> SymbolCache { get; } = new Dictionary<IntPtr, string?>();
    public CacheFootprintTracker CacheTracker { get; } = new CacheFootprintTracker();
    public long EventCount { get; set; }
    public bool HasInstructionEvents { get; set; }
    public TimestampMode TimestampMode { get; }
    
    public TraceState(ITraceFrameHandler frameHandler, TimestampMode mode)
    {
        FrameHandler = frameHandler;
        TimestampMode = mode;
    }
    
    public void Dispose()
    {
        if (FrameHandler is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

/// <summary>
/// Tracks and manages cache footprint data
/// </summary>
public class CacheFootprintTracker
{
    private const ulong CacheLineSize = 64;
    private const ulong CacheLineMask = ~(CacheLineSize - 1);
    
    public void UpdateFootprint(ThreadState threadState, ulong startIp, ulong endIp)
    {
        var cacheLineStart = startIp & CacheLineMask;
        var cacheLineEnd = endIp & CacheLineMask;
        var currentFrame = threadState.CurrentFrame;
        
        if (currentFrame == null) return;
        
        // Insert cache lines into the footprint set
        var cacheLine = cacheLineStart;
        while (cacheLine <= cacheLineEnd)
        {
            currentFrame.Footprint.Add(cacheLine);
            cacheLine += CacheLineSize;
        }
    }
    
    public ulong CalculateFootprintSize(HashSet<ulong> footprint)
    {
        return (ulong)footprint.Count * CacheLineSize;
    }
    
    public HashSet<ulong> MergeFootprints(HashSet<ulong> a, HashSet<ulong> b)
    {
        // Always merge into the larger set for efficiency
        if (a.Count < b.Count)
        {
            (a, b) = (b, a);
        }
        
        a.UnionWith(b);
        return a;
    }
}

/// <summary>
/// Symbol resolution and caching
/// </summary>
public unsafe class SymbolResolver
{
    public string? InternString(TraceState state, IntPtr intPtr)
    {
        if (intPtr == IntPtr.Zero) return null;
        
        if (state.SymbolCache.TryGetValue(intPtr, out var cachedSymbol))
        {
            return cachedSymbol;
        }
        
        var str = Marshal.PtrToStringUTF8(intPtr);
        state.SymbolCache[intPtr] = str;
        return str;
    }
    
    public string ResolveIp(PerfDlFilterSample* sample, void* ctx, byte[] buffer)
    {
        var dlfilter_fns = PerfDlFilter.get_perf_dlfilter_fns();
        var al = dlfilter_fns->resolve_ip(ctx);
        
        if (al != null && al->sym != IntPtr.Zero)
        {
            var symbol = Marshal.PtrToStringUTF8(al->sym);
            return symbol ?? FormatAddress(sample->ip);
        }
        
        return FormatAddress(sample->ip);
    }
    
    public string ResolveAddr(PerfDlFilterSample* sample, void* ctx, byte[] buffer)
    {
        if (sample->addr_correlates_sym != 0)
        {
            var dlfilter_fns = PerfDlFilter.get_perf_dlfilter_fns();
            var al = dlfilter_fns->resolve_addr(ctx);
            
            if (al != null && al->sym != IntPtr.Zero)
            {
                var symbol = Marshal.PtrToStringUTF8(al->sym);
                return symbol ?? FormatAddress((ulong)sample->addr);
            }
        }
        
        return FormatAddress((ulong)sample->addr);
    }
    
    private string FormatAddress(ulong address)
    {
        return $"0x{address:X}";
    }
}