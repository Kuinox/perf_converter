using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;

namespace PerfConverter;

/// <summary>
/// Main class that implements the perf dlfilter interface
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe class PerfDlFilter
{
    static ITraceProcessor TraceProcessor = null!;
    static IAddressProcessor AddressProcessor = null!;
    static SqliteConnection SharedConnection = null!;

    [DllImport("PerfConverter", EntryPoint = "get_perf_dlfilter_fns")]
    public static extern unsafe PerfDlfilterFns* get_perf_dlfilter_fns();

    [UnmanagedCallersOnly(EntryPoint = "start")]
    public static int Start(void** data, void* ctx)
    {
        try
        {
            var fns = get_perf_dlfilter_fns();
            int argCount = 0;
            var argsAddresses = fns->args(ctx, &argCount);
            var args = new string[argCount];
            for (int i = 0; i < argCount; i++)
            {
                var argPtr = Marshal.ReadIntPtr(argsAddresses, i * IntPtr.Size);
                args[i] = Marshal.PtrToStringUTF8(argPtr) ?? string.Empty;
            }

            var filename = args.Length > 0 ? args[0] : "out.db";
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = filename,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString();

            SharedConnection = new SqliteConnection(connectionString);
            SharedConnection.Open();

            AddressProcessor = SqlAddressProcessor.Create(SharedConnection);
            TraceProcessor = SqlTraceProcessor.Create(SharedConnection);

            return 0;
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
            var fns = get_perf_dlfilter_fns();
            if (sample->addr_correlates_sym != 0)
            {
                AddressProcessor.HandleAdress(fns, sample->pid, sample->addr);
            }
            AddressProcessor.ResolveIp(fns, sample->pid, sample->ip);

            TraceProcessor.FilterEventEarly(sample);

            return 0;
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
            (AddressProcessor as IDisposable)?.Dispose();
            (TraceProcessor as IDisposable)?.Dispose();
            SharedConnection?.Dispose();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error in Stop: {ex.Message}");
            return -1;
        }
    }
}
