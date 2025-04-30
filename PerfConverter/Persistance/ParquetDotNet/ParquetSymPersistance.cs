using System.Collections.Generic;
using System.IO;
using Parquet;
using Parquet.Data;
using Parquet.Schema;
using PerfConverter.Entry;

namespace PerfConverter.Persistance.ParquetDotNet;

public class ParquetSymPersistance : IBatchPersistance<SymbolEntry>
{
    readonly string _filePath;
    readonly ParquetSchema _schema;
    CompressionMethod _compressionMethod;
    long[]? _ids;
    string[]? _symbols;

    private ParquetSymPersistance(string basePath, int batchSize, CompressionMethod compressionMethod)
    {
        _filePath = Path.Combine(basePath, "symbols.parquet");

        _schema = new ParquetSchema(
            new DataField<long>("Id"),
            new DataField<string>("Symbol")
        );

        ResizeArrays(batchSize);
        _compressionMethod = compressionMethod;
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

        var fileExists = File.Exists(_filePath);

        await using var fileStream = fileExists
            ? new FileStream(_filePath, FileMode.Open, FileAccess.ReadWrite)
            : new FileStream(_filePath, FileMode.Create, FileAccess.ReadWrite);

        await using var writer = fileExists
            ? await ParquetWriter.CreateAsync(_schema, fileStream, append: true)
            : await ParquetWriter.CreateAsync(_schema, fileStream);

        writer.CompressionMethod = _compressionMethod;

        var idColumn = new DataColumn(_schema.DataFields[0], _ids);
        var symbolColumn = new DataColumn(_schema.DataFields[1], _symbols);
        
        using var groupWriter = writer.CreateRowGroup();

        await groupWriter.WriteColumnAsync(idColumn);
        await groupWriter.WriteColumnAsync(symbolColumn);
    }

    [System.Diagnostics.CodeAnalysis.MemberNotNull(
        nameof(_ids),
        nameof(_symbols))]
    private void ResizeArrays(int newSize)
    {
        if (_ids != null && _ids.Length == newSize)
#pragma warning disable CS8774 // Member must have a non-null value when exiting.
            return;
#pragma warning restore CS8774 // Member must have a non-null value when exiting.

        _ids = new long[newSize];
        _symbols = new string[newSize];
    }

    public static IBatchPersistance<SymbolEntry> Create(string basePath, int batchSize, CompressionMethod compressionMethod)
    {
        Directory.CreateDirectory(basePath);
        return new ParquetSymPersistance(basePath, batchSize, compressionMethod);
    }
}