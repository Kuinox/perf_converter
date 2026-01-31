using PerfConverter.Entry;
using PerfToPerfetto;

namespace PerfConverter.Persistence;

public class Batcher<T> : IPersister<T>, IAsyncDisposable
{
    readonly IBatchPersistence<T> _batchPersistence;
    readonly int _batchSize;
    readonly string _fileName;
    readonly string _fileActivityPrefix;
    readonly Queue<List<T>> _availableBuffers = new();
    readonly Queue<List<T>> _pendingFlushBuffers = new();
    List<T> _activeBatch;
    Task? _flushTask;
    bool _disposed;

    Batcher(IBatchPersistence<T> batchPersistence, int batchSize, string fileName)
    {
        _batchPersistence = batchPersistence;
        _batchSize = batchSize;
        _fileName = fileName;
        _fileActivityPrefix = $"FILE_ACTIVITY|{fileName}|ACTIVE|";
        // Start with 3 buffers for triple buffering
        _activeBatch = new List<T>(batchSize);
        _availableBuffers.Enqueue(new List<T>(batchSize));
        _availableBuffers.Enqueue(new List<T>(batchSize));
    }

    readonly DebounceSignal _debounceFileActivity = new(5000);

    public void Persist(T val)
    {
        _activeBatch.Add(val);

        if (_activeBatch.Count >= _batchSize)
        {
            // Queue current batch for flushing
            _pendingFlushBuffers.Enqueue(_activeBatch);

            // Get next available buffer
            if (_availableBuffers.Count > 0)
            {
                _activeBatch = _availableBuffers.Dequeue();
            }
            else
            {
                // All buffers in use - create a new one
                if (Configuration.EnableProgressSignals)
                    Console.Error.WriteLine($"BATCHER_EXPAND|{_fileName}|allocating additional buffer");
                _activeBatch = new List<T>(_batchSize);
            }

            // Start flush if not already running
            TryStartFlush();
        }

        _debounceFileActivity.Debounce(_fileActivityPrefix, _activeBatch.Count);
    }

    void TryStartFlush()
    {
        if (_flushTask != null && !_flushTask.IsCompleted)
            return; // Already flushing

        if (_pendingFlushBuffers.Count == 0)
            return; // Nothing to flush

        var batchToFlush = _pendingFlushBuffers.Dequeue();
        _flushTask = FlushBatchAsync(batchToFlush);
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
        _availableBuffers.Enqueue(batch);

        // Start next flush if there are pending buffers
        TryStartFlush();
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

        // Queue active batch if it has items
        if (_activeBatch.Count > 0)
            _pendingFlushBuffers.Enqueue(_activeBatch);

        // Start flush if not running
        TryStartFlush();

        // Wait for all pending flushes to complete
        while (_flushTask != null && !_flushTask.IsCompleted || _pendingFlushBuffers.Count > 0)
        {
            if (_flushTask != null)
                await _flushTask;
            TryStartFlush();
        }

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
