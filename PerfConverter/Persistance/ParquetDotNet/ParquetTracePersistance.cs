using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;
using Parquet;
using Parquet.Data;
using Parquet.Schema;
using PerfConverter.Entry;

namespace PerfConverter.Persistance.ParquetDotNet;

public class ParquetTracePersistance : IBatchPersistance<TraceSampleEntry>
{
    private readonly string _filePath;
    private readonly ParquetSchema _schema;
    
    private long[] _ids;
    private int[] _pids;
    private int[] _tids;
    private long[] _times;
    private int[] _cpus;
    private long[] _ips;
    private long[] _addrs;
    private long[] _periods;
    private long[] _insnCnts;
    private long[] _cycCnts;
    private long[] _weights;
    private byte[] _cpumodes;
    private byte[] _addrCorrelatesSyms;
    private string[] _events;
    private int[] _machinePids;
    private int[] _vcpus;
    
    private ParquetTracePersistance(string basePath, int batchSize)
    {
        _filePath = Path.Combine(basePath, "tracesamples.parquet");
        
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

        bool fileExists = File.Exists(_filePath);
        
        using var fileStream = fileExists
            ? new FileStream(_filePath, FileMode.Open, FileAccess.ReadWrite)
            : new FileStream(_filePath, FileMode.Create, FileAccess.ReadWrite);

        using var writer = fileExists
            ? await ParquetWriter.CreateAsync(_schema, fileStream, append: true)
            : await ParquetWriter.CreateAsync(_schema, fileStream);

        writer.CompressionMethod = CompressionMethod.None;

        using var groupWriter = writer.CreateRowGroup();
        Console.Error.WriteLine("Writing Ids");
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<long>("Id"), _ids));
        Console.Error.WriteLine("Writing Pids");
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<int>("Pid"), _pids));
        Console.Error.WriteLine("Writing Tids");
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<int>("Tid"), _tids));
        Console.Error.WriteLine("Writing Times");
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<long>("Time"), _times));
        Console.Error.WriteLine("Writing Cpus");
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<int>("Cpu"), _cpus));
        Console.Error.WriteLine("Writing Ips");
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<long>("Ip"), _ips));
        Console.Error.WriteLine("Writing Addrs");
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<long>("Addr"), _addrs));
        Console.Error.WriteLine("Writing Periods");
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<long>("Period"), _periods));
        Console.Error.WriteLine("Writing InsnCnt");
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<long>("InsnCnt"), _insnCnts));
        Console.Error.WriteLine("Writing CycCnt");
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<long>("CycCnt"), _cycCnts));
        Console.Error.WriteLine("Writing Weights");
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<long>("Weight"), _weights));
        Console.Error.WriteLine("Writing Cpumode");
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<byte>("Cpumode"), _cpumodes));
        Console.Error.WriteLine("Writing AddrCorrelatesSym");
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<byte>("AddrCorrelatesSym"), _addrCorrelatesSyms));
        Console.Error.WriteLine("Writing Events");
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<string>("Event"), _events));
        Console.Error.WriteLine("Writing MachinePids");
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<int>("MachinePid"), _machinePids));
        Console.WriteLine("Writing Vcpus");
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<int>("Vcpu"), _vcpus));
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
    private void ResizeArrays(int newSize)
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

    public static IBatchPersistance<TraceSampleEntry> Create(string basePath, int batchSize)
    {
        Directory.CreateDirectory(basePath);
        return new ParquetTracePersistance(basePath, batchSize);
    }
}