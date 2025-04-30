using Parquet;
using Parquet.Data;
using Parquet.Schema;
using PerfConverter.Entry;

namespace PerfConverter.Persistance.ParquetDotNet;

public class ParquetAddressPersistance : IBatchPersistance<AddressEntry>
{
    readonly ParquetSchema _schema;
    readonly ParquetWriter _writer;
    readonly FileStream _fileStream;

    long[] _ids;
    long[] _traceIds;
    long[] _addresses;
    int[] _pids;
    bool[] _isIps;
    int[] _sizes;
    int[] _symoffs;
    long[] _symStrIds;
    long[] _symStarts;
    long[] _symEnds;
    long[] _dsos;
    byte[] _symBindings;
    byte[] _is64Bits;
    byte[] _isKernelIps;
    byte[][] _buildIds;
    byte[] _filtereds;
    long[] _comms;
    long[] _privs;


    ParquetAddressPersistance(string basePath, int batchSize, CompressionMethod compressionMethod, ParquetWriter writer, FileStream fileStream)
    {
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

        _writer = writer;
        _fileStream = fileStream;
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
            _buildIds[i] = entry.BuildId ?? Array.Empty<byte>();
            _filtereds[i] = entry.Filtered;
            _comms[i] = entry.Comm;
            _privs[i] = entry.Priv;
            i++;
        }

        var idColumn = new DataColumn(_schema.DataFields[0], _ids);
        var traceIdColumn = new DataColumn(_schema.DataFields[1], _traceIds);
        var addressColumn = new DataColumn(_schema.DataFields[2], _addresses);
        var pidColumn = new DataColumn(_schema.DataFields[3], _pids);
        var isIpColumn = new DataColumn(_schema.DataFields[4], _isIps);
        var sizeColumn = new DataColumn(_schema.DataFields[5], _sizes);
        var symoffColumn = new DataColumn(_schema.DataFields[6], _symoffs);
        var symStrIdColumn = new DataColumn(_schema.DataFields[7], _symStrIds);
        var symStartColumn = new DataColumn(_schema.DataFields[8], _symStarts);
        var symEndColumn = new DataColumn(_schema.DataFields[9], _symEnds);
        var dsoColumn = new DataColumn(_schema.DataFields[10], _dsos);
        var symBindingColumn = new DataColumn(_schema.DataFields[11], _symBindings);
        var is64BitColumn = new DataColumn(_schema.DataFields[12], _is64Bits);
        var isKernelIpColumn = new DataColumn(_schema.DataFields[13], _isKernelIps);

        for (int x = 0; x < _buildIds.Length; x++)
        {
            if (_buildIds[x] == null)
            {
                _buildIds[x] = Array.Empty<byte>();
            }
        }
        var buildIdColumn = new DataColumn(_schema.DataFields[14], _buildIds);

        var filteredColumn = new DataColumn(_schema.DataFields[15], _filtereds);
        var commColumn = new DataColumn(_schema.DataFields[16], _comms);
        var privColumn = new DataColumn(_schema.DataFields[17], _privs);

        using var groupWriter = _writer.CreateRowGroup();

        await groupWriter.WriteColumnAsync(idColumn);
        await groupWriter.WriteColumnAsync(traceIdColumn);
        await groupWriter.WriteColumnAsync(addressColumn);
        await groupWriter.WriteColumnAsync(pidColumn);
        await groupWriter.WriteColumnAsync(isIpColumn);
        await groupWriter.WriteColumnAsync(sizeColumn);
        await groupWriter.WriteColumnAsync(symoffColumn);
        await groupWriter.WriteColumnAsync(symStrIdColumn);
        await groupWriter.WriteColumnAsync(symStartColumn);
        await groupWriter.WriteColumnAsync(symEndColumn);
        await groupWriter.WriteColumnAsync(dsoColumn);
        await groupWriter.WriteColumnAsync(symBindingColumn);
        await groupWriter.WriteColumnAsync(is64BitColumn);
        await groupWriter.WriteColumnAsync(isKernelIpColumn);
        await groupWriter.WriteColumnAsync(buildIdColumn);
        await groupWriter.WriteColumnAsync(filteredColumn);
        await groupWriter.WriteColumnAsync(commColumn);
        await groupWriter.WriteColumnAsync(privColumn);
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync();
        await _fileStream.DisposeAsync();
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
void ResizeArrays(int newSize)
    {
        if (_ids != null && _ids.Length == newSize)
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

    public static async Task<IBatchPersistance<AddressEntry>> Create(string basePath, int batchSize, CompressionMethod compressionMethod)
    {
        Directory.CreateDirectory(basePath);
        var filePath = Path.Combine(basePath, "addresses.parquet");

        var schema = new ParquetSchema(
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


        var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite);
        var writer = await ParquetWriter.CreateAsync(schema, fileStream);

        writer.CompressionMethod = compressionMethod;

        return new ParquetAddressPersistance(basePath, batchSize, compressionMethod, writer, fileStream);
    }
}