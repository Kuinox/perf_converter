using IronCompress;
using Parquet;
using Parquet.Data;
using PerfConverter.Entry;
using PerfConverter.PerfStructs;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using Temp.Core;
using Temp.Schema;

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

            int batchSize = 1_000_000;
            string? batchEnv = Environment.GetEnvironmentVariable("BATCH_SIZE");
            if (!string.IsNullOrEmpty(batchEnv) && int.TryParse(batchEnv, out var parsedBatch))
                batchSize = parsedBatch;

            var compressionMethod = CompressionMethod.Snappy;
            string? compressEnv = Environment.GetEnvironmentVariable("PARQUET_COMPRESSION");
            if (!string.IsNullOrEmpty(compressEnv) && Enum.TryParse<CompressionMethod>(compressEnv, true, out var parsedCompress))
                compressionMethod = parsedCompress;

            long processed = 0;
            int lastPercent = -10;

            var tid = uint.MaxValue;
            List<ulong> drops = null!;
            SegmentStack? segmentStack = null;
            int currentSegmentId = 0;
            
            // Create initial persistence and batcher
            var outputFile = Path.Combine(outputDir, $"traces_with_stack_segment_{currentSegmentId}.parquet");
            var persistence = await ParquetTraceWithStackPersistence.Create(outputFile, compressionMethod);
            var batcher = Batcher<TraceWithStackEntry>.Create(persistence, batchSize, BatchingMode.OnFull);
            
            await foreach (var trace in ReadAllTracesAsync(traceReader))
            {
                if (tid == uint.MaxValue)
                {
                    tid = trace.Tid; // Initialize tid on first trace
                    drops = dropTimes[tid];
                    segmentStack = new SegmentStack();
                }
                if(tid != trace.Tid) throw new InvalidDataException($"Trace TID changed from {tid} to {trace.Tid}. This should not happen in a single trace file.");

                var entry = segmentStack!.ProcessTrace(trace, drops);
                
                // Check if we've moved to a new segment (drop occurred)
                if (segmentStack.SegmentId != currentSegmentId)
                {
                    Console.WriteLine($"Trace cut detected at time {trace.Time}. Moving from segment {currentSegmentId} to {segmentStack.SegmentId}.");
                    Console.WriteLine($"Disposing batcher for segment {currentSegmentId}...");
                    
                    // Dispose the current batcher (which also disposes persistence)
                    await batcher.DisposeAsync();
                    Console.WriteLine($"Batcher disposed for segment {currentSegmentId}");
                    
                    // Create new persistence and batcher for the new segment
                    currentSegmentId = segmentStack.SegmentId;
                    outputFile = Path.Combine(outputDir, $"traces_with_stack_segment_{currentSegmentId}.parquet");
                    Console.WriteLine($"Creating new batcher for segment {currentSegmentId}...");
                    persistence = await ParquetTraceWithStackPersistence.Create(outputFile, compressionMethod);
                    batcher = Batcher<TraceWithStackEntry>.Create(persistence, batchSize, BatchingMode.OnFull);
                }
                
                batcher.Persist(entry);

                processed++;
                int percent = (int)(processed * 100 / totalTraces);
                if (percent >= lastPercent + 1 || processed == totalTraces)
                {
                    Console.WriteLine($"Processed {percent}% ({processed}/{totalTraces})");
                    lastPercent = percent;
                }
            }

            await batcher.DisposeAsync();
            Console.WriteLine($"Processing complete. Created {currentSegmentId + 1} segment file(s) in {outputDir}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing parquet files: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
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
