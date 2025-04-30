using ParquetSharp;
using PerfConverter.Entry;

namespace PerfConverter.Persistance.ParquetSharp;

public class ParquetSharpAddressPersistance : IBatchPersistance<AddressEntry>
{
    readonly ParquetFileWriter _writer;

    ulong[] _ids;
    long[] _traceIds;
    ulong[] _addresses;
    int[] _pids;
    bool[] _isIps;
    int[] _sizes;
    int[] _symoffs;
    long[] _symStrIds;
    ulong[] _symStarts;
    ulong[] _symEnds;
    long[] _dsos;
    byte[] _symBindings;
    byte[] _is64Bits;
    byte[] _isKernelIps;
    byte[][] _buildIds;
    byte[] _filtereds;
    long[] _comms;
    long[] _privs;

    ParquetSharpAddressPersistance(int batchSize, ParquetFileWriter writer)
    {
        _writer = writer;
        ResizeArrays(batchSize);
    }

    public Task PersistAsync(IReadOnlyCollection<AddressEntry> batch)
    {
        ResizeArrays(batch.Count);

        var i = 0;
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
            _buildIds[i] = entry.BuildId;
            _filtereds[i] = entry.Filtered;
            _comms[i] = entry.Comm;
            _privs[i] = entry.Priv;
            i++;
        }

        try
        {
            using var rowGroup = _writer.AppendRowGroup();
            using (var idWriter = rowGroup.NextColumn().LogicalWriter<ulong>())
                idWriter.WriteBatch(_ids);

            using (var traceIdWriter = rowGroup.NextColumn().LogicalWriter<long>())
                traceIdWriter.WriteBatch(_traceIds);

            using (var addressWriter = rowGroup.NextColumn().LogicalWriter<ulong>())
                addressWriter.WriteBatch(_addresses);

            using (var pidWriter = rowGroup.NextColumn().LogicalWriter<int>())
                pidWriter.WriteBatch(_pids);

            using (var isIpWriter = rowGroup.NextColumn().LogicalWriter<bool>())
                isIpWriter.WriteBatch(_isIps);

            using (var sizeWriter = rowGroup.NextColumn().LogicalWriter<int>())
                sizeWriter.WriteBatch(_sizes);

            using (var symoffWriter = rowGroup.NextColumn().LogicalWriter<int>())
                symoffWriter.WriteBatch(_symoffs);

            using (var symStrIdWriter = rowGroup.NextColumn().LogicalWriter<long>())
                symStrIdWriter.WriteBatch(_symStrIds);

            using (var symStartWriter = rowGroup.NextColumn().LogicalWriter<ulong>())
                symStartWriter.WriteBatch(_symStarts);

            using (var symEndWriter = rowGroup.NextColumn().LogicalWriter<ulong>())
                symEndWriter.WriteBatch(_symEnds);

            using (var dsoWriter = rowGroup.NextColumn().LogicalWriter<long>())
                dsoWriter.WriteBatch(_dsos);

            using (var symBindingWriter = rowGroup.NextColumn().LogicalWriter<byte>())
                symBindingWriter.WriteBatch(_symBindings);

            using (var is64BitWriter = rowGroup.NextColumn().LogicalWriter<byte>())
                is64BitWriter.WriteBatch(_is64Bits);

            using (var isKernelIpWriter = rowGroup.NextColumn().LogicalWriter<byte>())
                isKernelIpWriter.WriteBatch(_isKernelIps);

            using (var buildIdWriter = rowGroup.NextColumn().LogicalWriter<byte[]>())
                buildIdWriter.WriteBatch(_buildIds);

            using (var filteredWriter = rowGroup.NextColumn().LogicalWriter<byte>())
                filteredWriter.WriteBatch(_filtereds);

            using (var commWriter = rowGroup.NextColumn().LogicalWriter<long>())
                commWriter.WriteBatch(_comms);

            using (var privWriter = rowGroup.NextColumn().LogicalWriter<long>())
                privWriter.WriteBatch(_privs);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error writing addresses to parquet: {ex.Message}");
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

        _ids = new ulong[newSize];
        _traceIds = new long[newSize];
        _addresses = new ulong[newSize];
        _pids = new int[newSize];
        _isIps = new bool[newSize];
        _sizes = new int[newSize];
        _symoffs = new int[newSize];
        _symStrIds = new long[newSize];
        _symStarts = new ulong[newSize];
        _symEnds = new ulong[newSize];
        _dsos = new long[newSize];
        _symBindings = new byte[newSize];
        _is64Bits = new byte[newSize];
        _isKernelIps = new byte[newSize];
        _buildIds = new byte[newSize][];
        _filtereds = new byte[newSize];
        _comms = new long[newSize];
        _privs = new long[newSize];
    }

    public static IBatchPersistance<AddressEntry> Create(string basePath, int batchSize, Compression compressionMethod)
    {
        Directory.CreateDirectory(basePath);

        var filePath = Path.Combine(basePath, "addresses.parquet");
        var columns = new Column[]
        {
            new Column<ulong>("Id"),
            new Column<long>("TraceId"),
            new Column<ulong>("Address"),
            new Column<int>("Pid"),
            new Column<bool>("IsIp"),
            new Column<int>("Size"),
            new Column<int>("Symoff"),
            new Column<long>("SymStrId"),
            new Column<ulong>("SymStart"),
            new Column<ulong>("SymEnd"),
            new Column<long>("Dso"),
            new Column<byte>("SymBinding"),
            new Column<byte>("Is64Bit"),
            new Column<byte>("IsKernelIp"),
            new Column<byte[]>("BuildId"),
            new Column<byte>("Filtered"),
            new Column<long>("Comm"),
            new Column<long>("Priv")
        };

        var writer = new ParquetFileWriter(filePath, columns,  compressionMethod);
        return new ParquetSharpAddressPersistance(batchSize, writer);
    }
}