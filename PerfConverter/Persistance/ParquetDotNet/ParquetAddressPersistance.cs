using Parquet;
using Parquet.Data;
using Parquet.Schema;
using PerfConverter.Entry;

namespace PerfConverter.Persistance.ParquetDotNet;

public class ParquetAddressPersistance : IBatchPersistance<AddressEntry>
{
    private readonly string _filePath;
    private readonly ParquetSchema _schema;
    
    private long[]? _ids;
    private long[]? _traceIds;
    private long[]? _addresses;
    private int[]? _pids;
    private bool[]? _isIps;
    private int[]? _sizes;
    private int[]? _symoffs;
    private long[]? _symStrIds;
    private long[]? _symStarts;
    private long[]? _symEnds;
    private long[]? _dsos;
    private byte[]? _symBindings;
    private byte[]? _is64Bits;
    private byte[]? _isKernelIps;
    private byte[][]? _buildIds;
    private byte[]? _filtereds;
    private long[]? _comms;
    private long[]? _privs;
    
    private ParquetAddressPersistance(string basePath, int batchSize)
    {
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
        
        ResizeArrays(batchSize);
    }

    public async Task PersistAsync(IReadOnlyCollection<AddressEntry> batch)
    {
        int count = batch.Count;
        
        ResizeArrays(count);
        
        int i = 0;
        foreach (var entry in batch)
        {
            _ids[i] = (long)entry.Id;
            _traceIds[i] = entry.TraceId;
            _addresses[i] = (long)entry.Address;
            _pids[i] = entry.Pid;
            _isIps[i] = entry.IsIp;
            _sizes[i] = entry.Size;
            _symoffs[i] = entry.Symoff;
            _symStrIds[i] = entry.SymStrId;
            _symStarts[i] = (long)entry.SymStart;
            _symEnds[i] = (long)entry.SymEnd;
            _dsos[i] = entry.Dso;
            _symBindings[i] = entry.SymBinding;
            _is64Bits[i] = entry.Is64Bit;
            _isKernelIps[i] = entry.IsKernelIp;
            _buildIds[i] = entry.BuildId;
            _filtereds[i] = entry.Filtered;
            _comms[i] = entry.Comm;
            _privs[i] = entry.Priv;
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

        await groupWriter.WriteColumnAsync(new DataColumn(_schema.DataFields[0], _ids));
        await groupWriter.WriteColumnAsync(new DataColumn(_schema.DataFields[1], _traceIds));
        await groupWriter.WriteColumnAsync(new DataColumn(_schema.DataFields[2], _addresses));
        await groupWriter.WriteColumnAsync(new DataColumn(_schema.DataFields[3], _pids));
        await groupWriter.WriteColumnAsync(new DataColumn(_schema.DataFields[4], _isIps));
        await groupWriter.WriteColumnAsync(new DataColumn(_schema.DataFields[5], _sizes));
        await groupWriter.WriteColumnAsync(new DataColumn(_schema.DataFields[6], _symoffs));
        await groupWriter.WriteColumnAsync(new DataColumn(_schema.DataFields[7], _symStrIds));
        await groupWriter.WriteColumnAsync(new DataColumn(_schema.DataFields[8], _symStarts));
        await groupWriter.WriteColumnAsync(new DataColumn(_schema.DataFields[9], _symEnds));
        await groupWriter.WriteColumnAsync(new DataColumn(_schema.DataFields[10], _dsos));
        await groupWriter.WriteColumnAsync(new DataColumn(_schema.DataFields[11], _symBindings));
        await groupWriter.WriteColumnAsync(new DataColumn(_schema.DataFields[12], _is64Bits));
        await groupWriter.WriteColumnAsync(new DataColumn(_schema.DataFields[13], _isKernelIps));
        await groupWriter.WriteColumnAsync(new DataColumn(_schema.DataFields[14], _buildIds));
        await groupWriter.WriteColumnAsync(new DataColumn(_schema.DataFields[15], _filtereds));
        await groupWriter.WriteColumnAsync(new DataColumn(_schema.DataFields[16], _comms));
        await groupWriter.WriteColumnAsync(new DataColumn(_schema.DataFields[17], _privs));
    }

    [System.Diagnostics.CodeAnalysis.MemberNotNull(
        nameof(_ids),
        nameof(_traceIds),
        nameof(_addresses),
        nameof(_pids),
        nameof(_isIps),
        nameof(_sizes),
        nameof(_symoffs),
        nameof(_symStrIds),
        nameof(_symStarts),
        nameof(_symEnds),
        nameof(_dsos),
        nameof(_symBindings),
        nameof(_is64Bits),
        nameof(_isKernelIps),
        nameof(_buildIds),
        nameof(_filtereds),
        nameof(_comms),
        nameof(_privs))]
    private void ResizeArrays(int newSize)
    {
        if(_ids != null && _ids.Length == newSize)
#pragma warning disable CS8774 // Member must have a non-null value when exiting.
            return;
#pragma warning restore CS8774 // Member must have a non-null value when exiting.

        _ids = new long[newSize];
        _traceIds = new long[newSize];
        _addresses = new long[newSize];
        _pids = new int[newSize];
        _isIps = new bool[newSize];
        _sizes = new int[newSize];
        _symoffs = new int[newSize];
        _symStrIds = new long[newSize];
        _symStarts = new long[newSize];
        _symEnds = new long[newSize];
        _dsos = new long[newSize];
        _symBindings = new byte[newSize];
        _is64Bits = new byte[newSize];
        _isKernelIps = new byte[newSize];
        _buildIds = new byte[newSize][];
        _filtereds = new byte[newSize];
        _comms = new long[newSize];
        _privs = new long[newSize];
    }

    public static IBatchPersistance<AddressEntry> Create(string basePath, int batchSize)
    {
        Directory.CreateDirectory(basePath);
        return new ParquetAddressPersistance(basePath, batchSize);
    }
}