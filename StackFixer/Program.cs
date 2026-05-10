using PerfConverter.PerfStructs;
using Plank.Reading;
using Plank.Schema;
using Plank.Writing;
using System.Runtime.InteropServices;
using Temp.Schema;
using Temp.Schema.Schema;

namespace StackFixer;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length is < 1 or > 2)
        {
            Console.Error.WriteLine("Usage: StackFixer <parquet_output> [stack_frames.parquet]");
            return 1;
        }

        var inputFolder = args[0];
        var outputFile = args.Length == 2
            ? args[1]
            : Path.Combine(inputFolder, "stack_frames.parquet");

        if (!Directory.Exists(inputFolder))
        {
            Console.Error.WriteLine($"Input folder does not exist: {inputFolder}");
            return 1;
        }

        try
        {
            if (File.Exists(outputFile))
                File.Delete(outputFile);

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputFile))!);
            using var output = File.Create(outputFile);
            var writerOptions = new ParquetWriterOptions { Compression = CompressionKind.Snappy };
            var writer = StackFrameRowSchema.CreateRowWriter(output, writerOptions);
            var processor = new StackReconstructionProcessor();
            processor.Process(inputFolder);
            processor.WriteRows(writer);
            writer.Complete();

            Console.WriteLine(
                $"Wrote {outputFile}. Frames: {processor.Frames}, calls: {processor.Calls}, ignored calls: {processor.IgnoredCalls}, returns: {processor.Returns}, skipped returns: {processor.SkippedReturns}, ignored returns: {processor.IgnoredReturns}, resyncs: {processor.Resyncs}, dropped frames: {processor.DroppedFrames}, trace clips: {processor.TraceClips}, aux losses: {processor.AuxLossesApplied}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}

sealed class StackReconstructionProcessor
{
    public ulong Frames { get; private set; }
    public ulong Calls { get; private set; }
    public ulong Returns { get; private set; }
    public ulong SkippedReturns { get; private set; }
    public ulong TraceClips { get; private set; }
    public ulong AuxLossesApplied { get; private set; }
    public ulong Resyncs { get; private set; }
    public ulong DroppedFrames { get; private set; }
    public ulong IgnoredReturns { get; private set; }
    public ulong IgnoredCalls { get; private set; }

    ulong _nextFrameId = 1;
    IReadOnlyDictionary<uint, Queue<AuxLossRow>> _auxLosses = new Dictionary<uint, Queue<AuxLossRow>>();
    IReadOnlyDictionary<ulong, SourceLocationKey> _sourceLocations = new Dictionary<ulong, SourceLocationKey>();
    IReadOnlyDictionary<FunctionKey, ulong> _functionEntries = new Dictionary<FunctionKey, ulong>();
    readonly List<StackFrameOutput> _frames = [];

    public void Process(string inputDirectory)
    {
        _auxLosses = AuxLossParquetReader.ReadByTid(inputDirectory);
        _sourceLocations = SourceLocationParquetReader.Read(inputDirectory);
        _functionEntries = SourceLocationParquetReader.GetFunctionEntries(_sourceLocations.Values);

        foreach (var group in DiscoverBranchFiles(inputDirectory))
            ProcessThread(group.Tid, group.Files);
    }

    public void WriteRows(StackFrameRowSchema.PipelineWriter writer)
    {
        foreach (var frame in NormalizeDepths(_frames))
        {
            var row = writer.GetRow();
            row.FrameId = frame.FrameId;
            row.Tid = frame.Tid;
            row.Depth = frame.Depth;
            row.StartTime = frame.StartTime;
            row.EndTime = frame.EndTime;
            row.StartTrace = frame.StartTrace;
            row.EndTrace = frame.EndTrace;
            row.LocationId = frame.LocationId;
            row.StartCpu = frame.StartCpu;
            row.EndCpu = frame.EndCpu;
            row.StartReason = (byte)frame.StartReason;
            row.EndReason = (byte)frame.EndReason;
            row.Kind = (byte)frame.Kind;
            writer.Next();
        }
    }

    void ProcessThread(uint tid, IReadOnlyList<string> branchFiles)
    {
        var state = new ThreadStackState(tid);
        var auxLosses = _auxLosses.TryGetValue(tid, out var losses)
            ? losses
            : new Queue<AuxLossRow>();

        foreach (var branchFile in branchFiles)
        {
            Console.WriteLine($"Reconstructing tid={tid} from {branchFile}...");
            foreach (var row in TraceParquetReader.ReadRows(branchFile))
            {
                ApplyAuxLossesBefore(row, state, auxLosses);
                ProcessBranchRow(state, row);
                state.LastTime = row.Time;
                state.LastTrace = row.Id;
                state.LastCpu = row.Cpu;
                state.SeenRows = true;
            }
        }

        ApplyRemainingAuxLosses(state, auxLosses);
        CloseActiveIntervals(state, state.LastTime, state.LastTrace, state.LastCpu, StackFrameBoundaryReason.EndOfInput);
    }

    void ApplyAuxLossesBefore(TraceRow row, ThreadStackState state, Queue<AuxLossRow> auxLosses)
    {
        while (auxLosses.TryPeek(out var loss) && loss.Time < row.Time)
        {
            auxLosses.Dequeue();
            ApplyAuxLoss(state, loss);
        }
    }

    void ApplyRemainingAuxLosses(ThreadStackState state, Queue<AuxLossRow> auxLosses)
    {
        while (auxLosses.TryDequeue(out var loss))
            ApplyAuxLoss(state, loss);
    }

    void ApplyAuxLoss(ThreadStackState state, AuxLossRow loss)
    {
        AuxLossesApplied++;
        var endTrace = state.SeenRows ? state.LastTrace : 0;
        var endCpu = state.SeenRows ? state.LastCpu : loss.Cpu;
        CloseActiveIntervals(state, loss.Time, endTrace, endCpu, StackFrameBoundaryReason.AuxLoss);
        foreach (var stack in state.Stacks)
            stack.OpenFrames.Clear();
    }

    void ProcessBranchRow(ThreadStackState state, TraceRow row)
    {
        var flags = (DLFilterFlag)row.Flags;
        var stack = state.GetStack(GetStackKind(row));

        if ((flags & DLFilterFlag.PERF_DLFILTER_FLAG_TRACE_BEGIN) != 0)
            ResumeTrace(state);

        var isCall = IsCallBranch(row, flags);
        if (isCall)
        {
            Calls++;
            if (!IsStackCall(row))
            {
                IgnoredCalls++;
                return;
            }

            SynchronizeCaller(stack, row);
            var locationId = row.AddressLocationId != 0 ? row.AddressLocationId : row.IpLocationId;
            var frame = new OpenStackFrame(
                Tid: row.Tid,
                Kind: stack.Kind,
                LocationId: locationId,
                ActiveStartTime: row.Time,
                ActiveStartTrace: row.Id,
                ActiveStartCpu: row.Cpu,
                StartReason: StackFrameBoundaryReason.Call);
            stack.OpenFrames.Add(frame);
        }

        if (!isCall && IsReturnBranch(row, flags))
        {
            Returns++;
            ProcessReturn(stack, row);
        }

        if ((flags & DLFilterFlag.PERF_DLFILTER_FLAG_TRACE_END) != 0)
            SuspendTrace(state, row);
    }

    bool IsStackCall(TraceRow row)
    {
        var caller = GetLocation(row.IpLocationId);
        var callee = GetLocation(row.AddressLocationId);
        if (!caller.IsSameFunction(callee))
            return true;

        return IsFunctionEntry(callee);
    }

    bool IsCallBranch(TraceRow row, DLFilterFlag flags)
    {
        if ((flags & DLFilterFlag.PERF_DLFILTER_FLAG_CALL) != 0)
            return true;

        var caller = GetLocation(row.IpLocationId);
        var callee = GetLocation(row.AddressLocationId);
        return !caller.IsSameFunction(callee) && IsFunctionEntry(callee);
    }

    bool IsReturnBranch(TraceRow row, DLFilterFlag flags)
    {
        if ((flags & DLFilterFlag.PERF_DLFILTER_FLAG_RETURN) != 0)
            return true;

        var source = GetLocation(row.IpLocationId);
        var target = GetLocation(row.AddressLocationId);
        return !source.IsSameFunction(target) && !IsFunctionEntry(target);
    }

    bool IsFunctionEntry(SourceLocationKey location)
        => _functionEntries.TryGetValue(location.Function, out var entryAddress) &&
           location.RelativeAddress == entryAddress;

    static StackFrameKind GetStackKind(TraceRow row)
        => row.CpuMode == PerfCpuMode.User ? StackFrameKind.User : StackFrameKind.Kernel;

    void ProcessReturn(StackDomainState state, TraceRow row)
    {
        if (state.OpenFrames.Count == 0)
        {
            SkippedReturns++;
            return;
        }

        var returnSource = GetLocation(row.IpLocationId);
        var returnTarget = GetLocation(row.AddressLocationId);
        if (returnSource.IsSameFunction(returnTarget) && !IsRecursiveReturn(state, returnSource))
        {
            IgnoredReturns++;
            return;
        }

        var frameIndex = FindReturningFrame(state, row.IpLocationId);
        if (frameIndex < 0)
        {
            if (state.OpenFrames.Count > 1)
            {
                var topIndex = state.OpenFrames.Count - 1;
                var unmatchedFrame = state.OpenFrames[topIndex];
                state.OpenFrames.RemoveAt(topIndex);
                WriteFrame(unmatchedFrame, checked((uint)topIndex), row.Time, row.Id, row.Cpu, StackFrameBoundaryReason.Return);
                Resyncs++;
                return;
            }

            SkippedReturns++;
            return;
        }

        if (frameIndex != state.OpenFrames.Count - 1)
        {
            var dropped = state.OpenFrames.Count - frameIndex - 1;
            state.OpenFrames.RemoveRange(frameIndex + 1, dropped);
            DroppedFrames += checked((ulong)dropped);
            Resyncs++;
        }

        var frame = state.OpenFrames[frameIndex];
        state.OpenFrames.RemoveAt(frameIndex);
        WriteFrame(frame, checked((uint)frameIndex), row.Time, row.Id, row.Cpu, StackFrameBoundaryReason.Return);
    }

    void SynchronizeCaller(StackDomainState state, TraceRow row)
    {
        var caller = GetLocation(row.IpLocationId);
        var callee = GetLocation(row.AddressLocationId);
        if (caller.IsSameFunction(callee))
            return;

        var callerIndex = FindReturningFrame(state, row.IpLocationId);
        if (callerIndex >= 0)
        {
            if (callerIndex != state.OpenFrames.Count - 1)
            {
                var dropped = state.OpenFrames.Count - callerIndex - 1;
                state.OpenFrames.RemoveRange(callerIndex + 1, dropped);
                DroppedFrames += checked((ulong)dropped);
                Resyncs++;
            }

            return;
        }

        if (string.IsNullOrEmpty(caller.Symbol))
            return;

        state.OpenFrames.Add(new OpenStackFrame(
            Tid: row.Tid,
            Kind: state.Kind,
            LocationId: row.IpLocationId,
            ActiveStartTime: row.Time,
            ActiveStartTrace: row.Id,
            ActiveStartCpu: row.Cpu,
            StartReason: StackFrameBoundaryReason.Call));
        Resyncs++;
    }

    bool IsRecursiveReturn(StackDomainState state, SourceLocationKey returnLocation)
    {
        if (state.OpenFrames.Count < 2)
            return false;

        var top = GetLocation(state.OpenFrames[^1].LocationId);
        var parent = GetLocation(state.OpenFrames[^2].LocationId);
        return top.IsSameFunction(returnLocation) && parent.IsSameFunction(returnLocation);
    }

    int FindReturningFrame(StackDomainState state, ulong returnLocationId)
    {
        for (var i = state.OpenFrames.Count - 1; i >= 0; i--)
        {
            if (IsSameFunction(state.OpenFrames[i].LocationId, returnLocationId))
                return i;
        }

        return -1;
    }

    bool IsSameFunction(ulong frameLocationId, ulong returnLocationId)
    {
        if (frameLocationId == 0 || returnLocationId == 0)
            return frameLocationId == returnLocationId;

        if (frameLocationId == returnLocationId)
            return true;

        var frameLocation = GetLocation(frameLocationId);
        var returnLocation = GetLocation(returnLocationId);
        return frameLocation.IsSameFunction(returnLocation);
    }

    SourceLocationKey GetLocation(ulong locationId)
        => _sourceLocations.TryGetValue(locationId, out var location) ? location : default;

    void ResumeTrace(ThreadStackState state)
    {
        if (state.TraceActive)
            return;

        state.TraceActive = true;
    }

    void SuspendTrace(ThreadStackState state, TraceRow row)
    {
        if (!state.TraceActive)
            return;

        TraceClips++;
        state.TraceActive = false;
    }

    void CloseActiveIntervals(
        ThreadStackState state,
        ulong endTime,
        ulong endTrace,
        uint endCpu,
        StackFrameBoundaryReason endReason)
    {
        foreach (var stack in state.Stacks)
        {
            for (var i = stack.OpenFrames.Count - 1; i >= 0; i--)
            {
                var frame = stack.OpenFrames[i];
                WriteFrame(frame, checked((uint)i), endTime, endTrace, endCpu, endReason);
            }
        }
    }

    void WriteFrame(
        OpenStackFrame frame,
        uint depth,
        ulong endTime,
        ulong endTrace,
        uint endCpu,
        StackFrameBoundaryReason endReason)
    {
        if (endTime < frame.ActiveStartTime)
            endTime = frame.ActiveStartTime;
        if (endTrace == 0)
            endTrace = frame.ActiveStartTrace;

        if (endReason is not StackFrameBoundaryReason.Return and not StackFrameBoundaryReason.EndOfInput)
            return;

        if (endReason == StackFrameBoundaryReason.EndOfInput && GetLocation(frame.LocationId).Symbol != "main")
            return;

        _frames.Add(new StackFrameOutput(
            FrameId: _nextFrameId++,
            Tid: frame.Tid,
            Kind: frame.Kind,
            Depth: depth,
            StartTime: frame.ActiveStartTime,
            EndTime: endTime,
            StartTrace: frame.ActiveStartTrace,
            EndTrace: endTrace,
            LocationId: frame.LocationId,
            StartCpu: frame.ActiveStartCpu,
            EndCpu: endCpu,
            StartReason: frame.StartReason,
            EndReason: endReason));
        Frames++;
    }

    static IEnumerable<StackFrameOutput> NormalizeDepths(IEnumerable<StackFrameOutput> frames)
    {
        foreach (var group in frames.GroupBy(static frame => (frame.Tid, frame.Kind)).OrderBy(static group => group.Key.Tid).ThenBy(static group => group.Key.Kind))
        {
            var active = new List<StackFrameOutput>();
            foreach (var frame in group.OrderBy(static frame => frame, StackFrameStartComparer.Instance))
            {
                active.RemoveAll(open => EndsBeforeOrAt(open, frame));
                active.RemoveAll(open => !Contains(open, frame));

                var usedDepths = active.Select(static open => open.Depth).ToHashSet();
                var depth = 0u;
                while (usedDepths.Contains(depth))
                    depth++;

                var normalized = frame with { Depth = depth };
                active.Add(normalized);
                yield return normalized;
            }
        }
    }

    static bool EndsBeforeOrAt(StackFrameOutput open, StackFrameOutput next)
        => open.EndTime < next.StartTime ||
           (open.EndTime == next.StartTime && open.EndTrace <= next.StartTrace);

    static bool Contains(StackFrameOutput outer, StackFrameOutput inner)
        => outer.EndTime > inner.EndTime ||
           (outer.EndTime == inner.EndTime && outer.EndTrace >= inner.EndTrace);

    static IReadOnlyList<ThreadBranchFiles> DiscoverBranchFiles(string inputDirectory)
    {
        return Directory.EnumerateFiles(inputDirectory, "branches.parquet", SearchOption.AllDirectories)
            .Select(path => new
            {
                Path = path,
                Tid = TryParsePrefixedUInt(new DirectoryInfo(Path.GetDirectoryName(path)!).Name, "tid=")
            })
            .Where(item => item.Tid.HasValue)
            .GroupBy(item => item.Tid!.Value)
            .OrderBy(group => group.Key)
            .Select(group => new ThreadBranchFiles(
                group.Key,
                group.Select(item => item.Path).Order(StringComparer.Ordinal).ToArray()))
            .ToArray();
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

    readonly record struct ThreadBranchFiles(uint Tid, IReadOnlyList<string> Files);

    readonly record struct TraceRow(
        ulong Id,
        uint Tid,
        ulong Time,
        uint Cpu,
        uint Flags,
        PerfCpuMode CpuMode,
        ulong IpLocationId,
        ulong AddressLocationId);

    readonly record struct AuxLossRow(ulong Time, uint Tid, uint Cpu);

    readonly record struct FunctionKey(string Dso, string Symbol);

    readonly record struct SourceLocationKey(string Dso, ulong RelativeAddress, string Symbol)
    {
        public FunctionKey Function => new(Dso, Symbol);

        public bool IsSameFunction(SourceLocationKey other)
            => !string.IsNullOrEmpty(Dso) &&
               !string.IsNullOrEmpty(Symbol) &&
               string.Equals(Dso, other.Dso, StringComparison.Ordinal) &&
               string.Equals(Symbol, other.Symbol, StringComparison.Ordinal);
    }

    readonly record struct OpenStackFrame(
        uint Tid,
        StackFrameKind Kind,
        ulong LocationId,
        ulong ActiveStartTime,
        ulong ActiveStartTrace,
        uint ActiveStartCpu,
        StackFrameBoundaryReason StartReason);

    readonly record struct StackFrameOutput(
        ulong FrameId,
        uint Tid,
        StackFrameKind Kind,
        uint Depth,
        ulong StartTime,
        ulong EndTime,
        ulong StartTrace,
        ulong EndTrace,
        ulong LocationId,
        uint StartCpu,
        uint EndCpu,
        StackFrameBoundaryReason StartReason,
        StackFrameBoundaryReason EndReason);

    sealed class ThreadStackState(uint tid)
    {
        public uint Tid { get; } = tid;
        public StackDomainState UserStack { get; } = new(StackFrameKind.User);
        public StackDomainState KernelStack { get; } = new(StackFrameKind.Kernel);
        public StackDomainState[] Stacks => [UserStack, KernelStack];
        public bool TraceActive { get; set; } = true;
        public bool SeenRows { get; set; }
        public ulong LastTime { get; set; }
        public ulong LastTrace { get; set; }
        public uint LastCpu { get; set; }

        public StackDomainState GetStack(StackFrameKind kind)
            => kind == StackFrameKind.Kernel ? KernelStack : UserStack;
    }

    sealed class StackDomainState(StackFrameKind kind)
    {
        public StackFrameKind Kind { get; } = kind;
        public List<OpenStackFrame> OpenFrames { get; } = [];
    }

    enum PerfCpuMode : byte
    {
        Kernel = 1,
        User = 2
    }

    static class TraceParquetReader
    {
        public static IEnumerable<TraceRow> ReadRows(string path)
        {
            using var stream = File.OpenRead(path);
            using var reader = TraceSampleRowSchema.Schema.CreateReader(stream);

            foreach (var token in reader.EnumerateRowGroups())
            {
                using var rowGroup = reader.OpenRowGroup(stream, token);
                var ids = ReadColumn<ulong>(rowGroup, TraceSampleRowSchema.Schema.Columns[0]);
                var tids = ReadColumn<uint>(rowGroup, TraceSampleRowSchema.Schema.Columns[3]);
                var times = ReadColumn<ulong>(rowGroup, TraceSampleRowSchema.Schema.Columns[4]);
                var cpus = ReadColumn<uint>(rowGroup, TraceSampleRowSchema.Schema.Columns[5]);
                var flags = ReadColumn<uint>(rowGroup, TraceSampleRowSchema.Schema.Columns[6]);
                var cpuModes = ReadColumn<byte>(rowGroup, TraceSampleRowSchema.Schema.Columns[15]);
                var ipLocationIds = ReadColumn<ulong>(rowGroup, TraceSampleRowSchema.Schema.Columns[8]);
                var addressLocationIds = ReadColumn<ulong>(rowGroup, TraceSampleRowSchema.Schema.Columns[10]);

                ValidateRowGroupLengths(path, ids.Length, tids, times, cpus, flags, cpuModes, ipLocationIds, addressLocationIds);

                for (var i = 0; i < ids.Length; i++)
                {
                    yield return new TraceRow(
                        Id: ids[i],
                        Tid: tids[i],
                        Time: times[i],
                        Cpu: cpus[i],
                        Flags: flags[i],
                        CpuMode: (PerfCpuMode)cpuModes[i],
                        IpLocationId: ipLocationIds[i],
                        AddressLocationId: addressLocationIds[i]);
                }
            }
        }
    }

    sealed class StackFrameStartComparer : IComparer<StackFrameOutput>
    {
        public static StackFrameStartComparer Instance { get; } = new();

        public int Compare(StackFrameOutput left, StackFrameOutput right)
        {
            var compare = left.StartTime.CompareTo(right.StartTime);
            if (compare != 0)
                return compare;

            compare = left.StartTrace.CompareTo(right.StartTrace);
            if (compare != 0)
                return compare;

            compare = right.EndTime.CompareTo(left.EndTime);
            if (compare != 0)
                return compare;

            compare = right.EndTrace.CompareTo(left.EndTrace);
            if (compare != 0)
                return compare;

            return left.FrameId.CompareTo(right.FrameId);
        }
    }

    static class AuxLossParquetReader
    {
        public static IReadOnlyDictionary<uint, Queue<AuxLossRow>> ReadByTid(string inputDirectory)
        {
            var path = FindAuxLossFile(inputDirectory);
            if (path is null)
                return new Dictionary<uint, Queue<AuxLossRow>>();

            var rows = ReadRows(path)
                .GroupBy(row => row.Tid)
                .ToDictionary(
                    group => group.Key,
                    group => new Queue<AuxLossRow>(group.OrderBy(row => row.Time)));
            Console.WriteLine($"Loaded {rows.Sum(pair => pair.Value.Count)} AUX loss entries from {path}");
            return rows;
        }

        static string? FindAuxLossFile(string inputDirectory)
        {
            var path = Path.Combine(inputDirectory, "aux_loss.parquet");
            if (File.Exists(path))
                return path;

            var directory = new DirectoryInfo(inputDirectory);
            while (directory.Parent is not null)
            {
                path = Path.Combine(directory.Parent.FullName, "aux_loss.parquet");
                if (File.Exists(path))
                    return path;
                directory = directory.Parent;
            }

            return null;
        }

        static IEnumerable<AuxLossRow> ReadRows(string path)
        {
            using var stream = File.OpenRead(path);
            using var reader = AuxDataLossRowSchema.Schema.CreateReader(stream);

            foreach (var token in reader.EnumerateRowGroups())
            {
                using var rowGroup = reader.OpenRowGroup(stream, token);
                var times = ReadColumn<ulong>(rowGroup, AuxDataLossRowSchema.Schema.Columns[1]);
                var tids = ReadColumn<uint>(rowGroup, AuxDataLossRowSchema.Schema.Columns[3]);
                var cpus = ReadColumn<uint>(rowGroup, AuxDataLossRowSchema.Schema.Columns[4]);

                ValidateRowGroupLengths(path, times.Length, tids, cpus);

                for (var i = 0; i < times.Length; i++)
                    yield return new AuxLossRow(times[i], tids[i], cpus[i]);
            }
        }
    }

    static class SourceLocationParquetReader
    {
        public static IReadOnlyDictionary<ulong, SourceLocationKey> Read(string inputDirectory)
        {
            var path = Path.Combine(inputDirectory, "source_locations.parquet");
            if (!File.Exists(path))
                return new Dictionary<ulong, SourceLocationKey>();

            var rows = new Dictionary<ulong, SourceLocationKey>();
            using var stream = File.OpenRead(path);
            using var reader = SourceLocationRowSchema.Schema.CreateReader(stream);

            foreach (var token in reader.EnumerateRowGroups())
            {
                using var rowGroup = reader.OpenRowGroup(stream, token);
                var ids = ReadColumn<ulong>(rowGroup, SourceLocationRowSchema.Schema.Columns[0]);
                var dsos = ReadColumn<byte[]>(rowGroup, SourceLocationRowSchema.Schema.Columns[2]);
                var relativeAddresses = ReadColumn<ulong>(rowGroup, SourceLocationRowSchema.Schema.Columns[3]);
                var symbols = ReadColumn<byte[]?>(rowGroup, SourceLocationRowSchema.Schema.Columns[4]);

                ValidateRowGroupLengths(path, ids.Length, dsos, relativeAddresses, symbols);

                for (var i = 0; i < ids.Length; i++)
                    rows[ids[i]] = new SourceLocationKey(
                        ToUtf8String(dsos[i]),
                        relativeAddresses[i],
                        ToUtf8String(symbols[i]));
            }

            return rows;
        }

        public static IReadOnlyDictionary<FunctionKey, ulong> GetFunctionEntries(IEnumerable<SourceLocationKey> locations)
        {
            var entries = new Dictionary<FunctionKey, ulong>();
            foreach (var location in locations)
            {
                if (string.IsNullOrEmpty(location.Dso) || string.IsNullOrEmpty(location.Symbol))
                    continue;

                ref var entry = ref CollectionsMarshal.GetValueRefOrAddDefault(entries, location.Function, out var exists);
                if (!exists || location.RelativeAddress < entry)
                    entry = location.RelativeAddress;
            }

            return entries;
        }

        static string ToUtf8String(byte[]? value)
            => value is { Length: > 0 } ? System.Text.Encoding.UTF8.GetString(value) : string.Empty;

    }
}
