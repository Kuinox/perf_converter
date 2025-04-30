using ParquetSharp;
using PerfConverter.Entry;

namespace PerfConverter.Persistance.ParquetSharp;

public class ParquetSharpSymPersistance : IBatchPersistance<SymbolEntry>
{
    readonly ParquetFileWriter _writer;
    long[] _ids;
    string[] _symbols;


    ParquetSharpSymPersistance(int batchSize, ParquetFileWriter writer)
    {
        _writer = writer;
        ResizeArrays(batchSize);
    }

    public async Task PersistAsync(IReadOnlyCollection<SymbolEntry> batch)
    {
        ResizeArrays(batch.Count);

        var i = 0;
        foreach (var entry in batch)
        {
            _ids[i] = entry.Id;
            _symbols[i] = entry.Symbol;
            i++;
        }

        try
        {
            using var rowGroup = _writer.AppendRowGroup();
            using (var idWriter = rowGroup.NextColumn().LogicalWriter<long>())
            {
                idWriter.WriteBatch(_ids);
            }

            using (var symbolWriter = rowGroup.NextColumn().LogicalWriter<string>())
            {
                symbolWriter.WriteBatch(_symbols);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error writing symbols to parquet: {ex.Message}");
            throw;
        }

        await Task.CompletedTask; // Keep the async signature
    }

    public ValueTask DisposeAsync()
    {
        _writer.Close();
        _writer.Dispose();
        return ValueTask.CompletedTask;
    }

    [System.Diagnostics.CodeAnalysis.MemberNotNull(
        nameof(_ids),
        nameof(_symbols))]
    void ResizeArrays(int newSize)
    {
        if (_ids != null && _ids.Length == newSize)
#pragma warning disable CS8774 // Member must have a non-null value when exiting.
            return;
#pragma warning restore CS8774 // Member must have a non-null value when exiting.

        _ids = new long[newSize];
        _symbols = new string[newSize];
    }

    public static IBatchPersistance<SymbolEntry> Create(string basePath, int batchSize, Compression compressionMethod)
    {
        Directory.CreateDirectory(basePath);

        var filePath = Path.Combine(basePath, "symbols.parquet");
        var columns = new Column[]
        {
            new Column<long>("Id"),
            new Column<string>("Symbol")
        };

        var writer = new ParquetFileWriter(filePath, columns, compressionMethod);

        return new ParquetSharpSymPersistance(batchSize, writer);
    }
}