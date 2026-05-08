using System.Text;
using Google.Protobuf;
using Perfetto.Protos;
using Plank.Reading;
using Plank.Schema;
using Temp.Schema.Schema;

namespace PerfToPerfetto;

public sealed class Processor : IDisposable
{
    const byte TracePacketFieldTag = 10;
    const int MaxThreadNameParts = 16;

    readonly CodedOutputStream _output;
    readonly Dictionary<ulong, SourceLocationInfo> _sourceLocations = [];
    readonly HashSet<ulong> _writtenProcessTracks = [];
    bool _disposed;

    public Processor(Stream output)
    {
        _output = new CodedOutputStream(output, leaveOpen: true);
    }

    public Task ProcessAsync(string inputDirectory)
    {
        LoadSourceLocations(inputDirectory);

        var stackFrameFile = FindStackFrameFile(inputDirectory)
            ?? throw new FileNotFoundException(
                "Could not find stack_frames.parquet. Run StackFixer before PerfToPerfetto.",
                Path.Combine(inputDirectory, "stack_frames.parquet"));

        var threadInfos = DiscoverThreadInfos(inputDirectory);
        foreach (var info in threadInfos.Values.OrderBy(static info => info.Pid).ThenBy(static info => info.Tid))
        {
            WriteProcessTrack(info.Pid);
            WriteThreadTrack(info.Pid, info.Tid, info.TrackUuid, info.ThreadName);
        }

        foreach (var group in StackFrameParquetReader.ReadRows(stackFrameFile).GroupBy(static row => row.Tid).OrderBy(static group => group.Key))
        {
            if (!threadInfos.TryGetValue(group.Key, out var info))
            {
                info = new ThreadInfo(0, group.Key, [], ThreadTrackUuid(0, group.Key), $"Thread {group.Key}");
                WriteProcessTrack(info.Pid);
                WriteThreadTrack(info.Pid, info.Tid, info.TrackUuid, info.ThreadName);
            }

            Console.WriteLine($"Writing Perfetto stack slices for tid={group.Key}...");
            WriteStackFrames(info.TrackUuid, group);
        }

        _output.Flush();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _output.Flush();
        _output.Dispose();
        _disposed = true;
    }

    void WriteStackFrames(ulong trackUuid, IEnumerable<StackFrameRow> frames)
    {
        var endpoints = new List<StackFrameEndpoint>();
        foreach (var frame in frames)
        {
            endpoints.Add(new StackFrameEndpoint(frame.StartTime, frame.StartTrace, frame.Depth, IsBegin: true, frame));
            endpoints.Add(new StackFrameEndpoint(frame.EndTime, frame.EndTrace, frame.Depth, IsBegin: false, frame));
        }

        endpoints.Sort(StackFrameEndpointComparer.Instance);

        foreach (var endpoint in endpoints)
        {
            WriteTrackEvent(
                endpoint.Frame,
                trackUuid,
                endpoint.IsBegin ? TrackEvent.Types.Type.SliceBegin : TrackEvent.Types.Type.SliceEnd);
        }
    }

    void WriteProcessTrack(uint pid)
    {
        var trackUuid = ProcessTrackUuid(pid);
        if (!_writtenProcessTracks.Add(trackUuid))
            return;

        WritePacket(new TracePacket
        {
            TrackDescriptor = new TrackDescriptor
            {
                Uuid = trackUuid,
                Process = new ProcessDescriptor
                {
                    Pid = (int)pid,
                    ProcessName = pid == 0 ? "Unknown process" : $"Process {pid}"
                }
            }
        });
    }

    void WriteThreadTrack(uint pid, uint tid, ulong trackUuid, string threadName)
    {
        WritePacket(new TracePacket
        {
            TrackDescriptor = new TrackDescriptor
            {
                Uuid = trackUuid,
                ParentUuid = ProcessTrackUuid(pid),
                Name = threadName,
                Thread = new ThreadDescriptor
                {
                    Pid = (int)pid,
                    Tid = (int)tid,
                    ThreadName = threadName
                }
            }
        });
    }

    void WriteTrackEvent(StackFrameRow row, ulong trackUuid, TrackEvent.Types.Type type)
    {
        var trackEvent = new TrackEvent
        {
            Type = type,
            TrackUuid = trackUuid
        };

        if (type == TrackEvent.Types.Type.SliceBegin)
        {
            trackEvent.Name = GetEventName(row.LocationId);
            if (GetSourceLocation(row.LocationId) is { } sourceLocation)
                trackEvent.SourceLocation = sourceLocation;
        }

        WritePacket(new TracePacket
        {
            Timestamp = type == TrackEvent.Types.Type.SliceBegin ? row.StartTime : row.EndTime,
            TrackEvent = trackEvent,
            TrustedPacketSequenceId = checked((uint)(trackUuid & uint.MaxValue))
        });
    }

