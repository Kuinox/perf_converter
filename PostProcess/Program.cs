using IronCompress;
using Parquet;
using Parquet.Data;
using PerfConverter.Entry;
using PerfConverter.PerfStructs;
using PerfConverter.Persistence.ParquetDotNet;
using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
        Directory.Delete(outputPath, true);
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
        var inputReader = await ParquetReader.CreateAsync(tracesPath);
        var inputSchema = new TraceSampleSchema();

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

            long processed = 0;
            int lastPercent = -10;

            var tid = uint.MaxValue;
            Stack<ulong> drops = null!;
            int currentSegmentId = 0;

            // Create initial persistence and batcher
            var batcher = await CreateBatcher(outputDir, batchSize, currentSegmentId);
            var backgroundSaves = new List<Task>();
            await foreach (var trace in inputSchema.ReadAll(inputReader))
            {
                if (tid == uint.MaxValue)  // Initialize tid on first trace
                {
                    tid = trace.Tid;
                    drops = new(dropTimes[tid].OrderDescending());
                }
                if (tid != trace.Tid) throw new InvalidDataException($"Trace TID changed from {tid} to {trace.Tid}. This should not happen in a single trace file.");
                var smallestTime = drops.Peek();


                if (smallestTime > trace.Time)
                {
                    drops.Pop();
                    var savingSegmentId = currentSegmentId;
                    currentSegmentId++;
                    Console.WriteLine($"Trace cut detected at time {trace.Time}.");
                    Console.WriteLine($"Saving segment {savingSegmentId} in background...");
                    var task = batcher.DisposeAsync().AsTask().ContinueWith(t =>
                    {
                        Console.WriteLine($"Done saving segment {savingSegmentId}");
                    });
                    backgroundSaves.Add(task);

                    batcher = await CreateBatcher(outputDir, batchSize, currentSegmentId);
                }

                batcher.Persist(trace);


                processed++;
                int percent = (int)(processed * 100 / totalTraces);
                if (percent >= lastPercent + 1 || processed == totalTraces)
                {
                    Console.WriteLine($"Processed {percent}% ({processed}/{totalTraces})");
                    lastPercent = percent;
                }
            }

            await batcher.DisposeAsync();
            await Task.WhenAll(backgroundSaves);
            Console.WriteLine($"Processing complete. Created {currentSegmentId + 1} segment file(s) in {outputDir}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing parquet files: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }

    static bool _init;
    public static CompressionMethod CompressionMethod
    {
        get
        {
            if (_init) return field;
            _init = true;
            field = CompressionMethod.Snappy;
            string? compressEnv = Environment.GetEnvironmentVariable("PARQUET_COMPRESSION");
            if (!string.IsNullOrEmpty(compressEnv) && Enum.TryParse<CompressionMethod>(compressEnv, true, out var parsedCompress))
                field = parsedCompress;
            return field;
        }
    }

    private static async Task<Batcher<TraceEntry>> CreateBatcher(string outputDir, int batchSize, int currentSegmentId)
    {
        var outputFile = Path.Combine(outputDir, $"traces_with_stack_segment_{currentSegmentId}.parquet");
        var persistence = await ParquetTracePersistence.Create(outputFile, CompressionMethod);
        var batcher = Batcher<TraceEntry>.Create(persistence, batchSize, BatchingMode.OnFull);
        return batcher;
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
