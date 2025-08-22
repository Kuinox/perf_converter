using System.Diagnostics;
using System.Reflection.Emit;

namespace PerfToPerfetto;

public class Processor
{
    public static async Task ProcessAsync(string inputDirectory, string outputFile)
    {
        var processor = new TraceProcessor();
        processor.Start();

        var fileList = Directory.GetFiles(inputDirectory, "*.parquet", SearchOption.AllDirectories);

        var filePairs = fileList
            .Where(x => !x.Contains("stackranges"))
            .Select(x => x.Replace("segment", "").Replace(".parquet", ""))
            .OrderBy(x => int.Parse(Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(x)))!))
            .ThenBy(x => int.Parse(Path.GetFileName(Path.GetDirectoryName(x))!))
            .ThenBy(x => int.Parse(Path.GetFileName(x)))
            .Select(x =>
            {
                var id = Path.GetFileName(x);
                var dir = Path.GetDirectoryName(x)!;
                var traceFile = Path.Combine(dir, $"segment{id}.parquet");
                var stackRangeFile = Path.Combine(dir, $"segment{id}_stackranges.parquet");
                return (traceFile, stackRangeFile);
            })
            .ToList();



        Console.WriteLine($"Found {filePairs.Count} pair of files to process");

        foreach ((string traceFile, string stackRangeFile) in filePairs)
        {
            Console.WriteLine($"Processing {traceFile} & {stackRangeFile}...");
            var segmentProcessor = new SegmentProcessor(processor, traceFile, stackRangeFile);
            await segmentProcessor.ProcessAsync();
        }
    }
}