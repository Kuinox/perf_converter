using Dapper;
using Microsoft.Data.Sqlite;
using PerfConverter.Entry;
using PerfConverter.Persistance;
using PerfConverter.Persistance.Sql;
using PerfConverter.Processor;
using System.Runtime.InteropServices;

namespace PerfConverter;

[StructLayout(LayoutKind.Sequential)]
public unsafe class PerfDlFilter
{
    [DllImport("PerfConverter", EntryPoint = "get_perf_dlfilter_fns")]
    public static extern unsafe PerfDlfilterFns* get_perf_dlfilter_fns();

    static ISymProcessor _sqlSymProcessor = null!;
    static IAddressProcessor _addressProcessor = null!;
    static ITraceProcessor _traceProcessor = null!;
    static SqliteConnection _sqliteConnection = null!;
    static int? _maxTracesToProcess = null;
    static Batcher<SymbolEntry> _symbolBatcher = null!;
    static Batcher<AddressEntry> _addressBatcher = null!;
    static Batcher<TraceSampleEntry> _traceBatcher = null!;

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

            var batchSize = 1_000_000;

             var sqlSymbolPersister = SqlSymPersistance.Create(_sqliteConnection);
            var addressPersister = SqlAddressPersistance.Create(_sqliteConnection);
            var tracePersister = SqlTracePersistance.Create(_sqliteConnection);
            _symbolBatcher = Batcher<SymbolEntry>.Create(sqlSymbolPersister, batchSize);
            _addressBatcher = Batcher<AddressEntry>.Create(addressPersister, batchSize);
            _traceBatcher = Batcher<TraceSampleEntry>.Create(tracePersister, batchSize);


            _sqlSymProcessor = new SymProcessor(_symbolBatcher);
            _addressProcessor = new AddressProcessor(_sqlSymProcessor, _addressBatcher);
            _traceProcessor = new TraceProcessor(_traceBatcher);

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
            _traceBatcher.Dispose();
            _addressBatcher.Dispose();
            _symbolBatcher.Dispose();
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