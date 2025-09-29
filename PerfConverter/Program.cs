using PerfConverter.PerfStructs;
using PerfConverter.Persistence;
using PerfConverter.Persistence.ParquetDotNet;
using System.Runtime.InteropServices;
using Temp.Schema.Entry;

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
    }

    [UnmanagedCallersOnly(EntryPoint = "start")]
    public static int Start(void** data, void* ctx)
    {
        try
        {
            Console.WriteLine("DOTNET_READY");

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
            uint lineNumber = 0;
            var srcFileNamePtr = fns->srcline(ctx, &lineNumber);
            var srcFileName = EntryContentPool.Shared.GetStringFromUtf8Ptr(srcFileNamePtr);
            PerfDlfilterAl* address = null;
            if (sample->addr_correlates_sym != 0)
            {
                address = fns->resolve_addr(ctx);
            }
            _traceProcessor.ProcessData(sample, ip, address, srcFileName, lineNumber);

            var now = DateTime.UtcNow;
            if ((now - state.LastReportTime).TotalMilliseconds > 50)
            {
                Console.WriteLine($"PROGRESS:{state.EventCount}");
                state.LastReportTime = now;
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
            Console.Error.WriteLine("EXIT_MESSAGE");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Exception in Stop: {ex}");
            return -1;
        }
    }
}