using PerfConverter.PerfStructs;
using PerfConverter.Persistence;
using PerfConverter.Processor;
using Temp.Core;
using System.Runtime.InteropServices;

namespace PerfConverter;

[StructLayout(LayoutKind.Sequential)]
public unsafe class PerfDlFilter
{
    [DllImport("PerfConverter", EntryPoint = "get_perf_dlfilter_fns")]
    static extern unsafe PerfDlfilterFns* get_perf_dlfilter_fns();

    static ITraceProcessor _traceProcessor = null!;
    static int? _maxTracesToProcess = null;
    static IPersistenceLifetime _persistenceLifetime = null!;

    class State
    {
        public int EventCount { get; set; }
    }

    [UnmanagedCallersOnly(EntryPoint = "start")]
    public static int Start(void** data, void* ctx)
    {
        try
        {
        var state = new State
        {
            EventCount = 0,
        };

            string? maxTracesEnv = Environment.GetEnvironmentVariable("MAX_TRACES_TO_PROCESS");
            if (!string.IsNullOrEmpty(maxTracesEnv) && int.TryParse(maxTracesEnv, out int maxTraces))
            {
                _maxTracesToProcess = maxTraces;
                Console.Error.WriteLine($"Will process maximum of {_maxTracesToProcess} traces");
            }

            *data = (void*)GCHandle.ToIntPtr(GCHandle.Alloc(state));


            _persistenceLifetime = PersistenceFactory.CreatePersistence();

            _traceProcessor = new TraceProcessor(_persistenceLifetime.CreateTraceBatcher);

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
            if (_maxTracesToProcess.HasValue && state.EventCount > _maxTracesToProcess.Value)
            {
                Console.Error.WriteLine($"Reached trace limit of {_maxTracesToProcess.Value}. Exiting early.");
                return -1; // Return negative error code to make perf exit early
            }

            var fns = get_perf_dlfilter_fns();
            var ip = fns->resolve_ip(ctx);
            PerfDlfilterAl* addr = null;
            if (sample->addr_correlates_sym != 0)
            {
                addr = fns->resolve_addr(ctx);
            }
            var id = _traceProcessor.QueueData(sample, ip, addr);

           
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
            return 0;
        }
        catch
        {
            return -1;
        }
    }
}