using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Parquet;
using Parquet.Data;
using Parquet.Schema;
using PerfConverter.Entry;

namespace PerfConverter.Persistance.ParquetDotNet;

public class ParquetTracePersistance : IBatchPersistance<TraceSampleEntry>
{
    private readonly string _basePath;
    private readonly string _filePath;
    private readonly ParquetSchema _schema;
    
    private ParquetTracePersistance(string basePath)
    {
        _basePath = basePath;
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
    }

    public async Task PersistAsync(IReadOnlyCollection<TraceSampleEntry> batch)
    {
        if (batch.Count == 0) return;
        
        // Pre-allocate arrays for better performance
        int count = batch.Count;
        var ids = new long[count];
        var pids = new int[count];
        var tids = new int[count];
        var times = new long[count];
        var cpus = new int[count];
        var ips = new long[count];
        var addrs = new long[count];
        var periods = new long[count];
        var insnCnts = new long[count];
        var cycCnts = new long[count];
        var weights = new long[count];
        var cpumodes = new byte[count];
        var addrCorrelatesSyms = new byte[count];
        var events = new string[count];
        var machinePids = new int[count];
        var vcpus = new int[count];

        // Fill arrays directly
        int i = 0;
        foreach (var entry in batch)
        {
            ids[i] = (long)entry.Id;
            pids[i] = entry.Pid;
            tids[i] = entry.Tid;
            times[i] = (long)entry.Time;
            cpus[i] = entry.Cpu;
            ips[i] = entry.Ip;
            addrs[i] = entry.Addr;
            periods[i] = (long)entry.Period;
            insnCnts[i] = (long)entry.InsnCnt;
            cycCnts[i] = (long)entry.CycCnt;
            weights[i] = (long)entry.Weight;
            cpumodes[i] = entry.Cpumode;
            addrCorrelatesSyms[i] = entry.AddrCorrelatesSym;
            events[i] = entry.Event ?? string.Empty;
            machinePids[i] = entry.MachinePid;
            vcpus[i] = entry.Vcpu;
            i++;
        }

        // Parquet files don't support true appending, so we create new row groups
        bool fileExists = File.Exists(_filePath);
        
        // When appending, open with ReadWrite to preserve existing data and add new row group
        using var fileStream = fileExists
            ? new FileStream(_filePath, FileMode.Open, FileAccess.ReadWrite)
            : new FileStream(_filePath, FileMode.Create, FileAccess.ReadWrite);
        
        using var writer = fileExists
            ? await ParquetWriter.CreateAsync(_schema, fileStream, append: true)
            : await ParquetWriter.CreateAsync(_schema, fileStream);
            
        using var groupWriter = writer.CreateRowGroup();

        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<long>("Id"), ids));
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<int>("Pid"), pids));
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<int>("Tid"), tids));
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<long>("Time"), times));
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<int>("Cpu"), cpus));
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<long>("Ip"), ips));
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<long>("Addr"), addrs));
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<long>("Period"), periods));
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<long>("InsnCnt"), insnCnts));
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<long>("CycCnt"), cycCnts));
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<long>("Weight"), weights));
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<byte>("Cpumode"), cpumodes));
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<byte>("AddrCorrelatesSym"), addrCorrelatesSyms));
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<string>("Event"), events));
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<int>("MachinePid"), machinePids));
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<int>("Vcpu"), vcpus));
    }

    public static IBatchPersistance<TraceSampleEntry> Create(string basePath)
    {
        Directory.CreateDirectory(basePath);
        return new ParquetTracePersistance(basePath);
    }
}