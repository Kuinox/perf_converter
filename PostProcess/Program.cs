using Parquet;
using Parquet.Data;
using PerfConverter.Entry;
using System.Collections;
using PerfConverter.PerfStructs;
using Temp.Schema;
using System.Diagnostics;

namespace PostProcess;

class Program
{
    static async Task Main(string[] args)
    {
        string basePath = args.Length > 0 ? args[0] : @"C:\Users\Kuinox\Documents\parquet_output";
        string outputPath = args.Length > 1 ? args[1] : Path.Combine(basePath, "processed");

        if (OperatingSystem.IsLinux() && basePath.StartsWith("C:"))
        {
            basePath = "/mnt/c" + basePath.Substring(2).Replace('\\', '/');
        }

        Directory.CreateDirectory(outputPath);

        Console.WriteLine($"Reading parquet files from: {basePath}");

        string tracesPath = Path.Combine(basePath, "18461/18461/tracesamples.parquet");
        string auxPath = Path.Combine(basePath, "aux_events.parquet");

        if (!File.Exists(tracesPath))
        {
            Console.WriteLine($"Trace file not found: {tracesPath}");
            return;
        }

        if (!File.Exists(auxPath))
        {
            Console.WriteLine($"Aux events file not found: {auxPath}");
            return;
        }

        await ProcessAndSplit(tracesPath, auxPath, outputPath);
    }

