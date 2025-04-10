using Microsoft.Data.Sqlite;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PerfConverter;

[StructLayout(LayoutKind.Sequential)]
public unsafe class PerfDlFilter
{
    [DllImport("PerfConverter", EntryPoint = "get_perf_dlfilter_fns")]
    public static extern unsafe PerfDlfilterFns* get_perf_dlfilter_fns();

    private static IAddressProcessor _addressProcessor = null!;
    private static ITraceProcessor _traceProcessor = null!;

    private class State
    {
        public int EventCount { get; set; }
        public HashSet<string> Events { get; set; } = null!;

    }

    private static string? GetEventString(IntPtr eventPtr) => Marshal.PtrToStringUTF8(eventPtr);

    [UnmanagedCallersOnly(EntryPoint = "start")]
    public static int Start(void** data, void* ctx)
    {
        try
        {
            var state = new State
            {
                EventCount = 0,
                Events = [],
            };

            *data = (void*)GCHandle.ToIntPtr(GCHandle.Alloc(state));
            var sqliteConnection = new SqliteConnection("Data Source=perf.db");
            sqliteConnection.Open();
            _addressProcessor = SqlAddressProcessor.Create(sqliteConnection);
            _traceProcessor = SqlTraceProcessor.Create(sqliteConnection);

            return 0;
        }
        catch
        {
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
            (_traceProcessor as SqlTraceProcessor)?.Close();
            (_addressProcessor as SqlAddressProcessor)?.Close();

            handle.Free();
            return 0;
        }
        catch
        {
            return -1;
        }
    }
}