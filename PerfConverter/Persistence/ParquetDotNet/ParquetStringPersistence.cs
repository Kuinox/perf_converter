using Parquet;
using Parquet.Data;
using Parquet.Schema;
using PerfConverter.Entry;
using Temp.Schema;
using Temp.Core;

namespace PerfConverter.Persistence.ParquetDotNet;

public class ParquetStringPersistence : IBatchPersistence<StringEntry>
{
    readonly ParquetWriter _writer;
    readonly FileStream _fileStream;
    ulong[]? _ids;
    string[]? _symbols;


    ParquetStringPersistence(ParquetWriter writer, FileStream fileStream)
    {
        _writer = writer;
        _fileStream = fileStream;
        ResizeArrays(0);
    }

    public async Task PersistAsync(IReadOnlyCollection<StringEntry> batch)
    {
        ResizeArrays(batch.Count);

        var i = 0;
        foreach (var entry in batch)
        {
            _ids[i] = entry.Id;
            _symbols[i] = entry.Symbol;
            i++;
        }

        var idColumn = new DataColumn(DictionarySchema.Id, _ids);
        var symbolColumn = new DataColumn(DictionarySchema.Symbol, _symbols);

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

        _ids = new ulong[newSize];
        _symbols = new string[newSize];
    }

    public static async Task<IBatchPersistence<StringEntry>> Create(string basePath, string fileName, CompressionMethod compressionMethod)
    {
        Directory.CreateDirectory(basePath);
        var filePath = Path.Combine(basePath, fileName);

        var schema = DictionarySchema.Schema;

        var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite);

        var writer = await ParquetWriter.CreateAsync(schema, fileStream);

        writer.CompressionMethod = compressionMethod;

        return new ParquetStringPersistence(writer, fileStream);
    }
}