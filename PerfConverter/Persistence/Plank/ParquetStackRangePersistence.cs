using PerfConverter.Entry;
using Temp.Schema.Schema;

namespace PerfConverter.Persistence.Plank;

public sealed class ParquetStackRangePersistence(StackRangeRowSchema.PipelineWriter writer) : IPersister<StackRange>
{
    bool _disposed;

    public void Persist(StackRange entry)
    {
        var row = writer.GetRow();
        row.StartTrace = entry.StartTrace;
        row.EndTrace = entry.EndTrace;
        writer.Next();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            writer.Complete();
            _disposed = true;
        }
    }

    public static IPersister<StackRange> Create(string filepath)
        => Create(filepath, onFlush: null);

    public static IPersister<StackRange> Create(string filepath, Action<int>? onFlush)
    {
        var fileStream = new FileStream(filepath, FileMode.Create, FileAccess.ReadWrite);
        var writer = StackRangeRowSchema.CreateRowWriter(fileStream, onFlush);

        return new ParquetStackRangePersistence(writer);
    }
}
