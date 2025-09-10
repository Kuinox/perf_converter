using Parquet;
using PerfConverter.PerfStructs;
using PerfConverter.Schema;
using Perfetto.Protos;
using System.Diagnostics;
using System.Reflection.Emit;
using Temp.Schema;

namespace PerfToPerfetto;

public class Processor : Visitor
{
    public Perfetto.Protos.Trace Trace { get; } = new();
    readonly TraceSampleSchema _traceSchema = new();

    uint _trackId = 1;
    uint _sequenceId = 0;
    public override async Task VisitTracks(string[] paths)
    {
        Console.WriteLine($"Found {paths.Length} files to process.");
        await base.VisitTracks(paths);
        _trackId++;
        _sequenceId = 0;
    }


    (TrackEvent trackEvent, List<string> threadComms)? _threadNameState;
    public override async Task VisitTrack(string path)
    {
        var dirName = Path.GetFileName(Path.GetDirectoryName(path))!;
        bool isThread = dirName.StartsWith("tid=");

        var trackEvent = new TrackEvent { TrackUuid = _trackId };
        if (!isThread)
        {
            trackEvent.Name = dirName;
        }
        else
        {
            _threadNameState = (trackEvent, []);
        }

        Trace.Packet.Add(new TracePacket { TrackEvent = trackEvent });

        await base.VisitTrack(path);

        _threadNameState = null;
    }

    public override async Task VisitSegment(string segmentFile)
    {
        Console.WriteLine($"Processing {segmentFile}...");
        await ProcessFile(segmentFile);
    }

    async Task ProcessFile(string traceFile)
    {
        using var traceReader = await ParquetReader.CreateAsync(traceFile);
        await foreach (var currentTrace in _traceSchema.ReadAll(traceReader))
        {
            _sequenceId++;
            var currComm = currentTrace.IpComm ?? currentTrace.AddressComm;
            if (_threadNameState.HasValue && currComm != null)
            {
                var (trackEvent, threadComms) = _threadNameState.Value;
                var lastComm = threadComms.Last();
                if (lastComm != currComm)
                {
                    threadComms.Add(lastComm);
                    trackEvent.Name = string.Join(" => ", threadComms);
                }
            }

            if (currentTrace.Flags.HasFlag(DLFilterFlag.PERF_DLFILTER_FLAG_CALL))
            {
                Trace.Packet.Add(new TracePacket()
                {
                    Timestamp = currentTrace.Time,
                    TrackEvent = new()
                    {
                        Type = TrackEvent.Types.Type.SliceBegin,
                        TrackUuid = _trackId,
                    },
                    TrustedPacketSequenceId = _sequenceId
                });
            }
            if (currentTrace.Flags.HasFlag(DLFilterFlag.PERF_DLFILTER_FLAG_RETURN))
            {
                Trace.Packet.Add(new TracePacket()
                {
                    Timestamp = currentTrace.Time,
                    TrackEvent = new()
                    {
                        Type = TrackEvent.Types.Type.SliceEnd,
                        TrackUuid = _trackId
                    },
                    TrustedPacketSequenceId = _sequenceId
                });
            }
        }
    }
}