using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PerfConverter;

/// <summary>
/// Main class that implements the perf dlfilter interface
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe class PerfDlFilter
{
    [DllImport("PerfConverter", EntryPoint = "get_perf_dlfilter_fns")]
    public static extern unsafe PerfDlfilterFns* get_perf_dlfilter_fns();

    // Unmanaged exports for perf dlfilter
    [UnmanagedCallersOnly(EntryPoint = "start")]
    public static int Start(void** data, void* ctx)
    {
        try
        {
            return PerfTraceConverter.Instance.Start(data, ctx);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error in Start: {ex.Message}");
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "filter_event_early")]
    public static int FilterEventEarly(void* rawState, PerfDlFilterSample* sample, void* ctx)
    {
        try
        {
            return PerfTraceConverter.Instance.FilterEventEarly(rawState, sample, ctx);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error in FilterEventEarly: {ex.Message}");
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "stop")]
    public static int Stop(void* rawState, void* ctx)
    {
        try
        {
            return PerfTraceConverter.Instance.Stop(rawState, ctx);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error in Stop: {ex.Message}");
            return -1;
        }
    }
}
