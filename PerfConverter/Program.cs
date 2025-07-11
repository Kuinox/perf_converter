using PerfConverter.PerfStructs;
using PerfConverter.Persistence;
using PerfConverter.Persistence.ParquetDotNet;
using System.Runtime.InteropServices;

namespace PerfConverter;

[StructLayout(LayoutKind.Sequential)]
public unsafe class PerfDlFilter
{
    [DllImport("PerfConverter", EntryPoint = "get_perf_dlfilter_fns")]
    static extern unsafe PerfDlfilterFns* get_perf_dlfilter_fns();

    static TraceProcessor _traceProcessor = null!;
    static ParquetPersistenceLifetime _persistenceLifetime = null!;

    class State
    {
        public int EventCount { get; set; }
        public DateTime LastReportTime { get; set; } = DateTime.UtcNow;
        public DateTime LastGcReportTime { get; set; } = DateTime.UtcNow;
        public long LastGen0Count { get; set; }
        public long LastGen1Count { get; set; }
        public long LastGen2Count { get; set; }
        public long LastTotalMemory { get; set; }
    }

    [UnmanagedCallersOnly(EntryPoint = "start")]
    public static int Start(void** data, void* ctx)
    {
        try
        {
            var fns = get_perf_dlfilter_fns();
            int argCount;
            var argsPtr = fns->args(data, &argCount);
            var args = new string[argCount];
            for (var i = 0; i < argCount; i++)
            {
                args[i] = Marshal.PtrToStringUTF8((nint)argsPtr[i])!;
            }
            var state = new State
            {
                EventCount = 0,
                LastReportTime = DateTime.UtcNow
            };

            *data = (void*)GCHandle.ToIntPtr(GCHandle.Alloc(state));

            _persistenceLifetime = PersistenceFactory.CreatePersistence();

            _traceProcessor = new TraceProcessor(_persistenceLifetime.CreateTraceBatcher, _persistenceLifetime.CreateStackRangeBatcher);

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "filter_event_early")]
    public static int FilterEventEarly(void* rawState, PerfDlFilterSample* sample, void* ctx)
    {
        try
        {
            var handle = GCHandle.FromIntPtr((IntPtr)rawState);
            var state = (State)handle.Target!;
            state.EventCount++;
            var fns = get_perf_dlfilter_fns();
            var ip = fns->resolve_ip(ctx);
            PerfDlfilterAl* address = null;
            if (sample->addr_correlates_sym != 0)
            {
                address = fns->resolve_addr(ctx);
            }
            _traceProcessor.QueueData(sample, ip, address);

            var now = DateTime.UtcNow;
            if ((now - state.LastReportTime).TotalMilliseconds > 10)
            {
                Console.WriteLine($"PROGRESS:{state.EventCount}");
                state.LastReportTime = now;
            }
            
            // Report GC statistics every 1 second
            if ((now - state.LastGcReportTime).TotalMilliseconds > 1000)
            {
                var gen0Count = GC.CollectionCount(0);
                var gen1Count = GC.CollectionCount(1);
                var gen2Count = GC.CollectionCount(2);
                var totalMemory = GC.GetTotalMemory(false);
                
                // Check if any GC occurred
                if (gen0Count != state.LastGen0Count || gen1Count != state.LastGen1Count || gen2Count != state.LastGen2Count)
                {
                    Console.WriteLine($"GC_EVENT:Gen0={gen0Count},Gen1={gen1Count},Gen2={gen2Count},Memory={totalMemory}");
                }
                
                // Always report memory statistics
                Console.WriteLine($"MEMORY_STATS:Total={totalMemory},Gen0={gen0Count},Gen1={gen1Count},Gen2={gen2Count}");
                
                state.LastGen0Count = gen0Count;
                state.LastGen1Count = gen1Count;
                state.LastGen2Count = gen2Count;
                state.LastTotalMemory = totalMemory;
                state.LastGcReportTime = now;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Exception in FilterEventEarly: {ex}");
            return -1;
        }
        return 1;
    }

    [UnmanagedCallersOnly(EntryPoint = "stop")]
    public static int Stop(void* rawState, void* ctx)
    {
        try
        {
            var handle = GCHandle.FromIntPtr((IntPtr)rawState);
            var state = (State)handle.Target!;
            handle.Free();

            _persistenceLifetime.DisposeAsync().AsTask().Wait();
            Console.Error.WriteLine("Done.");
            Console.WriteLine("EXIT_MESSAGE");
            return 0;
        }
        catch
        {
            return -1;
        }
    }
}