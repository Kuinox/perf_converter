using PerfConverter.PerfStructs;
using PerfConverter.Persistence;
using PerfConverter.Persistence.ParquetDotNet;
using System.Collections.Concurrent;
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
        public int LastReportedCount { get; set; }
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
                LastReportedCount = 0,
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

    record struct SrcLineKey(ulong Addr, string Dso);

    static readonly Dictionary<SrcLineKey, (string, uint)> _srcLineCache = [];

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

            var dso = EntryContentPool.Shared.GetStringFromUtf8Ptr(ip->dso);
            var key = new SrcLineKey(ip->addr, dso);

            (string, uint) srcLine;
            //if (!_srcLineCache.TryGetValue(key, out var srcLine))
            {
                var srcFileNamePtr = fns->srcline(ctx, &srcLine.Item2);
                srcLine.Item1 = EntryContentPool.Shared.GetStringFromUtf8Ptr(srcFileNamePtr);
                //_srcLineCache.Add(key, srcLine);
            }

            PerfDlfilterAl* address = null;
            if (sample->addr_correlates_sym != 0)
            {
                address = fns->resolve_addr(ctx);
            }
            _traceProcessor.ProcessData(sample, ip, address, srcLine.Item1, srcLine.Item2);

            // Report every 1000 events or every 200ms
            var now = DateTime.UtcNow;
            var deltaCount = state.EventCount - state.LastReportedCount;
            var timeSinceLastReport = (now - state.LastReportTime).TotalMilliseconds;

            if (deltaCount >= 1000)
            {
                // Report +1000 for each 1000 events
                var deltaToReport = (deltaCount / 1000) * 1000;
                for (int i = 0; i < deltaToReport / 1000; i++)
                {
                    Console.WriteLine("PROGRESS:+1000");
                }
                state.LastReportedCount += deltaToReport;
                state.LastReportTime = now;
            }
            else if (timeSinceLastReport > 200)
            {
                // Report full count if time elapsed
                Console.Write("PROGRESS:");
                Console.WriteLine(state.EventCount);
                state.LastReportedCount = state.EventCount;
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