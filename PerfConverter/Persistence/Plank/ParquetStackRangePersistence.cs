using PerfConverter.Entry;
using PerfConverter.Schema;

namespace PerfConverter.Persistence.Plank;

public sealed class ParquetStackRangePersistence(StackRangeSchema schema, PlankParquetFileWriter writer) : IBatchPersistence<StackRange>
{
    int _prevSize;
    bool _disposed;

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

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            writer.CloseFile();
            _disposed = true;
        }

        return ValueTask.CompletedTask;
    }

    public static Task<IBatchPersistence<StackRange>> Create(string filepath)
    {
        var schema = new StackRangeSchema();
        var fileStream = new FileStream(filepath, FileMode.Create, FileAccess.ReadWrite);
        var writer = schema.CreateWriter(fileStream);

        return Task.FromResult<IBatchPersistence<StackRange>>(new ParquetStackRangePersistence(schema, writer));
    }
}
