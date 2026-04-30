using System.Text;
using Google.Protobuf;
using PerfConverter.PerfStructs;
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

    public async Task ProcessAsync(string inputDirectory)
    {
        LoadSourceLocations(inputDirectory);

        var pidDirectories = GetPidDirectories(inputDirectory);
        foreach (var pidDirectory in pidDirectories)
            await ProcessPidDirectoryAsync(pidDirectory).ConfigureAwait(false);

        _output.Flush();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _output.Flush();
        _output.Dispose();
        _disposed = true;
    }

    async Task ProcessPidDirectoryAsync(string pidDirectory)
    {
        var pid = ParsePrefixedUInt(Path.GetFileName(pidDirectory), "pid=");
        WriteProcessTrack(pid);

        var threadDirectories = Directory.EnumerateDirectories(pidDirectory, "tid=*")
            .OrderBy(static path => ParsePrefixedUInt(Path.GetFileName(path), "tid="))
            .ToArray();

        foreach (var threadDirectory in threadDirectories)
            await ProcessThreadDirectoryAsync(pid, threadDirectory).ConfigureAwait(false);
    }

    async Task ProcessThreadDirectoryAsync(uint pid, string threadDirectory)
    {
        var tid = ParsePrefixedUInt(Path.GetFileName(threadDirectory), "tid=");
        var branchesFiles = Directory.EnumerateFiles(threadDirectory, "branches.parquet")
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (branchesFiles.Length == 0)
            return;

        var trackUuid = ThreadTrackUuid(pid, tid);
        var threadName = BuildThreadName(tid, branchesFiles);
        WriteThreadTrack(pid, tid, trackUuid, threadName);

        var state = new ThreadEventState(trackUuid);
        foreach (var branchesFile in branchesFiles)
        {
            Console.WriteLine($"Processing {branchesFile}...");
            await ProcessBranchesFileAsync(state, branchesFile).ConfigureAwait(false);
        }

        state.CloseOpenSlices(this);
        Console.WriteLine($"Done tid={tid}. Calls: {state.Calls}, Returns: {state.Returns}, Skipped returns: {state.SkippedReturns}");
    }

    Task ProcessBranchesFileAsync(ThreadEventState state, string traceFile)
    {
        foreach (var row in TraceParquetReader.ReadRows(traceFile))
        {
            state.LastTimestamp = row.Time;

            var flags = row.Flags;
            if ((flags & (uint)DLFilterFlag.PERF_DLFILTER_FLAG_CALL) != 0)
            {
                state.Calls++;
                state.OpenDepth++;
                WriteTrackEvent(row, state.TrackUuid, TrackEvent.Types.Type.SliceBegin);
            }

            if ((flags & (uint)DLFilterFlag.PERF_DLFILTER_FLAG_RETURN) != 0)
            {
                state.Returns++;
                if (state.OpenDepth == 0)
                {
                    state.SkippedReturns++;
                    continue;
                }

                state.OpenDepth--;
                WriteTrackEvent(row, state.TrackUuid, TrackEvent.Types.Type.SliceEnd);
            }
        }

        return Task.CompletedTask;
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
                    ProcessName = $"Process {pid}"
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

    void WriteTrackEvent(TraceRow row, ulong trackUuid, TrackEvent.Types.Type type)
    {
        var trackEvent = new TrackEvent
        {
            Type = type,
            TrackUuid = trackUuid
        };

        if (type == TrackEvent.Types.Type.SliceBegin)
        {
            trackEvent.Name = GetEventName(row);
            if (GetBestSourceLocation(row) is { } sourceLocation)
                trackEvent.SourceLocation = sourceLocation;
        }

        WritePacket(new TracePacket
        {
            Timestamp = row.Time,
            TrackEvent = trackEvent,
            TrustedPacketSequenceId = checked((uint)(trackUuid & uint.MaxValue))
        });
    }

    void WritePacket(TracePacket packet)
    {
        _output.WriteRawTag(TracePacketFieldTag);
        _output.WriteMessage(packet);
    }

    SourceLocation? GetBestSourceLocation(TraceRow row)
    {
        if (row.AddressLocationId != 0 && _sourceLocations.TryGetValue(row.AddressLocationId, out var addressLocation))
            return addressLocation.ToPerfettoSourceLocation();

        if (row.IpLocationId != 0 && _sourceLocations.TryGetValue(row.IpLocationId, out var ipLocation))
            return ipLocation.ToPerfettoSourceLocation();

        return null;
    }

    string GetEventName(TraceRow row)
    {
        if (TryGetLocationName(row.AddressLocationId, out var addressLocationName))
            return addressLocationName;

        var addressSymbol = BytesToString(row.AddressSym);
        if (!string.IsNullOrEmpty(addressSymbol))
            return addressSymbol;

        if (row.Addr != 0)
            return $"target@0x{row.Addr:x}";

        if (TryGetLocationName(row.IpLocationId, out var ipLocationName))
            return ipLocationName;

        var ipSymbol = BytesToString(row.IpSym);
        if (!string.IsNullOrEmpty(ipSymbol))
            return ipSymbol;

        if (row.Ip != 0)
            return $"ip@0x{row.Ip:x}";

        return BytesToString(row.Event) ?? "Unknown";
    }

    bool TryGetLocationName(ulong locationId, out string name)
    {
        if (locationId != 0 && _sourceLocations.TryGetValue(locationId, out var location))
        {
            name = location.GetDisplayName();
            return !string.IsNullOrEmpty(name);
        }

        name = string.Empty;
        return false;
    }

    string BuildThreadName(uint tid, IReadOnlyList<string> traceFiles)
    {
        var comms = new List<string>();
        foreach (var traceFile in traceFiles)
        {
            foreach (var (ipComm, addressComm) in TraceParquetReader.ReadComms(traceFile))
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
        if (directory.Name.StartsWith("pid=", StringComparison.Ordinal) && directory.Parent is not null)
        {
            path = Path.Combine(directory.Parent.FullName, "source_locations.parquet");
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    static string[] GetPidDirectories(string inputDirectory)
    {
        var directory = new DirectoryInfo(inputDirectory);
        if (directory.Name.StartsWith("pid=", StringComparison.Ordinal))
            return [directory.FullName];

        return Directory.EnumerateDirectories(inputDirectory, "pid=*")
            .OrderBy(static path => ParsePrefixedUInt(Path.GetFileName(path), "pid="))
            .ToArray();
    }

    static uint ParsePrefixedUInt(string? value, string prefix)
    {
        if (value is null || !value.StartsWith(prefix, StringComparison.Ordinal) ||
            !uint.TryParse(value[prefix.Length..], out var parsed))
        {
            throw new InvalidDataException($"Expected directory name '{prefix}<number>', got '{value}'.");
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

    readonly record struct TraceRow(
        ulong Id,
        uint Pid,
        uint Tid,
        ulong Time,
        uint Flags,
        ulong Ip,
        ulong IpLocationId,
        ulong Addr,
        ulong AddressLocationId,
        byte[] Event,
        byte[]? IpSym,
        byte[]? IpComm,
        byte[]? AddressSym,
        byte[]? AddressComm);

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

    sealed class ThreadEventState(ulong trackUuid)
    {
        public ulong TrackUuid { get; } = trackUuid;
        public int OpenDepth { get; set; }
        public int Calls { get; set; }
        public int Returns { get; set; }
        public int SkippedReturns { get; set; }
        public ulong LastTimestamp { get; set; }

        public void CloseOpenSlices(Processor processor)
        {
            while (OpenDepth > 0)
            {
                OpenDepth--;
                processor.WritePacket(new TracePacket
                {
                    Timestamp = LastTimestamp,
                    TrackEvent = new TrackEvent
                    {
                        Type = TrackEvent.Types.Type.SliceEnd,
                        TrackUuid = TrackUuid
                    },
                    TrustedPacketSequenceId = checked((uint)(TrackUuid & uint.MaxValue))
                });
            }
        }
    }

    static class TraceParquetReader
    {
        public static IEnumerable<(byte[]? IpComm, byte[]? AddressComm)> ReadComms(string path)
        {
            using var stream = File.OpenRead(path);
            using var reader = TraceSampleRowSchema.Schema.CreateReader(stream);

            foreach (var token in reader.EnumerateRowGroups())
            {
                using var rowGroup = reader.OpenRowGroup(stream, token);
                var ipComms = ReadColumn<byte[]>(rowGroup, TraceSampleRowSchema.Schema.Columns[32]);
                var addressComms = ReadColumn<byte[]>(rowGroup, TraceSampleRowSchema.Schema.Columns[44]);

                ValidateRowGroupLengths(path, ipComms.Length, addressComms);

                for (var i = 0; i < ipComms.Length; i++)
                    yield return (ipComms[i], addressComms[i]);
            }
        }

        public static IEnumerable<TraceRow> ReadRows(string path)
        {
            using var stream = File.OpenRead(path);
            using var reader = TraceSampleRowSchema.Schema.CreateReader(stream);

            foreach (var token in reader.EnumerateRowGroups())
            {
                using var rowGroup = reader.OpenRowGroup(stream, token);
                var ids = ReadColumn<ulong>(rowGroup, TraceSampleRowSchema.Schema.Columns[0]);
                var pids = ReadColumn<uint>(rowGroup, TraceSampleRowSchema.Schema.Columns[2]);
                var tids = ReadColumn<uint>(rowGroup, TraceSampleRowSchema.Schema.Columns[3]);
                var times = ReadColumn<ulong>(rowGroup, TraceSampleRowSchema.Schema.Columns[4]);
                var flags = ReadColumn<uint>(rowGroup, TraceSampleRowSchema.Schema.Columns[6]);
                var ips = ReadColumn<ulong>(rowGroup, TraceSampleRowSchema.Schema.Columns[7]);
                var ipLocationIds = ReadColumn<ulong>(rowGroup, TraceSampleRowSchema.Schema.Columns[8]);
                var addrs = ReadColumn<ulong>(rowGroup, TraceSampleRowSchema.Schema.Columns[9]);
                var addressLocationIds = ReadColumn<ulong>(rowGroup, TraceSampleRowSchema.Schema.Columns[10]);
                var events = ReadColumn<byte[]>(rowGroup, TraceSampleRowSchema.Schema.Columns[17]);
                var ipSymbols = ReadColumn<byte[]>(rowGroup, TraceSampleRowSchema.Schema.Columns[23]);
                var ipComms = ReadColumn<byte[]>(rowGroup, TraceSampleRowSchema.Schema.Columns[32]);
                var addressSymbols = ReadColumn<byte[]>(rowGroup, TraceSampleRowSchema.Schema.Columns[35]);
                var addressComms = ReadColumn<byte[]>(rowGroup, TraceSampleRowSchema.Schema.Columns[44]);

                ValidateRowGroupLengths(path, ids.Length, pids, tids, times, flags, ips, ipLocationIds, addrs,
                    addressLocationIds, events, ipSymbols, ipComms, addressSymbols, addressComms);

                for (var i = 0; i < ids.Length; i++)
                {
                    yield return new TraceRow(
                        Id: ids[i],
                        Pid: pids[i],
                        Tid: tids[i],
                        Time: times[i],
                        Flags: flags[i],
                        Ip: ips[i],
                        IpLocationId: ipLocationIds[i],
                        Addr: addrs[i],
                        AddressLocationId: addressLocationIds[i],
                        Event: events[i],
                        IpSym: ipSymbols[i],
                        IpComm: ipComms[i],
                        AddressSym: addressSymbols[i],
                        AddressComm: addressComms[i]);
                }
            }
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

    static void ValidateRowGroupLengths<T1>(string path, int expected, T1[] values)
        => ValidateLength(path, expected, values.Length);

    static void ValidateRowGroupLengths<T1, T2, T3>(string path, int expected, T1[] v1, T2[] v2, T3[] v3)
    {
        ValidateLength(path, expected, v1.Length);
        ValidateLength(path, expected, v2.Length);
        ValidateLength(path, expected, v3.Length);
    }

    static void ValidateRowGroupLengths<T1, T2, T3, T4, T5>(
        string path,
        int expected,
        T1[] v1,
        T2[] v2,
        T3[] v3,
        T4[] v4,
        T5[] v5)
    {
        ValidateLength(path, expected, v1.Length);
        ValidateLength(path, expected, v2.Length);
        ValidateLength(path, expected, v3.Length);
        ValidateLength(path, expected, v4.Length);
        ValidateLength(path, expected, v5.Length);
    }

    static void ValidateRowGroupLengths<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(
        string path,
        int expected,
        T1[] v1,
        T2[] v2,
        T3[] v3,
        T4[] v4,
        T5[] v5,
        T6[] v6,
        T7[] v7,
        T8[] v8,
        T9[] v9,
        T10[] v10,
        T11[] v11,
        T12[] v12,
        T13[] v13)
    {
        ValidateLength(path, expected, v1.Length);
        ValidateLength(path, expected, v2.Length);
        ValidateLength(path, expected, v3.Length);
        ValidateLength(path, expected, v4.Length);
        ValidateLength(path, expected, v5.Length);
        ValidateLength(path, expected, v6.Length);
        ValidateLength(path, expected, v7.Length);
        ValidateLength(path, expected, v8.Length);
        ValidateLength(path, expected, v9.Length);
        ValidateLength(path, expected, v10.Length);
        ValidateLength(path, expected, v11.Length);
        ValidateLength(path, expected, v12.Length);
        ValidateLength(path, expected, v13.Length);
    }

    static void ValidateLength(string path, int expected, int actual)
    {
        if (actual != expected)
            throw new InvalidDataException($"Parquet column length mismatch in '{path}'. Expected {expected}, got {actual}.");
    }
}
