using Parquet;
using Parquet.Data;
using Parquet.Schema;
using PerfConverter.Entry;
using Temp.Core;

namespace PerfConverter.Persistence.ParquetDotNet;

public class ParquetAddressPersistence : IBatchPersistence<AddressEntry>
{
    readonly ParquetSchema _schema;
    readonly ParquetWriter _writer;
    readonly FileStream _fileStream;

    ulong[] _ids;
    ulong[] _traceIds;
    ulong[] _addresses;
    uint[] _pids;
    bool[] _isIps;
    uint[] _sizes;
    uint[] _symoffs;
    ulong[] _symStrIds;
    ulong[] _symStarts;
    ulong[] _symEnds;
    ulong[] _dsos;
    byte[] _symBindings;
    byte[] _is64Bits;
    byte[] _isKernelIps;
    byte[][] _buildIds;
    byte[] _filtereds;
    ulong[] _commsStrId;
    ulong[] _privs;


    ParquetAddressPersistence(ParquetSchema schema, ParquetWriter writer, FileStream fileStream)
    {
        ResizeArrays(0);
        _schema = schema;
        _writer = writer;
        _fileStream = fileStream;
    }

    public async Task PersistAsync(IReadOnlyCollection<AddressEntry> batch)
    {
        int count = batch.Count;

        ResizeArrays(count);

        int i = 0;
        foreach (var entry in batch)
        {
            _ids[i] = entry.Id;
            _traceIds[i] = entry.TraceId;
            _addresses[i] = entry.Address;
            _pids[i] = entry.Pid;
            _isIps[i] = entry.IsIp;
            _sizes[i] = entry.Size;
            _symoffs[i] = entry.Symoff;
            _symStrIds[i] = entry.SymStrId;
            _symStarts[i] = entry.SymStart;
            _symEnds[i] = entry.SymEnd;
            _dsos[i] = entry.Dso;
            _symBindings[i] = entry.SymBinding;
            _is64Bits[i] = entry.Is64Bit;
            _isKernelIps[i] = entry.IsKernelIp;
            _buildIds[i] = entry.BuildId ?? [];
            _filtereds[i] = entry.Filtered;
            _commsStrId[i] = entry.CommStrId;
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
                _buildIds[x] = [];
            }
        }
        var buildIdColumn = new DataColumn(_schema.DataFields[14], _buildIds);

        var filteredColumn = new DataColumn(_schema.DataFields[15], _filtereds);
        var commColumn = new DataColumn(_schema.DataFields[16], _commsStrId);
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
        nameof(_commsStrId),
        nameof(_privs))]
    void ResizeArrays(int newSize)
    {
        if (_ids != null && _ids.Length == newSize)
#pragma warning disable CS8774 // Member must have a non-null value when exiting.
            return;
#pragma warning restore CS8774 // Member must have a non-null value when exiting.

        _ids = new ulong[newSize];
        _traceIds = new ulong[newSize];
        _addresses = new ulong[newSize];
        _pids = new uint[newSize];
        _isIps = new bool[newSize];
        _sizes = new uint[newSize];
        _symoffs = new uint[newSize];
        _symStrIds = new ulong[newSize];
        _symStarts = new ulong[newSize];
        _symEnds = new ulong[newSize];
        _dsos = new ulong[newSize];
        _symBindings = new byte[newSize];
        _is64Bits = new byte[newSize];
        _isKernelIps = new byte[newSize];
        _buildIds = new byte[newSize][];
        _filtereds = new byte[newSize];
        _commsStrId = new ulong[newSize];
        _privs = new ulong[newSize];
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync();
        await _fileStream.DisposeAsync();
    }

    public static async Task<IBatchPersistence<AddressEntry>> Create(string basePath, CompressionMethod compressionMethod)
    {
        Directory.CreateDirectory(basePath);
        var filePath = Path.Combine(basePath, "addresses.parquet");

        var schema = new ParquetSchema(
            new DataField<ulong>("id"),
            new DataField<ulong>("traceId"),
            new DataField<ulong>("address"),
            new DataField<uint>("pid"),
            new DataField<bool>("isIp"),
            new DataField<uint>("size"),
            new DataField<uint>("symoff"),
            new DataField<ulong>("symStrId"),
            new DataField<ulong>("symStart"),
            new DataField<ulong>("symEnd"),
            new DataField<ulong>("dso"),
            new DataField<byte>("symBinding"),
            new DataField<byte>("is64Bit"),
            new DataField<byte>("isKernelIp"),
            new DataField<byte[]>("buildId"),
            new DataField<byte>("filtered"),
            new DataField<ulong>("commStrId"),
            new DataField<ulong>("priv")
        );


        var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite);
        var writer = await ParquetWriter.CreateAsync(schema, fileStream);

        writer.CompressionMethod = compressionMethod;

        return new ParquetAddressPersistence(schema, writer, fileStream);
    }
}