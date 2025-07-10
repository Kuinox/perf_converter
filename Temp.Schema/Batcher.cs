using System.Diagnostics;
using System.Threading.Channels;

namespace Temp.Core;

public class Batcher<T> : IPersister<T>, IAsyncDisposable
{
    readonly IBatchPersistence<T> _batchPersistence;
    readonly int _batchSize;
    readonly BatchingMode _batchingMode;
    readonly Channel<T> _channel;
    readonly string _fileName;
    Task? _workLoop;

    Batcher(IBatchPersistence<T> batchPersistence, int batchSize, BatchingMode batchingMode, string fileName)
    {
        _batchPersistence = batchPersistence;
        _batchSize = batchSize;
        _batchingMode = batchingMode;
        _fileName = fileName;
        _channel = Channel.CreateBounded<T>(batchSize);
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
        try
        {
            var batch = new List<T>();
            while (await _channel.Reader.WaitToReadAsync())
            {
                await Work(batch, false);
            }
            if(batch.Count > 0) await Work(batch, true);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            Console.Error.WriteLine(e.StackTrace);
            Environment.FailFast("Batch processing failed", e);
        }
    }

    async Task Work(List<T> batch, bool lastBatch)
    {
        AccumulateBatch(batch);
        if (!lastBatch && _batchingMode == BatchingMode.OnFull && batch.Count < _batchSize) return;
        await SendBatch(batch);
    }

    async Task SendBatch(List<T> batch)
    {

        Stopwatch sw = Stopwatch.StartNew();
        try
        {
            Console.Error.WriteLine($"FILE_STATUS|{_fileName}|FLUSHING|{batch.Count}");
            await _batchPersistence.PersistAsync(batch);
            Console.Error.WriteLine($"FILE_STATUS|{_fileName}|BUFFERING");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
        }

        sw.Restart();
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

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.Complete();
        if (_workLoop != null)
        {
            await _workLoop;
        }
        Console.Error.WriteLine($"FILE_STATUS|{_fileName}|CLOSED");
        await _batchPersistence.DisposeAsync();
    }
}
