using PerfConverter.Entry;
using PerfToPerfetto;

namespace PerfConverter.Persistence;

public class Batcher<T> : IPersister<T>, IAsyncDisposable
{
    readonly IBatchPersistence<T> _batchPersistence;
    readonly int _batchSize;
    readonly string _fileName;
    readonly string _fileActivityPrefix;
    readonly List<T> _batchA;
    readonly List<T> _batchB;
    List<T> _activeBatch;
    List<T> _standbyBatch;
    Task _flushTask = Task.CompletedTask;
    bool _disposed;

    Batcher(IBatchPersistence<T> batchPersistence, int batchSize, string fileName)
    {
        _batchPersistence = batchPersistence;
        _batchSize = batchSize;
        _fileName = fileName;
        _fileActivityPrefix = $"FILE_ACTIVITY|{fileName}|ACTIVE|";
        _batchA = new List<T>(batchSize);
        _batchB = new List<T>(batchSize);
        _activeBatch = _batchA;
        _standbyBatch = _batchB;
    }

    readonly DebounceSignal _debounceFileActivity = new(5000);

    public void Persist(T val)
    {
        _activeBatch.Add(val);

        if (_activeBatch.Count >= _batchSize)
        {
            // Wait for previous flush if still running
            if (!_flushTask.IsCompleted)
            {
                if (Configuration.EnableProgressSignals)
                    Console.Error.WriteLine($"BATCHER_STALL|{_fileName}|waiting for previous flush");
                _flushTask.GetAwaiter().GetResult();
            }

            // Swap batches
            var batchToFlush = _activeBatch;
            _activeBatch = _standbyBatch;
            _standbyBatch = batchToFlush;

            // Start background flush
            _flushTask = FlushBatchAsync(batchToFlush);
        }

        _debounceFileActivity.Debounce(_fileActivityPrefix, _activeBatch.Count);
    }

    async Task FlushBatchAsync(List<T> batch)
    {
        try
        {
            if (Configuration.EnableProgressSignals)
                Console.Error.WriteLine($"FILE_STATUS|{_fileName}|FLUSHING|{batch.Count}");

            await _batchPersistence.PersistAsync(batch);

            if (Configuration.EnableProgressSignals)
                Console.Error.WriteLine($"FILE_STATUS|{_fileName}|BUFFERING");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
        }

        batch.Clear();
    }

    public static Batcher<T> Create(IBatchPersistence<T> batchPersistence, int batchSize, BatchingMode batchingMode, string fileName)
    {
        return new Batcher<T>(batchPersistence, batchSize, fileName);
    }

    Task? _disposeTask;

    async Task DisposeAsyncCore()
    {
        if (_disposed) return;
        _disposed = true;

        // Wait for any pending flush
        await _flushTask;

        // Flush remaining items
        if (_activeBatch.Count > 0)
            await FlushBatchAsync(_activeBatch);

        await _batchPersistence.DisposeAsync();

        if (Configuration.EnableProgressSignals)
            Console.Error.WriteLine($"FILE_STATUS|{_fileName}|CLOSED");
    }

    public async ValueTask DisposeAsync()
    {
        _disposeTask ??= DisposeAsyncCore();
        await _disposeTask;
    }
}
