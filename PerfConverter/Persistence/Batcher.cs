using System.Diagnostics;
using System.Threading.Channels;
using PerfConverter.Entry;
using PerfToPerfetto;

namespace PerfConverter.Persistence;

public class Batcher<T> : IPersister<T>, IAsyncDisposable
{
    readonly IBatchPersistence<T> _batchPersistence;
    readonly int _batchSize;
    readonly BatchingMode _batchingMode;
    readonly Channel<T> _channel;
    readonly string _fileName;
    readonly string _fileActivityPrefix;
    readonly List<T> _batchA;
    readonly List<T> _batchB;
    Task _flushTask = Task.CompletedTask;
    Task? _workLoop;

    Batcher(IBatchPersistence<T> batchPersistence, int batchSize, BatchingMode batchingMode, string fileName)
    {
        _batchPersistence = batchPersistence;
        _batchSize = batchSize;
        _batchingMode = batchingMode;
        _fileName = fileName;
        _fileActivityPrefix = $"FILE_ACTIVITY|{fileName}|ACTIVE|";
        _batchA = new List<T>();
        _batchB = new List<T>();
        _channel = Channel.CreateBounded<T>(new BoundedChannelOptions(Math.Min(batchSize, 100_000))
        {
            SingleReader = true,
            SingleWriter = true
        });
    }

    void Start() => _workLoop = Task.Factory.StartNew(WorkLoop, TaskCreationOptions.LongRunning).Unwrap();

    public void Persist(T val)
    {
        var sentMessage = false;
        while (!_channel.Writer.TryWrite(val))
        {
            if(!sentMessage)
            {
                Console.Error.WriteLine($"Channel queue stalled.");
                sentMessage = true;
            }
            Thread.Yield();
        }
        if (sentMessage)
        {
            Console.Error.WriteLine($"Stalled queue completed.");
        }
    }

    async Task WorkLoop()
    {
        // Set thread name for profiling visibility
        Thread.CurrentThread.Name = $"Batcher:{_fileName}";

        try
        {
            var activeBatch = _batchA;
            var standbyBatch = _batchB;

            while (await _channel.Reader.WaitToReadAsync())
            {
                AccumulateBatch(activeBatch);

                _debounceFileActivity.Debounce(_fileActivityPrefix, activeBatch.Count);

                if (_batchingMode == BatchingMode.OnFull && activeBatch.Count < _batchSize)
                    continue;

                // Wait for previous flush to complete before swapping
                await _flushTask;

                // Start flushing current batch in background
                var batchToFlush = activeBatch;
                _flushTask = FlushBatchAsync(batchToFlush);

                // Swap to standby batch and continue accumulating
                activeBatch = standbyBatch;
                standbyBatch = batchToFlush;
            }

            // Shutdown: wait for pending flush, then flush remaining
            await _flushTask;
            if (activeBatch.Count > 0)
            {
                await FlushBatchAsync(activeBatch);
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            Console.Error.WriteLine(e.StackTrace);
            Environment.FailFast("Batch processing failed", e);
        }
    }

    readonly DebounceSignal _debounceFileActivity = new(5000);

    async Task FlushBatchAsync(List<T> batch)
    {
        try
        {
            if (Configuration.EnableProgressSignals)
            {
                Console.Error.WriteLine($"FILE_STATUS|{_fileName}|FLUSHING|{batch.Count}");
            }
            await _batchPersistence.PersistAsync(batch);
            if (Configuration.EnableProgressSignals)
            {
                Console.Error.WriteLine($"FILE_STATUS|{_fileName}|BUFFERING");
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
        }

        batch.Clear();
    }

    void AccumulateBatch(List<T> batch)
    {
        while (_channel.Reader.TryRead(out var item))
        {
            batch.Add(item);
            if (batch.Count >= _batchSize) break;
        }
    }

    public static Batcher<T> Create(IBatchPersistence<T> batchPersistence, int batchSize, BatchingMode batchingMode, string fileName)
    {
        var batcher = new Batcher<T>(batchPersistence, batchSize, batchingMode, fileName);
        batcher.Start();
        return batcher;
    }

    Task? _disposeTask;

    private async Task DisposeAsyncCore()
    {
        _channel.Writer.Complete();
        if (_workLoop != null)
        {
            await _workLoop;
        }
        await _batchPersistence.DisposeAsync();
        if (Configuration.EnableProgressSignals)
        {
            Console.Error.WriteLine($"FILE_STATUS|{_fileName}|CLOSED");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposeTask ??= DisposeAsyncCore();
        await _disposeTask;
    }
}
