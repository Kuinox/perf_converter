using Parquet;
using PerfConverter.Entry;
using Temp.Core;
using Temp.Schema;
namespace PerfConverter.Persistence.ParquetDotNet;
public class ParquetStackRangePersistence : IBatchPersistence<StackRange>
{
    private readonly ParquetWriter _writer;
    private readonly FileStream _fileStream;
    private readonly StackRangeSchema _schema;

    private ParquetStackRangePersistence(StackRangeSchema schema, ParquetWriter writer, FileStream fileStream)
    {
        _schema = schema;
        _writer = writer;
        _fileStream = fileStream;
    }

    private int _prevSize;
    public async Task PersistAsync(IReadOnlyCollection<StackRange> batch)
    {
        if (batch.Count == 0) return;

        if (batch.Count != _prevSize)
        {
            _prevSize = batch.Count;
            _schema.Resize(batch.Count);
        }

        int i = 0;
        foreach (var entry in batch)
        {
            _schema.StartTrace.Buffer[i] = entry.StartTrace;
            _schema.EndTrace.Buffer[i] = entry.EndTrace;
            i++;
        }
        await _schema.Writer(_writer);
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync();
        await _fileStream.DisposeAsync();
    }

    public static async Task<IBatchPersistence<StackRange>> Create(string filepath, CompressionMethod compressionMethod)
    {
        var schema = new StackRangeSchema();

        var fileStream = new FileStream(filepath, FileMode.Create, FileAccess.ReadWrite);
        var writer = await ParquetWriter.CreateAsync(schema.Schema, fileStream);
        writer.CompressionMethod = compressionMethod;
        writer.CompressionLevel = System.IO.Compression.CompressionLevel.Optimal;

        return new ParquetStackRangePersistence(schema, writer, fileStream);
    }
}
