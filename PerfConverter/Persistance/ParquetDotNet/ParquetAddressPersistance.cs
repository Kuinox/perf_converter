using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Parquet;
using Parquet.Data;
using Parquet.Schema;
using PerfConverter.Entry;

namespace PerfConverter.Persistance.ParquetDotNet;

public class ParquetAddressPersistance : IBatchPersistance<AddressEntry>
{
    private readonly string _basePath;
    private readonly string _filePath;
    private readonly ParquetSchema _schema;
    
    private ParquetAddressPersistance(string basePath)
    {
        _basePath = basePath;
        _filePath = Path.Combine(basePath, "addresses.parquet");
        
        _schema = new ParquetSchema(
            new DataField<long>("Id"),
            new DataField<long>("TraceId"),
            new DataField<long>("Address"),
            new DataField<int>("Pid"),
            new DataField<bool>("IsIp"),
            new DataField<int>("Size"),
            new DataField<int>("Symoff"),
            new DataField<long>("SymStrId"),
            new DataField<long>("SymStart"),
            new DataField<long>("SymEnd"),
            new DataField<long>("Dso"),
            new DataField<byte>("SymBinding"),
            new DataField<byte>("Is64Bit"),
            new DataField<byte>("IsKernelIp"),
            new DataField<byte[]>("BuildId"),
            new DataField<byte>("Filtered"),
            new DataField<long>("Comm"),
            new DataField<long>("Priv")
        );
    }

    public async Task PersistAsync(IReadOnlyCollection<AddressEntry> batch)
    {
        int count = batch.Count;
        var ids = new long[count];
        var traceIds = new long[count];
        var addresses = new long[count];
        var pids = new int[count];
        var isIps = new bool[count];
        var sizes = new int[count];
        var symoffs = new int[count];
        var symStrIds = new long[count];
        var symStarts = new long[count];
        var symEnds = new long[count];
        var dsos = new long[count];
        var symBindings = new byte[count];
        var is64Bits = new byte[count];
        var isKernelIps = new byte[count];
        var buildIds = new byte[count][];
        var filtereds = new byte[count];
        var comms = new long[count];
        var privs = new long[count];

        int i = 0;
        foreach (var entry in batch)
        {
            ids[i] = (long)entry.Id;
            traceIds[i] = entry.TraceId;
            addresses[i] = (long)entry.Address;
            pids[i] = entry.Pid;
            isIps[i] = entry.IsIp;
            sizes[i] = entry.Size;
            symoffs[i] = entry.Symoff;
            symStrIds[i] = entry.SymStrId;
            symStarts[i] = (long)entry.SymStart;
            symEnds[i] = (long)entry.SymEnd;
            dsos[i] = entry.Dso;
            symBindings[i] = entry.SymBinding;
            is64Bits[i] = entry.Is64Bit;
            isKernelIps[i] = entry.IsKernelIp;
            buildIds[i] = entry.BuildId;
            filtereds[i] = entry.Filtered;
            comms[i] = entry.Comm;
            privs[i] = entry.Priv;
            i++;
        }

        bool fileExists = File.Exists(_filePath);
        
        using var fileStream = fileExists
            ? new FileStream(_filePath, FileMode.Open, FileAccess.ReadWrite)
            : new FileStream(_filePath, FileMode.Create, FileAccess.ReadWrite);
        
        using var writer = fileExists
            ? await ParquetWriter.CreateAsync(_schema, fileStream, append: true)
            : await ParquetWriter.CreateAsync(_schema, fileStream);
            
        using var groupWriter = writer.CreateRowGroup();

        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<long>("Id"), ids));
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<long>("TraceId"), traceIds));
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<long>("Address"), addresses));
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<int>("Pid"), pids));
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<bool>("IsIp"), isIps));
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<int>("Size"), sizes));
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<int>("Symoff"), symoffs));
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<long>("SymStrId"), symStrIds));
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<long>("SymStart"), symStarts));
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<long>("SymEnd"), symEnds));
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<long>("Dso"), dsos));
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<byte>("SymBinding"), symBindings));
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<byte>("Is64Bit"), is64Bits));
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<byte>("IsKernelIp"), isKernelIps));
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<byte[]>("BuildId"), buildIds));
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<byte>("Filtered"), filtereds));
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<long>("Comm"), comms));
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<long>("Priv"), privs));
    }

    public static IBatchPersistance<AddressEntry> Create(string basePath)
    {
        Directory.CreateDirectory(basePath);
        return new ParquetAddressPersistance(basePath);
    }
}