    static async Task ProcessAndSplit(string tracesPath, string auxPath, string outputDir)
    {
        Console.WriteLine("Processing traces and aux events...");

        try
        {
            var dropTimes = await ReadAuxDataLossAsync(auxPath);

            using var traceReaderCount = await ParquetReader.CreateAsync(File.OpenRead(tracesPath));
            long totalTraces = 0;
            for (int i = 0; i < traceReaderCount.RowGroupCount; i++)
            {
                using var rg = traceReaderCount.OpenRowGroupReader(i);
                totalTraces += rg.RowCount;
            }

            using var traceReader = await ParquetReader.CreateAsync(File.OpenRead(tracesPath));

            var ids = new List<ulong>();
            var perfIds = new List<ulong>();
            var pids = new List<uint>();
            var tids = new List<uint>();
            var times = new List<ulong>();
            var cpus = new List<uint>();
            var flags = new List<uint>();
            var ips = new List<ulong>();
            var addrs = new List<ulong>();
            var periods = new List<ulong>();
            var insnCnts = new List<ulong>();
            var cycCnts = new List<ulong>();
            var weights = new List<ulong>();
            var cpumodes = new List<byte>();
            var addrCorrelates = new List<byte>();
            var eventIds = new List<ulong>();
            var machinePids = new List<uint>();
            var vcpus = new List<uint>();
            var segmentIds = new List<int>();
            var stacks = new List<ulong[]>();

            long processed = 0;
            int lastPercent = -10;

            var stacksByTid = new Dictionary<uint, Stack<ulong>>();
            var dropIndexByTid = new Dictionary<uint, int>();
            var segmentByTid = new Dictionary<uint, int>();

            await foreach (var trace in ReadAllTracesAsync(traceReader))
            {
                var tid = trace.Tid;
                if (!stacksByTid.TryGetValue(tid, out var stack))
                {
                    stack = new Stack<ulong>();
                    stacksByTid[tid] = stack;
                    dropIndexByTid[tid] = 0;
                    segmentByTid[tid] = 0;
                }

                if (dropTimes.TryGetValue(tid, out var drops))
                {
                    var idx = dropIndexByTid[tid];
                    while (idx < drops.Count && trace.Time >= drops[idx])
                    {
                        idx++;
                        stack.Clear();
                        segmentByTid[tid]++;
                    }
                    dropIndexByTid[tid] = idx;
                }

                if (trace.Flags.HasFlag(DLFilterFlag.PERF_DLFILTER_FLAG_CALL))
                {
                    stack.Push(trace.Id);
                }

                ids.Add(trace.Id);
                perfIds.Add(trace.PerfId);
                pids.Add(trace.Pid);
                tids.Add(trace.Tid);
                times.Add(trace.Time);
                cpus.Add(trace.Cpu);
                flags.Add((uint)trace.Flags);
                ips.Add(trace.Ip);
                addrs.Add(trace.Addr);
                periods.Add(trace.Period);
                insnCnts.Add(trace.InsnCnt);
                cycCnts.Add(trace.CycCnt);
                weights.Add(trace.Weight);
                cpumodes.Add(trace.Cpumode);
                addrCorrelates.Add(trace.AddrCorrelatesSym);
                eventIds.Add(trace.EventId);
                machinePids.Add(trace.MachinePid);
                vcpus.Add(trace.Vcpu);
                segmentIds.Add(segmentByTid[tid]);
                stacks.Add(stack.Reverse().ToArray());

                if (trace.Flags.HasFlag(DLFilterFlag.PERF_DLFILTER_FLAG_RETURN) && stack.Count > 0)
                {
                    stack.Pop();
                }

                processed++;
                int percent = (int)(processed * 100 / totalTraces);
                if (percent >= lastPercent + 10 || processed == totalTraces)
                {
                    Console.WriteLine($"Processed {percent}% ({processed}/{totalTraces})");
                    lastPercent = percent;
                }
            }

            var schema = TraceWithStackSchema.Schema;
            var outputFile = Path.Combine(outputDir, "traces_with_stack.parquet");
            using var writer = await ParquetWriter.CreateAsync(schema, File.Create(outputFile));
            using var rowGroup = writer.CreateRowGroup();

            await rowGroup.WriteColumnAsync(new DataColumn(TraceSampleSchema.Id, ids.ToArray()));
            await rowGroup.WriteColumnAsync(new DataColumn(TraceSampleSchema.PerfId, perfIds.ToArray()));
            await rowGroup.WriteColumnAsync(new DataColumn(TraceSampleSchema.Pid, pids.ToArray()));
            await rowGroup.WriteColumnAsync(new DataColumn(TraceSampleSchema.Tid, tids.ToArray()));
            await rowGroup.WriteColumnAsync(new DataColumn(TraceSampleSchema.Time, times.ToArray()));
            await rowGroup.WriteColumnAsync(new DataColumn(TraceSampleSchema.Cpu, cpus.ToArray()));
            await rowGroup.WriteColumnAsync(new DataColumn(TraceSampleSchema.Flags, flags.ToArray()));
            await rowGroup.WriteColumnAsync(new DataColumn(TraceSampleSchema.Ip, ips.ToArray()));
            await rowGroup.WriteColumnAsync(new DataColumn(TraceSampleSchema.Addr, addrs.ToArray()));
            await rowGroup.WriteColumnAsync(new DataColumn(TraceSampleSchema.Period, periods.ToArray()));
            await rowGroup.WriteColumnAsync(new DataColumn(TraceSampleSchema.InsnCnt, insnCnts.ToArray()));
            await rowGroup.WriteColumnAsync(new DataColumn(TraceSampleSchema.CycCnt, cycCnts.ToArray()));
            await rowGroup.WriteColumnAsync(new DataColumn(TraceSampleSchema.Weight, weights.ToArray()));
            await rowGroup.WriteColumnAsync(new DataColumn(TraceSampleSchema.Cpumode, cpumodes.ToArray()));
            await rowGroup.WriteColumnAsync(new DataColumn(TraceSampleSchema.AddrCorrelatesSym, addrCorrelates.ToArray()));
            await rowGroup.WriteColumnAsync(new DataColumn(TraceSampleSchema.EventId, eventIds.ToArray()));
            await rowGroup.WriteColumnAsync(new DataColumn(TraceSampleSchema.MachinePid, machinePids.ToArray()));
            await rowGroup.WriteColumnAsync(new DataColumn(TraceSampleSchema.Vcpu, vcpus.ToArray()));
            await rowGroup.WriteColumnAsync(new DataColumn(TraceWithStackSchema.SegmentId, segmentIds.ToArray()));
            await rowGroup.WriteColumnAsync(new DataColumn(TraceWithStackSchema.Stack, stacks.ToArray()));

            Console.WriteLine($"Wrote processed traces to {outputFile}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing parquet files: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }

    async static ValueTask<(AddressEntry, AddressEntry?)> GetIPAndAddress(IAsyncEnumerator<AddressEntry> addrEnum)
    {
        var ipEntry = addrEnum.Current;
        await addrEnum.MoveNextAsync();
        AddressEntry? addressEntry = default;
        if (!addrEnum.Current.IsIp)
        {
            addressEntry = addrEnum.Current;
            await addrEnum.MoveNextAsync();
        }
        Debug.Assert(ipEntry.IsIp);
        if (addressEntry.HasValue) Debug.Assert(!addressEntry.Value.IsIp);
        return (ipEntry, addressEntry);
    }

    /// <summary>
    /// Read all trace samples from the parquet file.
    /// </summary>
    /// <param name="reader">The parquet reader.</param>
    /// <returns>An async enumerable of trace sample entries.</returns>
    public static async IAsyncEnumerable<TraceSampleEntry> ReadAllTracesAsync(ParquetReader reader)
    {
        for (int i = 0; i < reader.RowGroupCount; i++)
        {
            using var rowGroup = reader.OpenRowGroupReader(i);

            // Use the schema fields from TraceSampleSchema
            var idColumn = await rowGroup.ReadColumnAsync(TraceSampleSchema.Id);
            var perfIdColumn = await rowGroup.ReadColumnAsync(TraceSampleSchema.PerfId);
            var pidColumn = await rowGroup.ReadColumnAsync(TraceSampleSchema.Pid);
            var tidColumn = await rowGroup.ReadColumnAsync(TraceSampleSchema.Tid);
            var timeColumn = await rowGroup.ReadColumnAsync(TraceSampleSchema.Time);
            var cpuColumn = await rowGroup.ReadColumnAsync(TraceSampleSchema.Cpu);
            var flagsColumn = await rowGroup.ReadColumnAsync(TraceSampleSchema.Flags);
            var ipColumn = await rowGroup.ReadColumnAsync(TraceSampleSchema.Ip);
            var addrColumn = await rowGroup.ReadColumnAsync(TraceSampleSchema.Addr);
            var periodColumn = await rowGroup.ReadColumnAsync(TraceSampleSchema.Period);
            var insnCntColumn = await rowGroup.ReadColumnAsync(TraceSampleSchema.InsnCnt);
            var cycCntColumn = await rowGroup.ReadColumnAsync(TraceSampleSchema.CycCnt);
            var weightColumn = await rowGroup.ReadColumnAsync(TraceSampleSchema.Weight);
            var cpumodeColumn = await rowGroup.ReadColumnAsync(TraceSampleSchema.Cpumode);
            var addrCorrelatesSymColumn = await rowGroup.ReadColumnAsync(TraceSampleSchema.AddrCorrelatesSym);
            var eventColumn = await rowGroup.ReadColumnAsync(TraceSampleSchema.EventId);
            var machinePidColumn = await rowGroup.ReadColumnAsync(TraceSampleSchema.MachinePid);
            var vcpuColumn = await rowGroup.ReadColumnAsync(TraceSampleSchema.Vcpu);

            for (int j = 0; j < rowGroup.RowCount; j++)
            {
                yield return new TraceSampleEntry
                {
                    Id = (ulong)((IList)idColumn.Data)[j]!,
                    PerfId = (ulong)((IList)perfIdColumn.Data)[j]!,
                    Pid = (uint)((IList)pidColumn.Data)[j]!,
                    Tid = (uint)((IList)tidColumn.Data)[j]!,
                    Time = (ulong)((IList)timeColumn.Data)[j]!,
                    Cpu = (uint)((IList)cpuColumn.Data)[j]!,
                    Flags = (DLFilterFlag)((IList)flagsColumn.Data)[j]!,
                    Ip = (ulong)((IList)ipColumn.Data)[j]!,
                    Addr = (ulong)((IList)addrColumn.Data)[j]!,
                    Period = (ulong)((IList)periodColumn.Data)[j]!,
                    InsnCnt = (ulong)((IList)insnCntColumn.Data)[j]!,
                    CycCnt = (ulong)((IList)cycCntColumn.Data)[j]!,
                    Weight = (ulong)((IList)weightColumn.Data)[j]!,
                    Cpumode = (byte)((IList)cpumodeColumn.Data)[j]!,
                    AddrCorrelatesSym = (byte)((IList)addrCorrelatesSymColumn.Data)[j]!,
                    EventId = (ulong)((IList)eventColumn.Data)[j]!,
                    MachinePid = (uint)((IList)machinePidColumn.Data)[j]!,
                    Vcpu = (uint)((IList)vcpuColumn.Data)[j]!
                };
            }
        }
    }

    /// <summary>
    /// Read all address entries from the parquet file.
    /// </summary>
    /// <param name="reader">The parquet reader.</param>
    /// <returns>An async enumerable of address entries.</returns>
    public static async IAsyncEnumerable<AddressEntry> ReadAllAddressesAsync(ParquetReader reader)
    {
        for (int i = 0; i < reader.RowGroupCount; i++)
        {
            using var rowGroup = reader.OpenRowGroupReader(i);

            // Use the schema fields from AddressSchema
            var idColumn = await rowGroup.ReadColumnAsync(AddressSchema.Id);
            var traceIdColumn = await rowGroup.ReadColumnAsync(AddressSchema.TraceId);
            var addressColumn = await rowGroup.ReadColumnAsync(AddressSchema.Address);
            var pidColumn = await rowGroup.ReadColumnAsync(AddressSchema.Pid);
            var isIpColumn = await rowGroup.ReadColumnAsync(AddressSchema.IsIp);
            var sizeColumn = await rowGroup.ReadColumnAsync(AddressSchema.Size);
            var symoffColumn = await rowGroup.ReadColumnAsync(AddressSchema.Symoff);
            var symStrIdColumn = await rowGroup.ReadColumnAsync(AddressSchema.SymStrId);
            var symStartColumn = await rowGroup.ReadColumnAsync(AddressSchema.SymStart);
            var symEndColumn = await rowGroup.ReadColumnAsync(AddressSchema.SymEnd);
            var dsoColumn = await rowGroup.ReadColumnAsync(AddressSchema.DsoStrId);
            var symBindingColumn = await rowGroup.ReadColumnAsync(AddressSchema.SymBinding);
            var is64BitColumn = await rowGroup.ReadColumnAsync(AddressSchema.Is64Bit);
            var isKernelIpColumn = await rowGroup.ReadColumnAsync(AddressSchema.IsKernelIp);
            //var buildIdColumn = await rowGroup.ReadColumnAsync(AddressSchema.BuildId);
            var filteredColumn = await rowGroup.ReadColumnAsync(AddressSchema.Filtered);
            var commStrIdColumn = await rowGroup.ReadColumnAsync(AddressSchema.CommStrId);
            var privColumn = await rowGroup.ReadColumnAsync(AddressSchema.Priv);

            // Yield each row
            for (int j = 0; j < rowGroup.RowCount; j++)
            {
                yield return new AddressEntry
                {
                    Id = (ulong)((IList)idColumn.Data)[j]!,
                    TraceId = (ulong)((IList)traceIdColumn.Data)[j]!,
                    Address = (ulong)((IList)addressColumn.Data)[j]!,
                    Pid = (uint)((IList)pidColumn.Data)[j]!,
                    IsIp = (bool)((IList)isIpColumn.Data)[j]!,
                    Size = (uint)((IList)sizeColumn.Data)[j]!,
                    Symoff = (uint)((IList)symoffColumn.Data)[j]!,
                    SymStrId = (ulong)((IList)symStrIdColumn.Data)[j]!,
                    SymStart = (ulong)((IList)symStartColumn.Data)[j]!,
                    SymEnd = (ulong)((IList)symEndColumn.Data)[j]!,
                    DsoStrId = (ulong)((IList)dsoColumn.Data)[j]!,
                    SymBinding = (byte)((IList)symBindingColumn.Data)[j]!,
                    Is64Bit = (byte)((IList)is64BitColumn.Data)[j]!,
                    IsKernelIp = (byte)((IList)isKernelIpColumn.Data)[j]!,
                    //BuildId = (byte[])((IList)buildIdColumn.Data)[j]!,
                    Filtered = (byte)((IList)filteredColumn.Data)[j]!,
                    CommStrId = (ulong)((IList)commStrIdColumn.Data)[j]!,
                    Priv = (ulong)((IList)privColumn.Data)[j]!
                };
            }
        }
    }

    /// <summary>
    /// Read all dictionary entries from a parquet file and return them as a dictionary.
    /// </summary>
    /// <param name="reader">The parquet reader.</param>
    /// <returns>A dictionary mapping string IDs to string values.</returns>
    public static async Task<Dictionary<ulong, string>> ReadDictAsync(ParquetReader reader)
    {
        var dict = new Dictionary<ulong, string>();
        for (int i = 0; i < reader.RowGroupCount; i++)
        {
            using var rowGroup = reader.OpenRowGroupReader(i);
            var idColumn = await rowGroup.ReadColumnAsync(DictionarySchema.Id);
            var symbolColumn = await rowGroup.ReadColumnAsync(DictionarySchema.Symbol);
            for (int j = 0; j < rowGroup.RowCount; j++)
            {
                dict.Add((ulong)((IList)idColumn.Data)[j]!, (string)((IList)symbolColumn.Data)[j]!);
            }
        }
        return dict;
    }
    public static async Task<Dictionary<uint, List<ulong>>> ReadAuxDataLossAsync(string auxPath)
    {
        var dict = new Dictionary<uint, List<ulong>>();
        using var reader = await ParquetReader.CreateAsync(File.OpenRead(auxPath));
        for (int i = 0; i < reader.RowGroupCount; i++)
        {
            using var rowGroup = reader.OpenRowGroupReader(i);
            var timeColumn = await rowGroup.ReadColumnAsync(AuxDataLostSchema.Time);
            var tidColumn = await rowGroup.ReadColumnAsync(AuxDataLostSchema.Tid);
            var flagsColumn = await rowGroup.ReadColumnAsync(AuxDataLostSchema.Flags);
            for (int j = 0; j < rowGroup.RowCount; j++)
            {
                var flags = (ulong)((IList)flagsColumn.Data)[j]!;
                if (flags == 0) continue; // only keep drops
                var tid = (uint)(ulong)((IList)tidColumn.Data)[j]!;
                var time = (ulong)((IList)timeColumn.Data)[j]!;
                if (!dict.TryGetValue(tid, out var list))
                {
                    list = new List<ulong>();
                    dict[tid] = list;
                }
                list.Add(time);
            }
        }
        foreach (var list in dict.Values)
        {
            list.Sort();
        }
        return dict;
    }
}
