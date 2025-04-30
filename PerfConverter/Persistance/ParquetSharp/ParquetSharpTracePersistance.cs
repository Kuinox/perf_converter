using ParquetSharp;
using PerfConverter.Entry;

namespace PerfConverter.Persistance.ParquetSharp;

public class ParquetSharpTracePersistance : IBatchPersistance<TraceSampleEntry>
{
    readonly ParquetFileWriter _writer;

    ulong[] _ids;
    int[] _pids;
    int[] _tids;
    ulong[] _times;
    int[] _cpus;
    long[] _ips;
    long[] _addrs;
    ulong[] _periods;
    ulong[] _insnCnts;
    ulong[] _cycCnts;
    ulong[] _weights;
    byte[] _cpumodes;
    byte[] _addrCorrelatesSyms;
    string[] _events;
    int[] _machinePids;
    int[] _vcpus;

    ParquetSharpTracePersistance(int batchSize, ParquetFileWriter writer)
    {
        _writer = writer;
        ResizeArrays(batchSize);
    }

    public Task PersistAsync(IReadOnlyCollection<TraceSampleEntry> batch)
    {
        ResizeArrays(batch.Count);

        var i = 0;
        foreach (var entry in batch)
        {
            _ids[i] = entry.Id;
            _pids[i] = entry.Pid;
            _tids[i] = entry.Tid;
            _times[i] = entry.Time;
            _cpus[i] = entry.Cpu;
            _ips[i] = entry.Ip;
            _addrs[i] = entry.Addr;
            _periods[i] = entry.Period;
            _insnCnts[i] = entry.InsnCnt;
            _cycCnts[i] = entry.CycCnt;
            _weights[i] = entry.Weight;
            _cpumodes[i] = entry.Cpumode;
            _addrCorrelatesSyms[i] = entry.AddrCorrelatesSym;
            _events[i] = entry.Event ?? string.Empty;
            _machinePids[i] = entry.MachinePid;
            _vcpus[i] = entry.Vcpu;
            i++;
        }

        try
        {
            using var rowGroup = _writer.AppendRowGroup();
            using (var idWriter = rowGroup.NextColumn().LogicalWriter<ulong>())
                idWriter.WriteBatch(_ids);

            using (var pidWriter = rowGroup.NextColumn().LogicalWriter<int>())
                pidWriter.WriteBatch(_pids);

            using (var tidWriter = rowGroup.NextColumn().LogicalWriter<int>())
                tidWriter.WriteBatch(_tids);

            using (var timeWriter = rowGroup.NextColumn().LogicalWriter<ulong>())
                timeWriter.WriteBatch(_times);

            using (var cpuWriter = rowGroup.NextColumn().LogicalWriter<int>())
                cpuWriter.WriteBatch(_cpus);

            using (var ipWriter = rowGroup.NextColumn().LogicalWriter<long>())
                ipWriter.WriteBatch(_ips);

            using (var addrWriter = rowGroup.NextColumn().LogicalWriter<long>())
                addrWriter.WriteBatch(_addrs);

            using (var periodWriter = rowGroup.NextColumn().LogicalWriter<ulong>())
                periodWriter.WriteBatch(_periods);

            using (var insnCntWriter = rowGroup.NextColumn().LogicalWriter<ulong>())
                insnCntWriter.WriteBatch(_insnCnts);

            using (var cycCntWriter = rowGroup.NextColumn().LogicalWriter<ulong>())
                cycCntWriter.WriteBatch(_cycCnts);

            using (var weightWriter = rowGroup.NextColumn().LogicalWriter<ulong>())
                weightWriter.WriteBatch(_weights);

            using (var cpumodeWriter = rowGroup.NextColumn().LogicalWriter<byte>())
                cpumodeWriter.WriteBatch(_cpumodes);

            using (var addrCorrelatesSymWriter = rowGroup.NextColumn().LogicalWriter<byte>())
                addrCorrelatesSymWriter.WriteBatch(_addrCorrelatesSyms);

            using (var eventWriter = rowGroup.NextColumn().LogicalWriter<string>())
                eventWriter.WriteBatch(_events);

            using (var machinePidWriter = rowGroup.NextColumn().LogicalWriter<int>())
                machinePidWriter.WriteBatch(_machinePids);

            using (var vcpuWriter = rowGroup.NextColumn().LogicalWriter<int>())
                vcpuWriter.WriteBatch(_vcpus);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error writing traces to parquet: {ex.Message}");
            throw;
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _writer.Close();
        _writer.Dispose();
        return ValueTask.CompletedTask;
    }

    [System.Diagnostics.CodeAnalysis.MemberNotNull(
        nameof(_ids),
        nameof(_pids),
        nameof(_tids),
        nameof(_times),
        nameof(_cpus),
        nameof(_ips),
        nameof(_addrs),
        nameof(_periods),
        nameof(_insnCnts),
        nameof(_cycCnts),
        nameof(_weights),
        nameof(_cpumodes),
        nameof(_addrCorrelatesSyms),
        nameof(_events),
        nameof(_machinePids),
        nameof(_vcpus))]
    void ResizeArrays(int newSize)
    {
        if (_ids != null && _ids.Length == newSize)
#pragma warning disable CS8774 // Member must have a non-null value when exiting.
            return;
#pragma warning restore CS8774 // Member must have a non-null value when exiting.

        _ids = new ulong[newSize];
        _pids = new int[newSize];
        _tids = new int[newSize];
        _times = new ulong[newSize];
        _cpus = new int[newSize];
        _ips = new long[newSize];
        _addrs = new long[newSize];
        _periods = new ulong[newSize];
        _insnCnts = new ulong[newSize];
        _cycCnts = new ulong[newSize];
        _weights = new ulong[newSize];
        _cpumodes = new byte[newSize];
        _addrCorrelatesSyms = new byte[newSize];
        _events = new string[newSize];
        _machinePids = new int[newSize];
        _vcpus = new int[newSize];
    }

    public static IBatchPersistance<TraceSampleEntry> Create(string basePath, int batchSize, Compression compressionMethod)
    {
        Directory.CreateDirectory(basePath);

        var filePath = Path.Combine(basePath, "traces.parquet");
        var columns = new Column[]
        {
            new Column<ulong>("Id"),
            new Column<int>("Pid"),
            new Column<int>("Tid"),
            new Column<ulong>("Time"),
            new Column<int>("Cpu"),
            new Column<long>("Ip"),
            new Column<long>("Addr"),
            new Column<ulong>("Period"),
            new Column<ulong>("InsnCnt"),
            new Column<ulong>("CycCnt"),
            new Column<ulong>("Weight"),
            new Column<byte>("Cpumode"),
            new Column<byte>("AddrCorrelatesSym"),
            new Column<string>("Event"),
            new Column<int>("MachinePid"),
            new Column<int>("Vcpu")
        };

        var writer = new ParquetFileWriter(filePath, columns, compressionMethod);

        return new ParquetSharpTracePersistance(batchSize, writer);
    }
}