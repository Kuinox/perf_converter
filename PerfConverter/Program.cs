using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PerfConverter;

[StructLayout(LayoutKind.Sequential)]
public unsafe class PerfDlFilter
{
    [DllImport("PerfConverter", EntryPoint = "get_perf_dlfilter_fns")]
    public static extern unsafe PerfDlfilterFns* get_perf_dlfilter_fns();

    private class State : IDisposable
    {
        public StreamWriter Writer { get; set; } = null!;
        public int EventCount { get; set; }
        public HashSet<string> Events { get; set; } = null!;
        public Dictionary<IntPtr, string?> SymbolCache { get; set; } = new Dictionary<IntPtr, string>();

        public void Dispose()
        {
            Writer?.Dispose();
        }
    }

    private static string? GetEventString(IntPtr eventPtr) => Marshal.PtrToStringUTF8(eventPtr);

    [UnmanagedCallersOnly(EntryPoint = "start")]
    public static int Start(void** data, void* ctx)
    {
        try
        {
            var state = new State
            {
                Writer = new StreamWriter(File.Create("output.txt")),
                EventCount = 0,
                Events = new(),
                SymbolCache = new()
            };

            // Write header
            state.Writer.WriteLine("Event#\tTime\tPID\tTID\tInstructions\tCycles\tEvent Type");
            state.Writer.WriteLine("--------------------------------------------------------");
            state.Writer.Flush();

            *data = (void*)GCHandle.ToIntPtr(GCHandle.Alloc(state));
            return 0;
        }
        catch
        {
            return -1;
        }
    }

    static string? InternString(State state, IntPtr intPtr)
    {
        if(intPtr == IntPtr.Zero) return null;
        if (state.SymbolCache.TryGetValue(intPtr, out var cachedSymbol))
        {
            return cachedSymbol;
        }
        var str = Marshal.PtrToStringUTF8(intPtr);
        state.SymbolCache[intPtr] = str;
        state.Writer.WriteLine($"Resolved: {str}");
        return str;
    }

    static void ResolveAddress(State state, PerfDlFilterSample* sample, void* ctx)
    {
        if (sample->addr_correlates_sym != 0)
        {
            var addr = (IntPtr)sample->addr;
            var dlfilter_fns = get_perf_dlfilter_fns();
            var al = dlfilter_fns->resolve_ip(ctx);
            
            if (al != null)
            {
                var symbolName = InternString(state, al->sym);
                //state.Writer.WriteLine($"Resolved: {symbolName}");
            }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "filter_event_early")]
    public static int FilterEventEarly(void* rawState, PerfDlFilterSample* sample, void* ctx)
    {
        var handle = GCHandle.FromIntPtr((IntPtr)rawState);
        var state = (State)handle.Target!;

        try
        {
            // extract info from the event.
            string eventType = GetEventString(sample->@event)!;
            var splitted = eventType.Split(":");
            var eventName = splitted[0];
            var flag = sample->flags;
            var isCall = (flag & (uint)DLFilterFlag.PERF_DLFILTER_FLAG_CALL) != 0;
            var isBranch = (flag & (uint)DLFilterFlag.PERF_DLFILTER_FLAG_BRANCH) != 0;
            var isReturn = (flag & (uint)DLFilterFlag.PERF_DLFILTER_FLAG_RETURN) != 0;

            if (sample->addr_correlates_sym != 0)
            {
                ResolveAddress(state, sample, ctx);
            }

            return 0;
        }
        catch (Exception e)
        {
            state.Writer.WriteLine("Error processing event");
            state.Writer.WriteLine(e);
            state.Writer.Flush();
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "stop")]
    public static int Stop(void* rawState, void* ctx)
    {
        try
        {
            var handle = GCHandle.FromIntPtr((IntPtr)rawState);
            var state = (State)handle.Target!;

            state.Writer.WriteLine("\nTotal events processed: " + state.EventCount);
            state.Dispose();
            handle.Free();
            return 0;
        }
        catch
        {
            return -1;
        }
    }
}