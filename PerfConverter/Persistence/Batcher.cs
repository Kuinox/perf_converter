using PerfConverter.Entry;
using PerfToPerfetto;

namespace PerfConverter.Persistence;

public class Batcher<T> : IPersister<T>
{
    readonly IBatchPersistence<T> _batchPersistence;
    readonly int _batchSize;
    readonly string _fileName;
    readonly string _fileActivityPrefix;
    readonly List<T> _batch;
    bool _disposed;

    Batcher(IBatchPersistence<T> batchPersistence, int batchSize, string fileName)
    {
        _batchPersistence = batchPersistence;
        _batchSize = batchSize;
        _fileName = fileName;
        _fileActivityPrefix = $"FILE_ACTIVITY|{fileName}|ACTIVE|";
        _batch = new List<T>(batchSize);
    }

    readonly DebounceSignal _debounceFileActivity = new(5000);

    public void Persist(T val)
    {
        _batch.Add(val);

        if (_batch.Count >= _batchSize)
            FlushBatch(_batch);

        _debounceFileActivity.Debounce(_fileActivityPrefix, _batch.Count);
    }

    void FlushBatch(List<T> batch)
    {
        try
        {
            if (Configuration.EnableProgressSignals)
                Console.Error.WriteLine($"FILE_STATUS|{_fileName}|FLUSHING|{batch.Count}");

            _batchPersistence.Persist(batch);

            if (Configuration.EnableProgressSignals)
                Console.Error.WriteLine($"FILE_STATUS|{_fileName}|BUFFERING");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"FATAL persistence failure while flushing '{_fileName}'.");
            Console.Error.WriteLine(e.ToString());
            Environment.FailFast($"Fatal persistence failure while flushing '{_fileName}'.", e);
        }

        batch.Clear();
    }

    public static Batcher<T> Create(IBatchPersistence<T> batchPersistence, int batchSize, string fileName)
    {
        return new Batcher<T>(batchPersistence, batchSize, fileName);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_batch.Count > 0)
            FlushBatch(_batch);

        _batchPersistence.Dispose();

        if (Configuration.EnableProgressSignals)
            Console.Error.WriteLine($"FILE_STATUS|{_fileName}|CLOSED");
    }
}
