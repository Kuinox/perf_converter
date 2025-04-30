using System.Collections.Generic;
using System.IO;
using Parquet;
using Parquet.Data;
using Parquet.Schema;
using PerfConverter.Entry;

namespace PerfConverter.Persistance.ParquetDotNet;

public class ParquetSymPersistance : IBatchPersistance<SymbolEntry>
{
    private readonly string _filePath;
    private readonly ParquetSchema _schema;

    private long[]? _ids;
    private string[]? _symbols;

    private ParquetSymPersistance(string basePath, int batchSize)
    {
        _filePath = Path.Combine(basePath, "symbols.parquet");

        _schema = new ParquetSchema(
            new DataField<long>("Id"),
            new DataField<string>("Symbol")
        );

        ResizeArrays(batchSize);
    }

    public async Task PersistAsync(IReadOnlyCollection<SymbolEntry> batch)
    {
        int count = batch.Count;


        ResizeArrays(count);


        var i = 0;
        foreach (var entry in batch)
        {
            _ids[i] = entry.Id;
            _symbols[i] = entry.Symbol;
            i++;
        }

        var fileExists = File.Exists(_filePath);

        using var fileStream = fileExists
            ? new FileStream(_filePath, FileMode.Open, FileAccess.ReadWrite)
            : new FileStream(_filePath, FileMode.Create, FileAccess.ReadWrite);

        using var writer = fileExists
            ? await ParquetWriter.CreateAsync(_schema, fileStream, append: true)
            : await ParquetWriter.CreateAsync(_schema, fileStream);

        using var groupWriter = writer.CreateRowGroup();

        await groupWriter.WriteColumnAsync(new DataColumn(_schema.DataFields[0], _ids));
        await groupWriter.WriteColumnAsync(new DataColumn(_schema.DataFields[1], _symbols));
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

    public static IBatchPersistance<SymbolEntry> Create(string basePath, int batchSize)
    {
        Directory.CreateDirectory(basePath);
        return new ParquetSymPersistance(basePath, batchSize);
    }
}