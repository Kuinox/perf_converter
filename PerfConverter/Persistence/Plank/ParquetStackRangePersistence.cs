using PerfConverter.Entry;
using PerfConverter.Schema;

namespace PerfConverter.Persistence.Plank;

public sealed class ParquetStackRangePersistence(StackRangeSchema schema, PlankParquetFileWriter writer) : IBatchPersistence<StackRange>
{
    int _prevSize;
    bool _disposed;

    public void Persist(IReadOnlyCollection<StackRange> batch)
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

        schema.Writer(writer);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            writer.CloseFile();
            _disposed = true;
        }
    }

    public static IBatchPersistence<StackRange> Create(string filepath)
    {
        var schema = new StackRangeSchema();
        var fileStream = new FileStream(filepath, FileMode.Create, FileAccess.ReadWrite);
        var writer = schema.CreateWriter(fileStream);

        return new ParquetStackRangePersistence(schema, writer);
    }
}
