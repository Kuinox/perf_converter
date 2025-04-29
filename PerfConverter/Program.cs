using Dapper;
using Microsoft.Data.Sqlite;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PerfConverter;

[StructLayout(LayoutKind.Sequential)]
public unsafe class PerfDlFilter
{
    [DllImport("PerfConverter", EntryPoint = "get_perf_dlfilter_fns")]
    public static extern unsafe PerfDlfilterFns* get_perf_dlfilter_fns();

    private static SymProcessor _sqlSymProcessor = null!;
    private static IAddressProcessor _addressProcessor = null!;
    private static ITraceProcessor _traceProcessor = null!;
    private static SqliteConnection _sqliteConnection = null!;
    private static int? _maxTracesToProcess = null;
    private class State
    {
        public int EventCount { get; set; }

    }

    private static string? GetEventString(IntPtr eventPtr) => Marshal.PtrToStringUTF8(eventPtr);

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
            _sqliteConnection = new SqliteConnection("Data Source=perf.db");
            _sqliteConnection.Open();
            _sqliteConnection.Execute("PRAGMA journal_mode=OFF;");
            _sqliteConnection.Execute("PRAGMA synchronous=OFF;");
            _sqliteConnection.Execute("PRAGMA locking_mode=EXCLUSIVE;");
            _sqlSymProcessor = SymProcessor.Create(_sqliteConnection);
            _addressProcessor = AddressProcessor.Create(_sqliteConnection, _sqlSymProcessor);
            _traceProcessor = TraceProcessor.Create(_sqliteConnection);

            return 0;
        }
        catch(Exception ex)
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

            // Now id will be the same as sample->id
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
            // Commit any remaining batched data
            _traceProcessor.Close();
            _addressProcessor.Close();
            _sqlSymProcessor.Close();
            _sqliteConnection.Close();
            Console.Error.WriteLine("Done.");
            return 0;
        }
        catch
        {
            return -1;
        }
    }
}