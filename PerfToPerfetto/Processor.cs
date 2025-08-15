using Parquet;
using PerfConverter.Entry;
using PerfConverter.Schema;
using System.Diagnostics;
using System.Reflection.Emit;

namespace PerfToPerfetto;

public class Processor
{
    readonly TraceSampleSchema _traceSchema = new();
    readonly StackRangeSchema _stackRangeSchema = new();

    public async Task ProcessAsync(string inputDirectory, string outputFile)
    {
        var processor = new TraceProcessor();
        processor.Start();

        var fileList = Directory.GetFiles(inputDirectory, "*.parquet", SearchOption.AllDirectories);

        var filePairs = fileList
            .Where(x => !x.Contains("stackranges"))
            .Select(x => x.Replace("segment", "").Replace(".parquet", ""))
            .OrderBy(x => int.Parse(Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(x)))))
            .ThenBy(x => int.Parse(Path.GetFileName(Path.GetDirectoryName(x))))
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
            await ProcessPair(processor, traceFile, stackRangeFile);
        }
    }

    async Task ProcessPair(TraceProcessor processor, string traceFile, string stackRangeFile)
    {
        using var traceReader = await ParquetReader.CreateAsync(traceFile);
        using var stackRangeReader = await ParquetReader.CreateAsync(stackRangeFile);

        var traceEnumerator = _traceSchema.ReadAll(traceReader).GetAsyncEnumerator();
        var stackEnumerator = _stackRangeSchema.ReadAll(stackRangeReader).GetAsyncEnumerator();


        var stacks = new Stack<StackRange>();
        if (!await stackEnumerator.MoveNextAsync()) throw new InvalidOperationException("No stack ranges found in the file.");
        var currentStackRange = null as StackRange?;

        async ValueTask NextStackRange()
        {
            if (!await stackEnumerator.MoveNextAsync())
            {
                currentStackRange = null;
                return;
            }
            currentStackRange = stackEnumerator.Current;
        }

        await NextStackRange();


        while (true)
        {
            if (!await traceEnumerator.MoveNextAsync())
                break;
            var currentTrace = traceEnumerator.Current;

            if (currentStackRange.HasValue)
            {
                if (currentTrace.Id >= currentStackRange.Value.StartTrace)
                {
                    stacks.Push(stackEnumerator.Current);
                    processor.PushFrame(stacks, currentTrace);
                    await NextStackRange();
                }
                else if (currentTrace.Id >= currentStackRange.Value.EndTrace)
                {
                    if (currentStackRange.Value.EndTrace == 0)
                        processor.PopUnknownFrame(stacks, currentTrace);
                    else
                        processor.PopFrame(stacks, currentTrace);

                    stacks.Pop();
                }
            }

        }

    }
}