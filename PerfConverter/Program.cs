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
    static ElfSymbolResolver _symbolResolver = null!;

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

            // Initialize symbol resolver by loading all ELF files
            _symbolResolver = new ElfSymbolResolver();
            var elfFiles = new List<string>();

            // Load JIT ELF files from ~/.debug/
            var debugDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".debug");
            if (Directory.Exists(debugDir))
            {
                foreach (var elfPath in Directory.GetFiles(debugDir, "elf", SearchOption.AllDirectories))
                {
                    elfFiles.Add(elfPath);
                }
            }

            // Load JIT .so files from /tmp/
            if (Directory.Exists("/tmp"))
            {
                foreach (var soPath in Directory.GetFiles("/tmp", "jitted-*.so"))
                {
                    elfFiles.Add(soPath);
                }
            }

            Console.Error.WriteLine($"Found {elfFiles.Count} ELF files to load symbols from");
            _symbolResolver.LoadSymbols(elfFiles.ToArray());

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

            // Resolve IP symbol using our own ELF parser (avoids perf's addr2line deadlock)
            var ipAddr = (ulong)sample->ip;
            var ipSymbol = _symbolResolver.Resolve(ipAddr);

            var ipStruct = new PerfDlfilterAl
            {
                addr = ipAddr,
                sym = ipSymbol.HasValue ? (nint)Marshal.StringToHGlobalAnsi(ipSymbol.Value.Name) : 0,
                dso = ipSymbol.HasValue ? (nint)Marshal.StringToHGlobalAnsi(ipSymbol.Value.Dso) : 0,
                sym_start = ipSymbol?.Start ?? 0,
                sym_end = ipSymbol?.End ?? 0,
            };
            PerfDlfilterAl* ip = &ipStruct;

            // Resolve address symbol if present
            PerfDlfilterAl* address = null;
            if (sample->addr_correlates_sym != 0)
            {
                var addrAddr = (ulong)sample->addr;
                var addrSymbol = _symbolResolver.Resolve(addrAddr);

                var addressStruct = new PerfDlfilterAl
                {
                    addr = addrAddr,
                    sym = addrSymbol.HasValue ? (nint)Marshal.StringToHGlobalAnsi(addrSymbol.Value.Name) : 0,
                    dso = addrSymbol.HasValue ? (nint)Marshal.StringToHGlobalAnsi(addrSymbol.Value.Dso) : 0,
                    sym_start = addrSymbol?.Start ?? 0,
                    sym_end = addrSymbol?.End ?? 0,
                };
                address = &addressStruct;
            }

            _traceProcessor.ProcessData(sample, ip, address, null, 0);

            // Free allocated strings
            if (ipStruct.sym != 0) Marshal.FreeHGlobal(ipStruct.sym);
            if (ipStruct.dso != 0) Marshal.FreeHGlobal(ipStruct.dso);
            if (address != null)
            {
                if (address->sym != 0) Marshal.FreeHGlobal(address->sym);
                if (address->dso != 0) Marshal.FreeHGlobal(address->dso);
            }

            // Report every 1000 events or every 200ms
            var now = DateTime.UtcNow;
            var deltaCount = state.EventCount - state.LastReportedCount;
            var timeSinceLastReport = (now - state.LastReportTime).TotalMilliseconds;

            if (Configuration.EnableProgressSignals)
            {
                if (deltaCount >= 1000)
                {
                    // Report +1000 for each 1000 events
                    var deltaToReport = (deltaCount / 1000) * 1000;
                    for (int i = 0; i < deltaToReport / 1000; i++)
                    {
                        Console.Error.WriteLine("PROGRESS:+1000");
                    }
                    state.LastReportedCount += deltaToReport;
                    state.LastReportTime = now;
                }
                else if (timeSinceLastReport > 200)
                {
                    // Report full count if time elapsed
                    Console.Error.Write("PROGRESS:");
                    Console.Error.WriteLine(state.EventCount);
                    state.LastReportedCount = state.EventCount;
                    state.LastReportTime = now;
                }
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