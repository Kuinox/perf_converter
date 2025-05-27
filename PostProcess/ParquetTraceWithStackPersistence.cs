using Parquet;
using Parquet.Data;
using Parquet.Schema;
using Temp.Schema;
using Temp.Core;

namespace PostProcess;

public class ParquetTraceWithStackPersistence : IBatchPersistence<TraceWithStackEntry>
{
    readonly ParquetWriter _writer;
    readonly FileStream _fileStream;

    ulong[] _ids = []!;
    ulong[] _perfIds = []!;
    uint[] _pids = []!;
    uint[] _tids = []!;
    ulong[] _times = []!;
    uint[] _cpus = []!;
    uint[] _flags = []!;
    ulong[] _ips = []!;
    ulong[] _addrs = []!;
    ulong[] _periods = []!;
    ulong[] _insnCnts = []!;
    ulong[] _cycCnts = []!;
    ulong[] _weights = []!;
    byte[] _cpumodes = []!;
    byte[] _addrCorrelates = []!;
    ulong[] _eventIds = []!;
    uint[] _machinePids = []!;
    uint[] _vcpus = []!;
    int[] _segmentIds = []!;
    ulong[][] _stacks = []!;

    ParquetTraceWithStackPersistence(ParquetWriter writer, FileStream fileStream)
    {
        _writer = writer;
        _fileStream = fileStream;
        ResizeArrays(0);
    }

    public async Task PersistAsync(IReadOnlyCollection<TraceWithStackEntry> batch)
    {
        if (batch.Count == 0) return;
        if (_ids.Length != batch.Count)
            ResizeArrays(batch.Count);
        int i = 0;
        foreach (var entry in batch)
        {
            _ids[i] = entry.Id;
            _perfIds[i] = entry.PerfId;
            _pids[i] = entry.Pid;
            _tids[i] = entry.Tid;
            _times[i] = entry.Time;
            _cpus[i] = entry.Cpu;
            _flags[i] = (uint)entry.Flags;
            _ips[i] = entry.Ip;
            _addrs[i] = entry.Addr;
            _periods[i] = entry.Period;
            _insnCnts[i] = entry.InsnCnt;
            _cycCnts[i] = entry.CycCnt;
            _weights[i] = entry.Weight;
            _cpumodes[i] = entry.Cpumode;
            _addrCorrelates[i] = entry.AddrCorrelatesSym;
            _eventIds[i] = entry.EventId;
            _machinePids[i] = entry.MachinePid;
            _vcpus[i] = entry.Vcpu;
            _segmentIds[i] = entry.SegmentId;
            _stacks[i] = entry.Stack;
            i++;
        }

        using var groupWriter = _writer.CreateRowGroup();
        await groupWriter.WriteColumnAsync(new DataColumn(TraceSampleSchema.Id, _ids));
        await groupWriter.WriteColumnAsync(new DataColumn(TraceSampleSchema.PerfId, _perfIds));
        await groupWriter.WriteColumnAsync(new DataColumn(TraceSampleSchema.Pid, _pids));
        await groupWriter.WriteColumnAsync(new DataColumn(TraceSampleSchema.Tid, _tids));
        await groupWriter.WriteColumnAsync(new DataColumn(TraceSampleSchema.Time, _times));
        await groupWriter.WriteColumnAsync(new DataColumn(TraceSampleSchema.Cpu, _cpus));
        await groupWriter.WriteColumnAsync(new DataColumn(TraceSampleSchema.Flags, _flags));
        await groupWriter.WriteColumnAsync(new DataColumn(TraceSampleSchema.Ip, _ips));
        await groupWriter.WriteColumnAsync(new DataColumn(TraceSampleSchema.Addr, _addrs));
        await groupWriter.WriteColumnAsync(new DataColumn(TraceSampleSchema.Period, _periods));
        await groupWriter.WriteColumnAsync(new DataColumn(TraceSampleSchema.InsnCnt, _insnCnts));
        await groupWriter.WriteColumnAsync(new DataColumn(TraceSampleSchema.CycCnt, _cycCnts));
        await groupWriter.WriteColumnAsync(new DataColumn(TraceSampleSchema.Weight, _weights));
        await groupWriter.WriteColumnAsync(new DataColumn(TraceSampleSchema.Cpumode, _cpumodes));
        await groupWriter.WriteColumnAsync(new DataColumn(TraceSampleSchema.AddrCorrelatesSym, _addrCorrelates));
        await groupWriter.WriteColumnAsync(new DataColumn(TraceSampleSchema.EventId, _eventIds));
        await groupWriter.WriteColumnAsync(new DataColumn(TraceSampleSchema.MachinePid, _machinePids));
        await groupWriter.WriteColumnAsync(new DataColumn(TraceSampleSchema.Vcpu, _vcpus));
        await groupWriter.WriteColumnAsync(new DataColumn(TraceWithStackSchema.SegmentId, _segmentIds));
        await groupWriter.WriteColumnAsync(new DataColumn(TraceWithStackSchema.Stack, _stacks));
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync();
        await _fileStream.DisposeAsync();
    }

    void ResizeArrays(int newSize)
    {
        _ids = new ulong[newSize];
        _perfIds = new ulong[newSize];
        _pids = new uint[newSize];
        _tids = new uint[newSize];
        _times = new ulong[newSize];
        _cpus = new uint[newSize];
        _flags = new uint[newSize];
        _ips = new ulong[newSize];
        _addrs = new ulong[newSize];
        _periods = new ulong[newSize];
        _insnCnts = new ulong[newSize];
        _cycCnts = new ulong[newSize];
        _weights = new ulong[newSize];
        _cpumodes = new byte[newSize];
        _addrCorrelates = new byte[newSize];
        _eventIds = new ulong[newSize];
        _machinePids = new uint[newSize];
        _vcpus = new uint[newSize];
        _segmentIds = new int[newSize];
        _stacks = new ulong[newSize][];
    }

    public static async Task<IBatchPersistence<TraceWithStackEntry>> Create(string filePath, CompressionMethod compressionMethod)
    {
        var schema = TraceWithStackSchema.Schema;
        var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite);
        var writer = await ParquetWriter.CreateAsync(schema, fileStream);
        writer.CompressionMethod = compressionMethod;
        return new ParquetTraceWithStackPersistence(writer, fileStream);
    }
}
