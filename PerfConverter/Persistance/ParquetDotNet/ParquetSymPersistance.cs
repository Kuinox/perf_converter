using System.Collections.Generic;
using System.IO;
using Parquet;
using Parquet.Data;
using Parquet.Schema;
using PerfConverter.Entry;

namespace PerfConverter.Persistance.ParquetDotNet;

public class ParquetSymPersistance : IBatchPersistance<SymbolEntry>
{
    private readonly string _basePath;
    private readonly string _filePath;
    private readonly ParquetSchema _schema;
    
    private ParquetSymPersistance(string basePath)
    {
        _basePath = basePath;
        _filePath = Path.Combine(basePath, "symbols.parquet");
        
        _schema = new ParquetSchema(
            new DataField<long>("Id"),
            new DataField<string>("Symbol")
        );
    }

    public async Task PersistAsync(IReadOnlyCollection<SymbolEntry> batch)
    {
        var ids = new long[batch.Count];
        var symbols = new string[batch.Count];
        var i = 0;
        foreach (var entry in batch)
        {
            ids[i] = entry.Id;
            symbols[i] = entry.Symbol;
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

        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<long>("Id"), ids));
        await groupWriter.WriteColumnAsync(new DataColumn(new DataField<string>("Symbol"), symbols));
    }

    public static IBatchPersistance<SymbolEntry> Create(string basePath)
    {
        Directory.CreateDirectory(basePath);
        return new ParquetSymPersistance(basePath);
    }
}