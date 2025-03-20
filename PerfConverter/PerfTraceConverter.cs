using System.Runtime.InteropServices;

namespace PerfConverter;

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
        var frameHandler = new FuchsiaFrameHandler(filename);
        var state = new TraceState(frameHandler, mode);

        // Store the state in the GCHandle for later use
        *data = (void*)GCHandle.ToIntPtr(GCHandle.Alloc(state));
        return 0;
    }

    public int FilterEventEarly(void* rawState, PerfDlFilterSample* sample, void* ctx)
    {
        var handle = GCHandle.FromIntPtr((IntPtr)rawState);
        var state = (TraceState)handle.Target!;

        
        // Extract info from the event
        string eventType = _symbolResolver.InternString(state, sample->@event)!;
        var flag = sample->flags;
        var isCall = (flag & (uint)DLFilterFlag.PERF_DLFILTER_FLAG_CALL) != 0;
        var isBranch = (flag & (uint)DLFilterFlag.PERF_DLFILTER_FLAG_BRANCH) != 0;
        var isReturn = (flag & (uint)DLFilterFlag.PERF_DLFILTER_FLAG_RETURN) != 0;
        Console.WriteLine(flag);
        Console.WriteLine($"Event {state.EventCount}: {eventType} (call={isCall}, branch={isBranch}, return={isReturn})");
        if (!isCall && !isReturn) return 0;
        state.EventCount++;
        if (state.EventCount > 260)
        {
            state.FrameHandler.Finish();
            return -1;
        }

        // Get thread ID
        var pid = (ulong)sample->pid;
        var tid = (ulong)sample->tid;
        var pidTid = (pid, tid);

        // Get or create thread state
        if (!state.Threads.TryGetValue(tid, out var threadState))
        {
            Console.WriteLine($"Creating new thread context for {pidTid}");
            threadState = new ThreadContext(pidTid);
            state.Threads[tid] = threadState;
        }

        // Update thread state with current event timing
        threadState.LastSeenTime = sample->time;


        if (isCall)
        {
            state.FrameHandler.PushFrame(threadState, sample, state.TimestampMode, ctx);
        }

        if (isReturn)
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

        if (!isCall && !isReturn && !isBranch)
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
}
