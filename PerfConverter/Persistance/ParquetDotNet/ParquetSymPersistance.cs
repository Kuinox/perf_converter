using Parquet;
using Parquet.Data;
using Parquet.Schema;
using PerfConverter.Entry;

namespace PerfConverter.Persistance.ParquetDotNet;

public class ParquetSymPersistance : IBatchPersistance<SymbolEntry>
{
    readonly ParquetSchema _schema;
    readonly ParquetWriter _writer;
    readonly FileStream _fileStream;
    long[]? _ids;
    string[]? _symbols;


    ParquetSymPersistance(string basePath, int batchSize, CompressionMethod compressionMethod, ParquetWriter writer, FileStream fileStream)
    {
        _schema = new ParquetSchema(
            new DataField<long>("Id"),
            new DataField<string>("Symbol")
        );

        _writer = writer;
        _fileStream = fileStream;
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

        var idColumn = new DataColumn(_schema.DataFields[0], _ids);
        var symbolColumn = new DataColumn(_schema.DataFields[1], _symbols);

        using var groupWriter = _writer.CreateRowGroup();

        await groupWriter.WriteColumnAsync(idColumn);
        await groupWriter.WriteColumnAsync(symbolColumn);
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync();
        await _fileStream.DisposeAsync();
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

    public static async Task<IBatchPersistance<SymbolEntry>> Create(string basePath, int batchSize, CompressionMethod compressionMethod)
    {
        Directory.CreateDirectory(basePath);
        var filePath = Path.Combine(basePath, "symbols.parquet");

        var schema = new ParquetSchema(
            new DataField<long>("Id"),
            new DataField<string>("Symbol")
        );

        var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite);

        var writer = await ParquetWriter.CreateAsync(schema, fileStream);

        writer.CompressionMethod = compressionMethod;

        return new ParquetSymPersistance(basePath, batchSize, compressionMethod, writer, fileStream);
    }
}