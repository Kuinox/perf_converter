using IronCompress;
using Parquet;
using Parquet.Data;
using Parquet.Schema;
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
        var reader = await ParquetReader.CreateAsync(tracesPath);
        var inputSchema = new TraceSampleSchema();

        try
        {
            var dropTimes = await AuxDataReader.ReadAuxDataLossAsync(auxPath);

            var totalTraces = GetTotalTraceCount(reader);
            var tid = await GetTraceTID(reader, inputSchema);
            var drops = dropTimes[tid].Order().ToArray();
            var segments = new List<(ulong, ulong)>();
            for (var i = 1; i < drops.Length; i++)
            {
                segments.Add((drops[i - 1], drops[i]));
            }

            var traceStream = inputSchema.ReadAll(reader);
            await ProcessTraceStream(traceStream, dropTimes, totalTraces, outputDir);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing parquet files: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }

    private static async Task ProcessSegment(string outputDir, int segmentId, ulong start, ulong end, ParquetReader reader, TraceSampleSchema schema, Action<ulong> progress)
    {
        var currentTraceSegment = await TraceSegment.CreateAsync(outputDir, segmentId);
        foreach (var rowGroup in reader.RowGroups)
        {
            var stats = rowGroup.GetStatistics(schema.Time.Field)!;
            var isOverlapping = start <= (ulong)stats.MaxValue! && end >= (ulong)stats.MinValue!;
            if (!isOverlapping) continue;

            await foreach (var row in schema.ReadRowGroup(rowGroup))
            {
                currentTraceSegment.Process(row);
                progress(row.Time);
            }
        }
    }

    private static async Task ProcessTraceStream(IAsyncEnumerable<TraceEntry> traceStream, IReadOnlyDictionary<uint, IReadOnlyList<ulong>> dropTimes, long totalTraces, string outputDir)
    {
        //long processed = 0;
        //int lastPercent = -10;

        //var tid = uint.MaxValue;
        //Stack<ulong> drops = null!;
        //int currentSegmentId = 0;

        //var backgroundSaves = new List<Task>();


        //await foreach (var trace in traceStream)
        //{
        //    if (tid == uint.MaxValue)  // Initialize tid on first trace
        //    {
        //        tid = trace.Tid;
        //    }
        //    if (tid != trace.Tid) throw new InvalidDataException($"Trace TID changed from {tid} to {trace.Tid}. This should not happen in a single trace file.");
        //    var smallestTime = drops.Count > 0 ? drops.Peek() : ulong.MaxValue;


        //    if (smallestTime < trace.Time)
        //    {
        //        drops.Pop();
        //        Console.WriteLine($"Trace cut detected at time {trace.Time}.");
        //        backgroundSaves.Add(currentTraceSegment.DisposeAsync().AsTask());

        //        currentSegmentId++;
        //        currentTraceSegment = await TraceSegment.CreateAsync(outputDir, currentSegmentId);
        //    }

        //    currentTraceSegment.Process(trace);


        //    processed++;
        //    UpdateProgress(processed, totalTraces, ref lastPercent);
        //}

        //await currentTraceSegment.DisposeAsync();
        //await Task.WhenAll(backgroundSaves);
        //Console.WriteLine($"Processing complete. Created {currentSegmentId + 1} segment file(s) in {outputDir}");

    }

    private static void UpdateProgress(long processed, long totalTraces, ref int lastPercent)
    {
        if (totalTraces == 0) return;
        int percent = (int)(processed * 100 / totalTraces);
        if (percent >= lastPercent + 1 || processed == totalTraces)
        {
            Console.WriteLine($"Processed {percent}% ({processed}/{totalTraces})");
            lastPercent = percent;
        }
    }

    private static long GetTotalTraceCount(ParquetReader reader)
    {
        long totalTraces = 0;
        for (int i = 0; i < reader.RowGroupCount; i++)
        {
            using var rg = reader.OpenRowGroupReader(i);
            totalTraces += rg.RowCount;
        }
        return totalTraces;
    }

    private static async Task<uint> GetTraceTID(ParquetReader inputReader, TraceSampleSchema inputSchema)
    {

        using (var rowGroupReader = inputReader.OpenRowGroupReader(0))
        {
            var column = await rowGroupReader.ReadColumnAsync(inputSchema.Tid.Field);
            return column.AsSpan<uint>()[0];
        }
    }
}
