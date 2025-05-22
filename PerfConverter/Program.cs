using PerfConverter.PerfStructs;
using PerfConverter.Persistence;
using PerfConverter.Processor;
using System.Runtime.InteropServices;

namespace PerfConverter;

[StructLayout(LayoutKind.Sequential)]
public unsafe class PerfDlFilter
{
    [DllImport("PerfConverter", EntryPoint = "get_perf_dlfilter_fns")]
    static extern unsafe PerfDlfilterFns* get_perf_dlfilter_fns();

    static IStringProcessor _symProcessor = null!;
    static IStringProcessor _commProcessor = null!;
    static IStringProcessor _eventProcessor = null!;
    static IAddressProcessor _addressProcessor = null!;
    static ITraceProcessor _traceProcessor = null!;
    static int? _maxTracesToProcess = null;
    static IPersistenceLifetime _persistenceLifetime = null!;

    class State
    {
        public int EventCount { get; set; }
    }

    static string? GetEventString(IntPtr eventPtr) => Marshal.PtrToStringUTF8(eventPtr);

    [UnmanagedCallersOnly(EntryPoint = "start")]
    public static int Start(void** data, void* ctx)
    {
        try
        {
            Console.SetError(new WrappingWriter(Console.Error));
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

            _symProcessor = new StringProcessor(_persistenceLifetime.SymbolBatcher);
            _commProcessor = new StringProcessor(_persistenceLifetime.CommBatcher);
            _eventProcessor = new StringProcessor(_persistenceLifetime.EventBatcher);
            _addressProcessor = new AddressProcessor(_symProcessor, _commProcessor, _persistenceLifetime.AddressBatcher);
            _traceProcessor = new TraceProcessor(_eventProcessor, _persistenceLifetime.CreateTraceBatcher);

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

            var id = _traceProcessor.FilterEventEarly(sample);
            var fns = get_perf_dlfilter_fns();

            _addressProcessor.ProcessIp(fns, id, sample->pid, ctx);
            if (sample->addr_correlates_sym != 0)
            {
                _addressProcessor.ProcessAddress(fns, id, sample->pid, ctx);
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
            
            // Dispose the persistence lifetime which will handle all cleanup
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