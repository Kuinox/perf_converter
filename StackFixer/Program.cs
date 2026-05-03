using PerfConverter.PerfStructs;
using Plank.Reading;
using Plank.Schema;
using Plank.Writing;
using Temp.Schema.Schema;

namespace StackFixer;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length is < 1 or > 2)
        {
            Console.Error.WriteLine("Usage: StackFixer <parquet_output> [stack_index.parquet]");
            return 1;
        }

        var inputFolder = args[0];
        var outputFile = args.Length == 2
            ? args[1]
            : Path.Combine(inputFolder, "stack_index.parquet");

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
            var writer = StackIndexRowSchema.CreateRowWriter(output, writerOptions);
            var processor = new StackIndexProcessor(writer);
            processor.Process(inputFolder);
            writer.Complete();

            Console.WriteLine(
                $"Wrote {outputFile}. Frames: {processor.Frames}, calls: {processor.Calls}, returns: {processor.Returns}, skipped returns: {processor.SkippedReturns}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}

sealed class StackIndexProcessor(StackIndexRowSchema.PipelineWriter writer)
{
    public ulong Frames { get; private set; }
    public ulong Calls { get; private set; }
    public ulong Returns { get; private set; }
    public ulong SkippedReturns { get; private set; }

    public void Process(string inputDirectory)
    {
        foreach (var pidDirectory in GetPidDirectories(inputDirectory))
            ProcessPidDirectory(pidDirectory);
    }

    void ProcessPidDirectory(string pidDirectory)
    {
        var threadDirectories = Directory.EnumerateDirectories(pidDirectory, "tid=*")
            .OrderBy(static path => ParsePrefixedUInt(Path.GetFileName(path), "tid="))
            .ToArray();

        foreach (var threadDirectory in threadDirectories)
            ProcessThreadDirectory(threadDirectory);
    }

    void ProcessThreadDirectory(string threadDirectory)
    {
        var branchFiles = Directory.EnumerateFiles(threadDirectory, "branches.parquet")
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (branchFiles.Length == 0)
            return;

        var state = new ThreadStackState();
        foreach (var branchFile in branchFiles)
        {
            Console.WriteLine($"Indexing {branchFile}...");
            foreach (var row in TraceParquetReader.ReadRows(branchFile))
                ProcessBranchRow(state, row);
        }

        CloseOpenFrames(state);
    }

    void ProcessBranchRow(ThreadStackState state, TraceRow row)
    {
        state.LastTime = row.Time;
        state.LastTrace = row.Id;

        if ((row.Flags & (uint)DLFilterFlag.PERF_DLFILTER_FLAG_CALL) != 0)
        {
            Calls++;
            var locationId = row.AddressLocationId != 0 ? row.AddressLocationId : row.IpLocationId;
            state.OpenFrames.Push(new OpenStackFrame(
                Pid: row.Pid,
                Tid: row.Tid,
                Cpu: row.Cpu,
                Depth: checked((uint)state.OpenFrames.Count),
                StartTime: row.Time,
                StartTrace: row.Id,
                LocationId: locationId));
        }

        if ((row.Flags & (uint)DLFilterFlag.PERF_DLFILTER_FLAG_RETURN) != 0)
        {
            Returns++;
            if (!state.OpenFrames.TryPop(out var frame))
            {
                SkippedReturns++;
                return;
            }

            WriteFrame(frame, row.Time, row.Id);
        }
    }

    void CloseOpenFrames(ThreadStackState state)
    {
        while (state.OpenFrames.TryPop(out var frame))
            WriteFrame(frame, state.LastTime, state.LastTrace);
    }

    void WriteFrame(OpenStackFrame frame, ulong endTime, ulong endTrace)
    {
        if (endTime < frame.StartTime)
            endTime = frame.StartTime;

        var row = writer.GetRow();
        row.Pid = frame.Pid;
        row.Tid = frame.Tid;
        row.Cpu = frame.Cpu;
        row.Depth = frame.Depth;
        row.StartTime = frame.StartTime;
        row.EndTime = endTime;
        row.StartTrace = frame.StartTrace;
        row.EndTrace = endTrace;
        row.LocationId = frame.LocationId;
        writer.Next();
        Frames++;
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

    static void ValidateRowGroupLengths<T1, T2, T3, T4, T5, T6, T7>(
        string path,
        int expected,
        T1[] v1,
        T2[] v2,
        T3[] v3,
        T4[] v4,
        T5[] v5,
        T6[] v6,
        T7[] v7)
    {
        ValidateLength(path, expected, v1.Length);
        ValidateLength(path, expected, v2.Length);
        ValidateLength(path, expected, v3.Length);
        ValidateLength(path, expected, v4.Length);
        ValidateLength(path, expected, v5.Length);
        ValidateLength(path, expected, v6.Length);
        ValidateLength(path, expected, v7.Length);
    }

    static void ValidateLength(string path, int expected, int actual)
    {
        if (actual != expected)
            throw new InvalidDataException($"Parquet column length mismatch in '{path}'. Expected {expected}, got {actual}.");
    }

    readonly record struct TraceRow(
        ulong Id,
        uint Pid,
        uint Tid,
        ulong Time,
        uint Cpu,
        uint Flags,
        ulong IpLocationId,
        ulong AddressLocationId);

    readonly record struct OpenStackFrame(
        uint Pid,
        uint Tid,
        uint Cpu,
        uint Depth,
        ulong StartTime,
        ulong StartTrace,
        ulong LocationId);

    sealed class ThreadStackState
    {
        public Stack<OpenStackFrame> OpenFrames { get; } = new();
        public ulong LastTime { get; set; }
        public ulong LastTrace { get; set; }
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
                var pids = ReadColumn<uint>(rowGroup, TraceSampleRowSchema.Schema.Columns[2]);
                var tids = ReadColumn<uint>(rowGroup, TraceSampleRowSchema.Schema.Columns[3]);
                var times = ReadColumn<ulong>(rowGroup, TraceSampleRowSchema.Schema.Columns[4]);
                var cpus = ReadColumn<uint>(rowGroup, TraceSampleRowSchema.Schema.Columns[5]);
                var flags = ReadColumn<uint>(rowGroup, TraceSampleRowSchema.Schema.Columns[6]);
                var ipLocationIds = ReadColumn<ulong>(rowGroup, TraceSampleRowSchema.Schema.Columns[8]);
                var addressLocationIds = ReadColumn<ulong>(rowGroup, TraceSampleRowSchema.Schema.Columns[10]);

                ValidateRowGroupLengths(path, ids.Length, pids, tids, times, cpus, flags, ipLocationIds, addressLocationIds);

                for (var i = 0; i < ids.Length; i++)
                {
                    yield return new TraceRow(
                        Id: ids[i],
                        Pid: pids[i],
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
}
