using Parquet;
using PerfConverter.Schema;
using System.Diagnostics;

namespace PerfToPerfetto;

public class Processor
{
    readonly TraceSampleSchema _traceSchema = new();
    readonly StackRangeSchema _stackRangeSchema = new();

    public async Task ProcessAsync(string inputDirectory, string outputFile)
    {
        var processor = new TraceProcessor();

        var fileList = Directory.GetFiles(inputDirectory, "*.parquet", SearchOption.AllDirectories);

        var filePairs = fileList
            .Where(x => !x.Contains("stackranges"))
            .Select(x => x.Replace("segment", "").Replace(".parquet", ""))
            .Select(x =>
            {
                var id = Path.GetFileName(x);
                var dir = Path.GetDirectoryName(x)!;
                var traceFile = Path.Combine(dir, $"{id}.parquet");
                var stackRangeFile = Path.Combine(dir, $"stackranges_{id}.parquet");
                return (traceFile, stackRangeFile);
            })
            .ToList();



        Console.WriteLine($"Found {filePairs.Count} pair of files to process");

        foreach ((string traceFile, string stackRangeFile) in filePairs)
        {
            Console.WriteLine($"Processing {traceFile} & {stackRangeFile}...");
            await ProcessPair(traceFile, stackRangeFile);
        }

        processor.Flush();
    }

    async Task ProcessPair(string traceFile, string stackRangeFile)
    {
        var processor = new TraceProcessor();
        using var traceReader = await ParquetReader.CreateAsync(traceFile);
        using var stackRangeReader = await ParquetReader.CreateAsync(stackRangeFile);

        var traceEnumerator = _traceSchema.ReadAll(traceReader).GetAsyncEnumerator();
        var stackEnumerator = _stackRangeSchema.ReadAll(stackRangeReader).GetAsyncEnumerator();

        if(!await stackEnumerator.MoveNextAsync()) throw new InvalidOperationException("No stack ranges found in the file.");

        while(true)
        {
            if(!await traceEnumerator.MoveNextAsync())
                break;
            var currentTrace = traceEnumerator.Current;
            
        }

    }
}