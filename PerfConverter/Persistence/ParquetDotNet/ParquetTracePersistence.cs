using System.Diagnostics.CodeAnalysis;
using Parquet;
using Parquet.Data;
using Parquet.Schema;
using PerfConverter.Entry;
using Temp.Schema;
using Temp.Core;

namespace PerfConverter.Persistence.ParquetDotNet;

public class ParquetTracePersistence : IBatchPersistence<TraceSampleEntry>
{
    readonly ParquetWriter _writer;
    readonly FileStream _fileStream;

    ulong[] _ids;
    ulong[] _perfIds;
    uint[] _pids;
    uint[] _tids;
    ulong[] _times;
    uint[] _cpus;
    uint[] _flags;
    ulong[] _ips;
    ulong[] _addrs;
    ulong[] _periods;
    ulong[] _insnCnts;
    ulong[] _cycCnts;
    ulong[] _weights;
    byte[] _cpumodes;
    byte[] _addrCorrelatesSyms;
    ulong[] _eventIds;
    uint[] _machinePids;
    uint[] _vcpus;

    ParquetTracePersistence(ParquetWriter writer, FileStream fileStream)
    {
        _writer = writer;
        _fileStream = fileStream;
        ResizeArrays(0);
    }

    public async Task PersistAsync(IReadOnlyCollection<TraceSampleEntry> batch)
    {
        if (batch.Count == 0) return;

        int count = batch.Count;

        if (count != _ids.Length)
        {
            ResizeArrays(count);
        }

        int i = 0;
        foreach (var entry in batch)
        {
            _ids[i] = entry.Id;
            _perfIds[i] = entry.PerfId;
            _pids[i] = entry.Pid;
            _tids[i] = entry.Tid;
            _times[i] = entry.Time;
            _flags[i] = (uint)entry.Flags;
            _cpus[i] = entry.Cpu;
            _ips[i] = entry.Ip;
            _addrs[i] = entry.Addr;
            _periods[i] = entry.Period;
            _insnCnts[i] = entry.InsnCnt;
            _cycCnts[i] = entry.CycCnt;
            _weights[i] = entry.Weight;
            _cpumodes[i] = entry.Cpumode;
            _addrCorrelatesSyms[i] = entry.AddrCorrelatesSym;
            _eventIds[i] = entry.EventId;
            _machinePids[i] = entry.MachinePid;
            _vcpus[i] = entry.Vcpu;
            i++;
        }

        var idColumn = new DataColumn(TraceSampleSchema.Id, _ids);
        var perfIdColumn = new DataColumn(TraceSampleSchema.PerfId, _perfIds);
        var pidColumn = new DataColumn(TraceSampleSchema.Pid, _pids);
        var tidColumn = new DataColumn(TraceSampleSchema.Tid, _tids);
        var timeColumn = new DataColumn(TraceSampleSchema.Time, _times);
        var cpuColumn = new DataColumn(TraceSampleSchema.Cpu, _cpus);
        var flagsColumn = new DataColumn(TraceSampleSchema.Flags, _flags);
        var ipColumn = new DataColumn(TraceSampleSchema.Ip, _ips);
        var addrColumn = new DataColumn(TraceSampleSchema.Addr, _addrs);
        var periodColumn = new DataColumn(TraceSampleSchema.Period, _periods);
        var insnCntColumn = new DataColumn(TraceSampleSchema.InsnCnt, _insnCnts);
        var cycCntColumn = new DataColumn(TraceSampleSchema.CycCnt, _cycCnts);
        var weightColumn = new DataColumn(TraceSampleSchema.Weight, _weights);
        var cpumodeColumn = new DataColumn(TraceSampleSchema.Cpumode, _cpumodes);
        var addrCorrelatesSymColumn = new DataColumn(TraceSampleSchema.AddrCorrelatesSym, _addrCorrelatesSyms);
        var eventIdColumn = new DataColumn(TraceSampleSchema.EventId, _eventIds);
        var machinePidColumn = new DataColumn(TraceSampleSchema.MachinePid, _machinePids);
        var vcpuColumn = new DataColumn(TraceSampleSchema.Vcpu, _vcpus);

        using var groupWriter = _writer.CreateRowGroup();

        await groupWriter.WriteColumnAsync(idColumn);
        await groupWriter.WriteColumnAsync(perfIdColumn);
        await groupWriter.WriteColumnAsync(pidColumn);
        await groupWriter.WriteColumnAsync(tidColumn);
        await groupWriter.WriteColumnAsync(timeColumn);
        await groupWriter.WriteColumnAsync(cpuColumn);
        await groupWriter.WriteColumnAsync(flagsColumn);
        await groupWriter.WriteColumnAsync(ipColumn);
        await groupWriter.WriteColumnAsync(addrColumn);
        await groupWriter.WriteColumnAsync(periodColumn);
        await groupWriter.WriteColumnAsync(insnCntColumn);
        await groupWriter.WriteColumnAsync(cycCntColumn);
        await groupWriter.WriteColumnAsync(weightColumn);
        await groupWriter.WriteColumnAsync(cpumodeColumn);
        await groupWriter.WriteColumnAsync(addrCorrelatesSymColumn);
        await groupWriter.WriteColumnAsync(eventIdColumn);
        await groupWriter.WriteColumnAsync(machinePidColumn);
        await groupWriter.WriteColumnAsync(vcpuColumn);
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync();
        await _fileStream.DisposeAsync();
    }

    [MemberNotNull(
        nameof(_ids),
        nameof(_perfIds),
        nameof(_pids),
        nameof(_tids),
        nameof(_times),
        nameof(_flags),
        nameof(_cpus),
        nameof(_ips),
        nameof(_addrs),
        nameof(_periods),
        nameof(_insnCnts),
        nameof(_cycCnts),
        nameof(_weights),
        nameof(_cpumodes),
        nameof(_addrCorrelatesSyms),
        nameof(_eventIds),
        nameof(_machinePids),
        nameof(_vcpus))]
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
        _addrCorrelatesSyms = new byte[newSize];
        _eventIds = new ulong[newSize];
        _machinePids = new uint[newSize];
        _vcpus = new uint[newSize];
    }

    public static async Task<IBatchPersistence<TraceSampleEntry>> Create(string basePath, CompressionMethod compressionMethod)
    {
        Directory.CreateDirectory(basePath);
        var filePath = Path.Combine(basePath, "tracesamples.parquet");

        var schema = TraceSampleSchema.Schema;

        var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite);
        var writer = await ParquetWriter.CreateAsync(schema, fileStream);
        writer.CompressionMethod = compressionMethod;

        return new ParquetTracePersistence(writer, fileStream);
    }
}