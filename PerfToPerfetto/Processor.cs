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
            await ProcessPair(processor, traceFile, stackRangeFile);
        }
    }

    static IEnumerable<StackRange> EnumerateStartRange(IAsyncEnumerable<StackRange> ranges) => ranges.ToBlockingEnumerable().OrderBy(x => x.StartTrace);

    async Task ProcessPair(TraceProcessor processor, string traceFile, string stackRangeFile)
    {
        using var traceReader = await ParquetReader.CreateAsync(traceFile);
        using var stackRangeReader = await ParquetReader.CreateAsync(stackRangeFile);

        var traceEnumerator = _traceSchema.ReadAll(traceReader).GetAsyncEnumerator();
        var endStackEnumerator = _stackRangeSchema.ReadAll(stackRangeReader).GetAsyncEnumerator();
        var startStackEnumerator = EnumerateStartRange(_stackRangeSchema.ReadAll(stackRangeReader))
            .Where(x => x.StartTrace != 0) // 0 means we dont have the event that created this stack, since this enumerator is interested in things that open the stacks, we can ignore stacks which dont have an event that create it.
            .GetEnumerator();
        var unorderedStackRanges = _stackRangeSchema.ReadAll(stackRangeReader).ToBlockingEnumerable().ToArray();
        var firstFileTrace = traceEnumerator.Current;
        var stacks = new Stack<(TraceEntry, StackRange)>();
        if (!await endStackEnumerator.MoveNextAsync()) throw new InvalidOperationException("No stack ranges found in the file.");
        if (!startStackEnumerator.MoveNext()) throw new InvalidOperationException("No stack ranges found in the file.");
        var currentEndStackRange = endStackEnumerator.Current as StackRange?;

        async ValueTask NextEndStackRange()
        {
            if (!await endStackEnumerator.MoveNextAsync())
            {
                currentEndStackRange = null;
                return;
            }
            currentEndStackRange = endStackEnumerator.Current;
        }

        var currentStartStackRange = startStackEnumerator.Current as StackRange?;

        void NextStartStackRange()
        {
            if (!startStackEnumerator.MoveNext())
            {
                currentStartStackRange = null;
                return;
            }
            currentStartStackRange = startStackEnumerator.Current;
        }

        while (true)
        {
            var previousTrace = traceEnumerator.Current;

            if (!await traceEnumerator.MoveNextAsync())
                break;
            var currentTrace = traceEnumerator.Current;

            // Notify the processor about each trace so it can accumulate instruction/cycle counts
            processor.ProcessTrace(currentTrace);

            if (currentStartStackRange.HasValue)
            {
                var currentRange = currentStartStackRange.Value;

                if (currentTrace.Id == currentRange.StartTrace)
                {
                    stacks.Push((currentTrace, currentRange));
                    processor.PushFrame(currentTrace);
                    NextStartStackRange();
                }
            }
            if (currentEndStackRange.HasValue)
            {
                var currentRange = currentEndStackRange.Value;
                if (currentTrace.Id == currentRange.EndTrace)
                {
                    if (currentRange.StartTrace == 0)
                    {
                        processor.PopUnknownFrame(firstFileTrace, currentTrace);
                    }
                    else
                    {
                        var pushTrace = stacks.Pop();
                        if (pushTrace.Item2.EndTrace != currentTrace.Id) throw new InvalidOperationException("bug");
                        processor.PopFrame(pushTrace.Item1, currentTrace);
                    }
                    await NextEndStackRange();
                }
            }

        }

    }
}