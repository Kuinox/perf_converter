using Parquet;
using PerfConverter.PerfStructs;
using PerfConverter.Schema;
using Perfetto.Protos;
using System.Reflection.Emit;

namespace PerfToPerfetto;

public class Processor
{
    public static async Task<Trace> ProcessAsync(string inputDirectory, string outputFile)
    {
        var fileList = Directory.GetFiles(inputDirectory, "*.parquet", SearchOption.AllDirectories);

        var filePairs = fileList.Where(x => !x.Contains("stackranges")).ToList();

        Console.WriteLine($"Found {filePairs.Count} files to process.");
        var trace = new Trace();
        foreach (var traceFile in filePairs)
        {
            Console.WriteLine($"Processing {traceFile}...");
            var dirName = Path.GetDirectoryName(traceFile)!;
            var threadId = uint.Parse(Path.GetFileName(dirName));
            await foreach (var packet in ProcessFile(traceFile, threadId))
            {
                trace.Packet.Add(packet);
            }
        }
        return trace;
    }

    static async IAsyncEnumerable<TracePacket> ProcessFile(string traceFile, uint threadId)
    {
        var traceSchema = new TraceSampleSchema();

        var first = true;

        using var traceReader = await ParquetReader.CreateAsync(traceFile);
        await foreach (var currentTrace in traceSchema.ReadAll(traceReader))
        {
            if(first)
            {
                first = false;
                yield return new()
                {
                    Timestamp = currentTrace.Time,
                    TrackEvent = new()
                    {
                        TrackUuid = threadId,
                        Name = $"Thread {threadId}"
                    },
                    TrustedPacketSequenceId = threadId
                };
            }

            if (currentTrace.Flags.HasFlag(DLFilterFlag.PERF_DLFILTER_FLAG_CALL))
            {
                yield return new()
                {
                    Timestamp = currentTrace.Time,
                    TrackEvent = new()
                    {
                        Type = TrackEvent.Types.Type.SliceBegin,
                        TrackUuid = threadId,
                    },
                    TrustedPacketSequenceId = threadId
                };
            }
            if (currentTrace.Flags.HasFlag(DLFilterFlag.PERF_DLFILTER_FLAG_RETURN))
            {
                yield return new TracePacket()
                {
                    Timestamp = currentTrace.Time,
                    TrackEvent = new()
                    {
                        Type = TrackEvent.Types.Type.SliceEnd,
                        TrackUuid = threadId
                    },
                    TrustedPacketSequenceId = threadId
                };
            }
        }

    }
}