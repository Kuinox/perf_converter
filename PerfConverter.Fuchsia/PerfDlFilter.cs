using System.Runtime.InteropServices;

namespace PerfConverter.Fuchsia;

/// <summary>
/// P/Invoke access to the native perf dlfilter functions
/// </summary>
public static unsafe class PerfDlFilter
{
    /// <summary>
    /// Gets the perf_dlfilter_fns struct from the native library
    /// </summary>
    [DllImport("PerfConverter")]
    public static extern PerfDlFilterFns* get_perf_dlfilter_fns();
}

/// <summary>
/// Corresponds to the native perf_dlfilter_fns struct
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct PerfDlFilterFns
{
    public delegate* unmanaged<void*, PerfDlFilterAddress*> resolve_ip;
    public delegate* unmanaged<void*, PerfDlFilterAddress*> resolve_addr;
}

/// <summary>
/// Corresponds to the native perf_dlfilter_al struct
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct PerfDlFilterAddress
{
    public IntPtr sym;
    public IntPtr dso;
    public ulong addr;
    public byte filtered;
}