using PerfConverter.PerfStructs;
using Perfetto.Protos;
using System.Diagnostics;
using Temp.Schema;
using Temp.Schema.Schema;

namespace PerfToPerfetto;

public class Processor : FileVisitor
{
    public Perfetto.Protos.Trace Trace { get; } = new();
    readonly TraceSampleSchema _traceSchema = new();

    uint _trackId = 2;

    int? _pid;
    public override Task VisitRoot(string path)
    {
        var dirName = Path.GetFileName(path);
        if (dirName.StartsWith("pid="))
        {
            _pid = int.Parse(dirName[4..]);
            Trace.Packet.Add(new TracePacket
            {
                TrackDescriptor = new TrackDescriptor
                {
                    Uuid = (ulong)_pid.Value,
                    Process = new ProcessDescriptor
                    {
                        Pid = _pid.Value,
                        ProcessName = $"Process {_pid.Value}"
                    }
                }
            });
        }
        return base.VisitRoot(path);
    }

    public override async Task VisitTracks(string[] paths)
    {
        Console.WriteLine($"Found {paths.Length} tracks to process.");
        await base.VisitTracks(paths);
    }


    (TrackDescriptor trackDescriptor, List<string> threadComms)? _threadNameState;
    public override async Task VisitTrack(string path)
    {
        var dirName = Path.GetFileName(path)!;
        bool isThread = dirName.StartsWith("tid=");

        var trackDescriptor = new TrackDescriptor
        {
            Uuid = _trackId,
        };

        if (_pid.HasValue)
        {
            trackDescriptor.Thread ??= new();
            trackDescriptor.Thread.Pid = _pid.Value;
        }

        if (!isThread)
        {
            trackDescriptor.Name = dirName;
        }
        else
        {
            _threadNameState = (trackDescriptor, []);

            var tid = int.Parse(dirName[4..]);
            trackDescriptor.Thread ??= new();
            trackDescriptor.Thread.Tid = tid;
        }

        Trace.Packet.Add(new TracePacket
        {
            TrackDescriptor = trackDescriptor,
        });

        await base.VisitTrack(path);

        _trackId++;
        _threadNameState = null;
    }

    public override async Task VisitFile(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        if (fileName != "branches")
            return;
        Console.WriteLine($"Processing {path}...");
        await ProcessFile(path);
        Console.WriteLine($"Done. Calls: {calls}, Returns: {returns}");
        calls = 0;
        returns = 0;
    }
    int calls = 0;
    int returns = 0;
    async Task ProcessFile(string traceFile)
    {
        await foreach (var currentTrace in _traceSchema.ReadAll(traceFile))
        {
            var currComm = currentTrace.IpComm ?? currentTrace.AddressComm;
            if (_threadNameState.HasValue && currComm != null)
            {
                var (trackEvent, threadComms) = _threadNameState.Value;
                var lastComm = threadComms.LastOrDefault();
                if (lastComm != currComm)
                {
                    threadComms.Add(currComm);
                    trackEvent.Name = string.Join(" => ", threadComms);
                }
            }
            string name;
            if (!string.IsNullOrWhiteSpace(currentTrace.SourceFileName))
                name = currentTrace.SourceFileName + ":" + currentTrace.SourceLineNumber;
            else
                name = currentTrace.IpSym ?? currentTrace.AddressSym ?? currentTrace.Event ?? "Unknown";

            if (currentTrace.Flags.HasFlag(DLFilterFlag.PERF_DLFILTER_FLAG_CALL))
            {
                calls++;
                Trace.Packet.Add(new TracePacket()
                {
                    Timestamp = currentTrace.Time,
                    TrackEvent = new()
                    {
                        Type = TrackEvent.Types.Type.SliceBegin,
                        TrackUuid = _trackId,
                        Name = name
                    },
                    TrustedPacketSequenceId = _trackId
                });
            }
            if (currentTrace.Flags.HasFlag(DLFilterFlag.PERF_DLFILTER_FLAG_RETURN))
            {
                returns++;
                Trace.Packet.Add(new TracePacket()
                {
                    Timestamp = currentTrace.Time,
                    TrackEvent = new()
                    {
                        Type = TrackEvent.Types.Type.SliceEnd,
                        TrackUuid = _trackId
                    },
                    TrustedPacketSequenceId = _trackId
                });
            }
        }
    }
}