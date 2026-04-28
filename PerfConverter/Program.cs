using PerfConverter.PerfStructs;
using PerfConverter.Persistence;
using PerfConverter.Persistence.Plank;
using System.Runtime.InteropServices;
using Temp.Schema.Entry;

namespace PerfConverter;

[StructLayout(LayoutKind.Sequential)]
public unsafe class PerfDlFilter
{
    // This symbol is exported by the current NativeAOT shared library.
    [DllImport("__Internal", EntryPoint = "get_perf_dlfilter_fns")]
    static extern unsafe PerfDlfilterFns* get_perf_dlfilter_fns();

    static TraceProcessor _traceProcessor = null!;
    static ParquetPersistenceLifetime _persistenceLifetime = null!;
    static MetricsPipeReporter? _metricsPipeReporter;

    class State
    {
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
            var state = new State();

            *data = (void*)GCHandle.ToIntPtr(GCHandle.Alloc(state));

            _metricsPipeReporter = MetricsPipeReporter.TryStart();
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

    static ulong _count = 0;

    [UnmanagedCallersOnly(EntryPoint = "filter_event_early")]
    public static int FilterEventEarly(void* rawState, PerfDlFilterSample* sample, void* ctx)
    {
        try
        {
            if ((_count++) % 1000 == 0)
                EntryContentPool.Shared.Tick();

            var handle = GCHandle.FromIntPtr((IntPtr)rawState);
            _ = (State)handle.Target!;
            PerfConverterMetrics.IncrementProcessedEvents();

            var fns = get_perf_dlfilter_fns();
            var ip = fns->resolve_ip(ctx);

            PerfDlfilterAl* address = null;
            if (sample->addr_correlates_sym != 0)
            {
                address = fns->resolve_addr(ctx);
            }
            _traceProcessor.ProcessData(sample, ip, address, null, 0);

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
            _ = (State)handle.Target!;
            handle.Free();

            _metricsPipeReporter?.Dispose();
            _metricsPipeReporter = null;
            _persistenceLifetime.Dispose();
            EntryContentPool.Shared.Dispose();
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
