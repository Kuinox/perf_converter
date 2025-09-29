using Parquet;
using PerfConverter.Entry;
using PerfConverter.Schema;
namespace PerfConverter.Persistence.ParquetDotNet;
public sealed class ParquetStackRangePersistence(StackRangeSchema schema, ParquetWriter writer, FileStream fileStream) : IBatchPersistence<StackRange>
{
    private int _prevSize;
    public async Task PersistAsync(IReadOnlyCollection<StackRange> batch)
    {
        if (batch.Count == 0) return;

        if (batch.Count != _prevSize)
        {
            _prevSize = batch.Count;
            schema.Resize(batch.Count);
        }

        int i = 0;
        foreach (var entry in batch)
        {
            schema.StartTrace.Buffer[i] = entry.StartTrace;
            schema.EndTrace.Buffer[i] = entry.EndTrace;
            i++;
        }
        await schema.Writer(writer);
    }

    Task? _dispose;

    public async ValueTask DisposeAsync()
    {
        _dispose ??= Task.WhenAll(
            writer.DisposeAsync().AsTask(),
            fileStream.DisposeAsync().AsTask()
        );
        await _dispose;
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
