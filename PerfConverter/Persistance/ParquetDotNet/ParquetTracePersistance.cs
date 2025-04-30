using System.Diagnostics.CodeAnalysis;
using Parquet;
using Parquet.Data;
using Parquet.Schema;
using PerfConverter.Entry;

namespace PerfConverter.Persistance.ParquetDotNet;

public class ParquetTracePersistance : IBatchPersistance<TraceSampleEntry>
{
    readonly ParquetSchema _schema;
    ParquetWriter _writer;
    FileStream _fileStream;

    long[] _ids;
    int[] _pids;
    int[] _tids;
    long[] _times;
    int[] _cpus;
    long[] _ips;
    long[] _addrs;
    long[] _periods;
    long[] _insnCnts;
    long[] _cycCnts;
    long[] _weights;
    byte[] _cpumodes;
    byte[] _addrCorrelatesSyms;
    string[] _events;
    int[] _machinePids;
    int[] _vcpus;

    ParquetTracePersistance(int batchSize, ParquetWriter writer, FileStream fileStream)
    {
        _schema = new ParquetSchema(
            new DataField<long>("Id"),
            new DataField<int>("Pid"),
            new DataField<int>("Tid"),
            new DataField<long>("Time"),
            new DataField<int>("Cpu"),
            new DataField<long>("Ip"),
            new DataField<long>("Addr"),
            new DataField<long>("Period"),
            new DataField<long>("InsnCnt"),
            new DataField<long>("CycCnt"),
            new DataField<long>("Weight"),
            new DataField<byte>("Cpumode"),
            new DataField<byte>("AddrCorrelatesSym"),
            new DataField<string>("Event"),
            new DataField<int>("MachinePid"),
            new DataField<int>("Vcpu")
        );

        _writer = writer;
        _fileStream = fileStream;
        ResizeArrays(batchSize);
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
            _ids[i] = (long)entry.Id;
            _pids[i] = entry.Pid;
            _tids[i] = entry.Tid;
            _times[i] = (long)entry.Time;
            _cpus[i] = entry.Cpu;
            _ips[i] = entry.Ip;
            _addrs[i] = entry.Addr;
            _periods[i] = (long)entry.Period;
            _insnCnts[i] = (long)entry.InsnCnt;
            _cycCnts[i] = (long)entry.CycCnt;
            _weights[i] = (long)entry.Weight;
            _cpumodes[i] = entry.Cpumode;
            _addrCorrelatesSyms[i] = entry.AddrCorrelatesSym;
            _events[i] = entry.Event ?? string.Empty;
            _machinePids[i] = entry.MachinePid;
            _vcpus[i] = entry.Vcpu;
            i++;
        }

        // Create columns using the schema fields
        var idColumn = new DataColumn(_schema.DataFields[0], _ids);
        var pidColumn = new DataColumn(_schema.DataFields[1], _pids);
        var tidColumn = new DataColumn(_schema.DataFields[2], _tids);
        var timeColumn = new DataColumn(_schema.DataFields[3], _times);
        var cpuColumn = new DataColumn(_schema.DataFields[4], _cpus);
        var ipColumn = new DataColumn(_schema.DataFields[5], _ips);
        var addrColumn = new DataColumn(_schema.DataFields[6], _addrs);
        var periodColumn = new DataColumn(_schema.DataFields[7], _periods);
        var insnCntColumn = new DataColumn(_schema.DataFields[8], _insnCnts);
        var cycCntColumn = new DataColumn(_schema.DataFields[9], _cycCnts);
        var weightColumn = new DataColumn(_schema.DataFields[10], _weights);
        var cpumodeColumn = new DataColumn(_schema.DataFields[11], _cpumodes);
        var addrCorrelatesSymColumn = new DataColumn(_schema.DataFields[12], _addrCorrelatesSyms);
        var eventColumn = new DataColumn(_schema.DataFields[13], _events);
        var machinePidColumn = new DataColumn(_schema.DataFields[14], _machinePids);
        var vcpuColumn = new DataColumn(_schema.DataFields[15], _vcpus);

        using var groupWriter = _writer.CreateRowGroup();

        await groupWriter.WriteColumnAsync(idColumn);
        await groupWriter.WriteColumnAsync(pidColumn);
        await groupWriter.WriteColumnAsync(tidColumn);
        await groupWriter.WriteColumnAsync(timeColumn);
        await groupWriter.WriteColumnAsync(cpuColumn);
        await groupWriter.WriteColumnAsync(ipColumn);
        await groupWriter.WriteColumnAsync(addrColumn);
        await groupWriter.WriteColumnAsync(periodColumn);
        await groupWriter.WriteColumnAsync(insnCntColumn);
        await groupWriter.WriteColumnAsync(cycCntColumn);
        await groupWriter.WriteColumnAsync(weightColumn);
        await groupWriter.WriteColumnAsync(cpumodeColumn);
        await groupWriter.WriteColumnAsync(addrCorrelatesSymColumn);
        await groupWriter.WriteColumnAsync(eventColumn);
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
        _ids = new long[newSize];
        _pids = new int[newSize];
        _tids = new int[newSize];
        _times = new long[newSize];
        _cpus = new int[newSize];
        _ips = new long[newSize];
        _addrs = new long[newSize];
        _periods = new long[newSize];
        _insnCnts = new long[newSize];
        _cycCnts = new long[newSize];
        _weights = new long[newSize];
        _cpumodes = new byte[newSize];
        _addrCorrelatesSyms = new byte[newSize];
        _events = new string[newSize];
        _machinePids = new int[newSize];
        _vcpus = new int[newSize];
    }

    public static async Task<IBatchPersistance<TraceSampleEntry>> Create(string basePath, int batchSize, CompressionMethod compressionMethod)
    {
        Directory.CreateDirectory(basePath);
        var filePath = Path.Combine(basePath, "tracesamples.parquet");

        var schema = new ParquetSchema(
            new DataField<long>("Id"),
            new DataField<int>("Pid"),
            new DataField<int>("Tid"),
            new DataField<long>("Time"),
            new DataField<int>("Cpu"),
            new DataField<long>("Ip"),
            new DataField<long>("Addr"),
            new DataField<long>("Period"),
            new DataField<long>("InsnCnt"),
            new DataField<long>("CycCnt"),
            new DataField<long>("Weight"),
            new DataField<byte>("Cpumode"),
            new DataField<byte>("AddrCorrelatesSym"),
            new DataField<string>("Event"),
            new DataField<int>("MachinePid"),
            new DataField<int>("Vcpu")
        );

        var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite);
        var writer = await ParquetWriter.CreateAsync(schema, fileStream);
        writer.CompressionMethod = compressionMethod;

        return new ParquetTracePersistance(batchSize, writer, fileStream);
    }
}