    void WritePacket(TracePacket packet)
    {
        _output.WriteRawTag(TracePacketFieldTag);
        _output.WriteMessage(packet);
    }

    SourceLocation? GetSourceLocation(ulong locationId)
    {
        if (locationId != 0 && _sourceLocations.TryGetValue(locationId, out var location))
            return location.ToPerfettoSourceLocation();

        return null;
    }

    string GetEventName(ulong locationId)
    {
        if (locationId != 0 && _sourceLocations.TryGetValue(locationId, out var location))
        {
            var name = location.GetDisplayName();
            if (!string.IsNullOrEmpty(name))
                return name;
        }

        return locationId == 0 ? "Unknown" : $"location:{locationId}";
    }

    void LoadSourceLocations(string inputDirectory)
    {
        var sourceLocationFile = FindSourceLocationFile(inputDirectory);
        if (sourceLocationFile is null)
            return;

        foreach (var location in SourceLocationParquetReader.ReadRows(sourceLocationFile))
            _sourceLocations[location.Id] = location;
    }

    static string? FindSourceLocationFile(string inputDirectory)
    {
        var path = Path.Combine(inputDirectory, "source_locations.parquet");
        if (File.Exists(path))
            return path;

        var directory = new DirectoryInfo(inputDirectory);
        while (directory.Parent is not null)
        {
            path = Path.Combine(directory.Parent.FullName, "source_locations.parquet");
            if (File.Exists(path))
                return path;
            directory = directory.Parent;
        }

        return null;
    }

    static string? FindStackFrameFile(string inputDirectory)
    {
        var path = Path.Combine(inputDirectory, "stack_frames.parquet");
        if (File.Exists(path))
            return path;

        var directory = new DirectoryInfo(inputDirectory);
        while (directory.Parent is not null)
        {
            path = Path.Combine(directory.Parent.FullName, "stack_frames.parquet");
            if (File.Exists(path))
                return path;
            directory = directory.Parent;
        }

        return null;
    }

    Dictionary<uint, ThreadInfo> DiscoverThreadInfos(string inputDirectory)
    {
        var result = new Dictionary<uint, ThreadInfo>();

        foreach (var group in Directory.EnumerateFiles(inputDirectory, "branches.parquet", SearchOption.AllDirectories)
                     .Select(path => new
                     {
                         Path = path,
                         Tid = TryParsePrefixedUInt(new DirectoryInfo(Path.GetDirectoryName(path)!).Name, "tid="),
                         Pid = TryParsePrefixedUInt(new DirectoryInfo(Path.GetDirectoryName(path)!).Parent?.Name, "pid=")
                     })
                     .Where(static item => item.Tid.HasValue)
                     .GroupBy(static item => item.Tid!.Value)
                     .OrderBy(static group => group.Key))
        {
            var branchFiles = group.Select(static item => item.Path).Order(StringComparer.Ordinal).ToArray();
            var pid = group.Select(static item => item.Pid).FirstOrDefault(static pid => pid.HasValue) ?? 0;
            var tid = group.Key;
            var threadName = BuildThreadName(tid, branchFiles);
            result[tid] = new ThreadInfo(pid, tid, branchFiles, ThreadTrackUuid(pid, tid), threadName);
        }

        return result;
    }

    string BuildThreadName(uint tid, IReadOnlyList<string> traceFiles)
    {
        var comms = new List<string>();
        foreach (var traceFile in traceFiles)
        {
            foreach (var (ipComm, addressComm) in TraceParquetReader.TryReadComms(traceFile))
            {
                var comm = BytesToString(ipComm) ?? BytesToString(addressComm);
                if (string.IsNullOrEmpty(comm) || comms.LastOrDefault() == comm)
                    continue;

                comms.Add(comm);
                if (comms.Count == MaxThreadNameParts)
                    return string.Join(" -> ", comms);
            }
        }

        return comms.Count == 0 ? $"Thread {tid}" : string.Join(" -> ", comms);
    }

    static uint? TryParsePrefixedUInt(string? value, string prefix)
    {
        if (value is null || !value.StartsWith(prefix, StringComparison.Ordinal) ||
            !uint.TryParse(value[prefix.Length..], out var parsed))
        {
            return null;
        }

        return parsed;
    }

    static ulong ProcessTrackUuid(uint pid)
        => 0x1000_0000_0000_0000UL | pid;

    static ulong ThreadTrackUuid(uint pid, uint tid)
        => 0x2000_0000_0000_0000UL | ((ulong)pid << 32) | tid;

