using PerfConverter.PerfStructs;
using PerfConverter.Persistence;
using PerfConverter.Persistence.Plank;
using System.Collections.Concurrent;
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

    class State
    {
        public long EventCount { get; set; }
        public long LastReportedCount { get; set; }
        public long LastReportTicks { get; set; } = Environment.TickCount64;
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
            var state = new State { EventCount = 0 };

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

    static ulong _count = 0;

    [UnmanagedCallersOnly(EntryPoint = "filter_event_early")]
    public static int FilterEventEarly(void* rawState, PerfDlFilterSample* sample, void* ctx)
    {
        try
        {
            if ((_count++) % 1000 == 0)
                EntryContentPool.Shared.Tick();

            var handle = GCHandle.FromIntPtr((IntPtr)rawState);
            var state = (State)handle.Target!;
            state.EventCount++;

            // Report progress every 1 second - BEFORE ProcessData which may block
            if (Configuration.EnableProgressSignals)
            {
                long now = Environment.TickCount64;
                long elapsed = now - state.LastReportTicks;
                if (elapsed >= 1000)
                {
                    long eventsInWindow = state.EventCount - state.LastReportedCount;
                    long rate = eventsInWindow * 1000 / elapsed;
                    Console.Error.WriteLine($"PROGRESS:{state.EventCount}|{rate}/s");
                    state.LastReportedCount = state.EventCount;
                    state.LastReportTicks = now;
                }
            }

            var fns = get_perf_dlfilter_fns();
            var ip = fns->resolve_ip(ctx);

            var dso = EntryContentPool.Shared.GetStringFromUtf8Ptr(ip->dso);
            var key = new SrcLineKey(ip->addr, dso);

            //if (!_srcLineCache.TryGetValue(key, out var srcLine))
            //{
            //    var srcFileNamePtr = fns->srcline(ctx, &srcLine.Item2);
            //    srcLine.Item1 = EntryContentPool.Shared.GetStringFromUtf8Ptr(srcFileNamePtr);
            //    _srcLineCache.Add(key, srcLine);
            //}

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
            var state = (State)handle.Target!;
            handle.Free();

            _persistenceLifetime.Dispose();
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
