using PerfConverter.PerfStructs;
using Plank.Reading;
using Plank.Schema;
using Plank.Writing;
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
            var processor = new StackReconstructionProcessor(writer);
            processor.Process(inputFolder);
            writer.Complete();

            Console.WriteLine(
                $"Wrote {outputFile}. Frames: {processor.Frames}, calls: {processor.Calls}, returns: {processor.Returns}, skipped returns: {processor.SkippedReturns}, trace clips: {processor.TraceClips}, aux losses: {processor.AuxLossesApplied}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}

sealed class StackReconstructionProcessor(StackFrameRowSchema.PipelineWriter writer)
{
    public ulong Frames { get; private set; }
    public ulong Calls { get; private set; }
    public ulong Returns { get; private set; }
    public ulong SkippedReturns { get; private set; }
    public ulong TraceClips { get; private set; }
    public ulong AuxLossesApplied { get; private set; }

    ulong _nextFrameId = 1;
    IReadOnlyDictionary<uint, Queue<AuxLossRow>> _auxLosses = new Dictionary<uint, Queue<AuxLossRow>>();

    public void Process(string inputDirectory)
    {
        _auxLosses = AuxLossParquetReader.ReadByTid(inputDirectory);

        foreach (var group in DiscoverBranchFiles(inputDirectory))
            ProcessThread(group.Tid, group.Files);
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
        state.OpenFrames.Clear();
    }

    void ProcessBranchRow(ThreadStackState state, TraceRow row)
    {
        var flags = (DLFilterFlag)row.Flags;

        if ((flags & DLFilterFlag.PERF_DLFILTER_FLAG_TRACE_BEGIN) != 0)
            ResumeTrace(state);

        if ((flags & DLFilterFlag.PERF_DLFILTER_FLAG_CALL) != 0)
        {
            Calls++;
            var locationId = row.AddressLocationId != 0 ? row.AddressLocationId : row.IpLocationId;
            var frame = new OpenStackFrame(
                Tid: row.Tid,
                Depth: checked((uint)state.OpenFrames.Count),
                LocationId: locationId,
                ActiveStartTime: row.Time,
                ActiveStartTrace: row.Id,
                ActiveStartCpu: row.Cpu,
                StartReason: StackFrameBoundaryReason.Call);
            state.OpenFrames.Add(frame);
        }

        if ((flags & DLFilterFlag.PERF_DLFILTER_FLAG_RETURN) != 0)
        {
            Returns++;
            if (state.OpenFrames.Count == 0)
            {
                SkippedReturns++;
            }
            else
            {
                var index = state.OpenFrames.Count - 1;
                var frame = state.OpenFrames[index];
                state.OpenFrames.RemoveAt(index);
                WriteFrame(frame, row.Time, row.Id, row.Cpu, StackFrameBoundaryReason.Return);
            }
        }

        if ((flags & DLFilterFlag.PERF_DLFILTER_FLAG_TRACE_END) != 0)
            SuspendTrace(state, row);
    }

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
        CloseActiveIntervals(state, row.Time, row.Id, row.Cpu, StackFrameBoundaryReason.TraceEnd);

        for (var i = 0; i < state.OpenFrames.Count; i++)
        {
            var frame = state.OpenFrames[i];
            state.OpenFrames[i] = frame with
            {
                ActiveStartTime = row.Time,
                ActiveStartTrace = row.Id,
                ActiveStartCpu = row.Cpu,
                StartReason = StackFrameBoundaryReason.TraceResume
            };
        }
    }

    void CloseActiveIntervals(
        ThreadStackState state,
        ulong endTime,
        ulong endTrace,
        uint endCpu,
        StackFrameBoundaryReason endReason)
    {
        for (var i = state.OpenFrames.Count - 1; i >= 0; i--)
        {
            var frame = state.OpenFrames[i];
            WriteFrame(frame, endTime, endTrace, endCpu, endReason);
        }
    }

    void WriteFrame(OpenStackFrame frame, ulong endTime, ulong endTrace, uint endCpu, StackFrameBoundaryReason endReason)
    {
        if (endTime < frame.ActiveStartTime)
            endTime = frame.ActiveStartTime;
        if (endTrace == 0)
            endTrace = frame.ActiveStartTrace;

        var row = writer.GetRow();
        row.FrameId = _nextFrameId++;
        row.Tid = frame.Tid;
        row.Depth = frame.Depth;
        row.StartTime = frame.ActiveStartTime;
        row.EndTime = endTime;
        row.StartTrace = frame.ActiveStartTrace;
        row.EndTrace = endTrace;
        row.LocationId = frame.LocationId;
        row.StartCpu = frame.ActiveStartCpu;
        row.EndCpu = endCpu;
        row.StartReason = (byte)frame.StartReason;
        row.EndReason = (byte)endReason;
        writer.Next();
        Frames++;
    }

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
        ulong IpLocationId,
        ulong AddressLocationId);

    readonly record struct AuxLossRow(ulong Time, uint Tid, uint Cpu);

    readonly record struct OpenStackFrame(
        uint Tid,
        uint Depth,
        ulong LocationId,
        ulong ActiveStartTime,
        ulong ActiveStartTrace,
        uint ActiveStartCpu,
        StackFrameBoundaryReason StartReason);

    sealed class ThreadStackState(uint tid)
    {
        public uint Tid { get; } = tid;
        public List<OpenStackFrame> OpenFrames { get; } = [];
        public bool TraceActive { get; set; } = true;
        public bool SeenRows { get; set; }
        public ulong LastTime { get; set; }
        public ulong LastTrace { get; set; }
        public uint LastCpu { get; set; }
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
                var ipLocationIds = ReadColumn<ulong>(rowGroup, TraceSampleRowSchema.Schema.Columns[8]);
                var addressLocationIds = ReadColumn<ulong>(rowGroup, TraceSampleRowSchema.Schema.Columns[10]);

                ValidateRowGroupLengths(path, ids.Length, tids, times, cpus, flags, ipLocationIds, addressLocationIds);

                for (var i = 0; i < ids.Length; i++)
                {
                    yield return new TraceRow(
                        Id: ids[i],
                        Tid: tids[i],
                        Time: times[i],
                        Cpu: cpus[i],
                        Flags: flags[i],
                        IpLocationId: ipLocationIds[i],
                        AddressLocationId: addressLocationIds[i]);
                }
            }
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
}