    static string? BytesToString(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 })
            return null;

        return Encoding.UTF8.GetString(bytes);
    }

    static T[] ReadColumn<T>(RowGroupReader rowGroup, Column column)
    {
        var values = new List<T>();
        foreach (var page in rowGroup.Column<T>(column).Pages)
        {
            var span = page.Values.Span;
            for (var i = 0; i < span.Length; i++)
                values.Add(span[i]);
        }

        return [.. values];
    }

    static void ValidateRowGroupLengths(string path, int expected, params Array[] values)
    {
        foreach (var value in values)
            ValidateLength(path, expected, value.Length);
    }

    static void ValidateLength(string path, int expected, int actual)
    {
        if (actual != expected)
            throw new InvalidDataException($"Parquet column length mismatch in '{path}'. Expected {expected}, got {actual}.");
    }

    readonly record struct ThreadInfo(uint Pid, uint Tid, IReadOnlyList<string> BranchFiles, ulong TrackUuid, string ThreadName);

    readonly record struct StackFrameRow(
        ulong FrameId,
        uint Tid,
        uint Depth,
        ulong StartTime,
        ulong EndTime,
        ulong StartTrace,
        ulong EndTrace,
        ulong LocationId);

    public readonly record struct SliceEndpoint(ulong Time, ulong Trace, uint Depth, ulong FrameId, bool IsBegin);

    readonly record struct StackFrameEndpoint(ulong Time, ulong Trace, uint Depth, bool IsBegin, StackFrameRow Frame)
    {
        public SliceEndpoint ToSliceEndpoint()
            => new(Time, Trace, Depth, Frame.FrameId, IsBegin);
    }

    public sealed class SliceEndpointComparer : IComparer<SliceEndpoint>
    {
        public static SliceEndpointComparer Instance { get; } = new();

        public int Compare(SliceEndpoint left, SliceEndpoint right)
        {
            var compare = left.Time.CompareTo(right.Time);
            if (compare != 0)
                return compare;

            compare = left.Trace.CompareTo(right.Trace);
            if (compare != 0)
                return compare;

            if (left.FrameId == right.FrameId && left.IsBegin != right.IsBegin)
                return left.IsBegin ? -1 : 1;

            if (left.IsBegin != right.IsBegin)
                return left.IsBegin ? 1 : -1;

            if (left.IsBegin)
            {
                compare = left.Depth.CompareTo(right.Depth);
                if (compare != 0)
                    return compare;
            }
            else
            {
                compare = right.Depth.CompareTo(left.Depth);
                if (compare != 0)
                    return compare;
            }

            return left.FrameId.CompareTo(right.FrameId);
        }
    }

    sealed class StackFrameEndpointComparer : IComparer<StackFrameEndpoint>
    {
        public static StackFrameEndpointComparer Instance { get; } = new();

        public int Compare(StackFrameEndpoint left, StackFrameEndpoint right)
            => SliceEndpointComparer.Instance.Compare(left.ToSliceEndpoint(), right.ToSliceEndpoint());
    }

    sealed record SourceLocationInfo(
        ulong Id,
        string? Dso,
        ulong RelativeAddress,
        string? Symbol,
        string? SourceFileName,
        uint SourceLineNumber)
    {
        public SourceLocation? ToPerfettoSourceLocation()
        {
            if (string.IsNullOrEmpty(Symbol) && string.IsNullOrEmpty(SourceFileName))
                return null;

            var sourceLocation = new SourceLocation();
            if (!string.IsNullOrEmpty(Symbol))
                sourceLocation.FunctionName = Symbol;
            if (!string.IsNullOrEmpty(SourceFileName))
                sourceLocation.FileName = SourceFileName;
            if (SourceLineNumber != 0)
                sourceLocation.LineNumber = SourceLineNumber;

            return sourceLocation;
        }

        public string GetDisplayName()
        {
            if (!string.IsNullOrEmpty(Symbol))
                return Symbol;

            if (!string.IsNullOrEmpty(SourceFileName) && SourceLineNumber != 0)
                return $"{Path.GetFileName(SourceFileName)}:{SourceLineNumber}";

            if (!string.IsNullOrEmpty(Dso))
                return $"{Path.GetFileName(Dso)}+0x{RelativeAddress:x}";

            return RelativeAddress == 0 ? string.Empty : $"0x{RelativeAddress:x}";
        }
    }

    static class StackFrameParquetReader
    {
        public static IEnumerable<StackFrameRow> ReadRows(string path)
        {
            using var stream = File.OpenRead(path);
            using var reader = StackFrameRowSchema.Schema.CreateReader(stream);

            foreach (var token in reader.EnumerateRowGroups())
            {
                using var rowGroup = reader.OpenRowGroup(stream, token);
                var frameIds = ReadColumn<ulong>(rowGroup, StackFrameRowSchema.Schema.Columns[0]);
                var tids = ReadColumn<uint>(rowGroup, StackFrameRowSchema.Schema.Columns[1]);
                var depths = ReadColumn<uint>(rowGroup, StackFrameRowSchema.Schema.Columns[2]);
                var startTimes = ReadColumn<ulong>(rowGroup, StackFrameRowSchema.Schema.Columns[3]);
                var endTimes = ReadColumn<ulong>(rowGroup, StackFrameRowSchema.Schema.Columns[4]);
                var startTraces = ReadColumn<ulong>(rowGroup, StackFrameRowSchema.Schema.Columns[5]);
                var endTraces = ReadColumn<ulong>(rowGroup, StackFrameRowSchema.Schema.Columns[6]);
                var locationIds = ReadColumn<ulong>(rowGroup, StackFrameRowSchema.Schema.Columns[7]);

                ValidateRowGroupLengths(path, frameIds.Length, tids, depths, startTimes, endTimes, startTraces, endTraces, locationIds);

                for (var i = 0; i < frameIds.Length; i++)
                {
                    yield return new StackFrameRow(
                        FrameId: frameIds[i],
                        Tid: tids[i],
                        Depth: depths[i],
                        StartTime: startTimes[i],
                        EndTime: endTimes[i],
                        StartTrace: startTraces[i],
                        EndTrace: endTraces[i],
                        LocationId: locationIds[i]);
                }
            }
        }
    }

    static class TraceParquetReader
    {
        public static IEnumerable<(byte[]? IpComm, byte[]? AddressComm)> TryReadComms(string path)
        {
            using var stream = File.OpenRead(path);
            using var reader = TraceSampleRowSchema.Schema.CreateReader(stream);

            foreach (var token in reader.EnumerateRowGroups())
            {
                using var rowGroup = reader.OpenRowGroup(stream, token);
                var ids = ReadColumn<ulong>(rowGroup, TraceSampleRowSchema.Schema.Columns[0]);
                var ipComms = ReadOptionalColumn<byte[]>(rowGroup, TraceSampleRowSchema.Schema.Columns[32], ids.Length);
                var addressComms = ReadOptionalColumn<byte[]>(rowGroup, TraceSampleRowSchema.Schema.Columns[44], ids.Length);

                for (var i = 0; i < ids.Length; i++)
                    yield return (ipComms[i], addressComms[i]);
            }
        }

        static T?[] ReadOptionalColumn<T>(RowGroupReader rowGroup, Column column, int expected)
            where T : class
        {
            var values = ReadColumn<T>(rowGroup, column);
            if (values.Length == expected)
                return values;

            if (values.Length == 0)
                return new T?[expected];

            return new T?[expected];
        }
    }

    static class SourceLocationParquetReader
    {
        public static IEnumerable<SourceLocationInfo> ReadRows(string path)
        {
            using var stream = File.OpenRead(path);
            using var reader = SourceLocationRowSchema.Schema.CreateReader(stream);

            foreach (var token in reader.EnumerateRowGroups())
            {
                using var rowGroup = reader.OpenRowGroup(stream, token);
                var ids = ReadColumn<ulong>(rowGroup, SourceLocationRowSchema.Schema.Columns[0]);
                var dsos = ReadColumn<byte[]>(rowGroup, SourceLocationRowSchema.Schema.Columns[2]);
                var relativeAddresses = ReadColumn<ulong>(rowGroup, SourceLocationRowSchema.Schema.Columns[3]);
                var symbols = ReadColumn<byte[]>(rowGroup, SourceLocationRowSchema.Schema.Columns[4]);
                var sourceFiles = ReadColumn<byte[]>(rowGroup, SourceLocationRowSchema.Schema.Columns[8]);
                var sourceLines = ReadColumn<uint>(rowGroup, SourceLocationRowSchema.Schema.Columns[9]);

                ValidateRowGroupLengths(path, ids.Length, dsos, relativeAddresses, symbols, sourceFiles, sourceLines);

                for (var i = 0; i < ids.Length; i++)
                {
                    yield return new SourceLocationInfo(
                        Id: ids[i],
                        Dso: BytesToString(dsos[i]),
                        RelativeAddress: relativeAddresses[i],
                        Symbol: BytesToString(symbols[i]),
                        SourceFileName: BytesToString(sourceFiles[i]),
                        SourceLineNumber: sourceLines[i]);
                }
            }
        }
    }
}
