using System.Diagnostics;
using System.Threading.Channels;

namespace PerfConverter.Persistence;

public class Batcher<T> : IPersister<T>, IAsyncDisposable
{
    readonly IBatchPersistence<T> _batchPersistence;
    readonly int _batchSize;
    readonly BatchingMode _batchingMode;
    readonly Channel<T> _channel;
    Task? _workLoop;

    Batcher(IBatchPersistence<T> batchPersistence, int batchSize, BatchingMode batchingMode)
    {
        _batchPersistence = batchPersistence;
        _batchSize = batchSize;
        _batchingMode = batchingMode;
        _channel = Channel.CreateBounded<T>(batchSize);
    }

    void Start() => _workLoop = Task.Factory.StartNew(WorkLoop, TaskCreationOptions.LongRunning).Unwrap();

    public void Persist(T val)
    {
        while (!_channel.Writer.TryWrite(val)) Thread.Yield();
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
            await _batchPersistence.PersistAsync(batch);
            Console.Error.WriteLine($"Wrote batch of {batch.Count} {typeof(T).Name} in {sw.ElapsedMilliseconds}ms");
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

    public static Batcher<T> Create(IBatchPersistence<T> batchPersistence, int batchSize, BatchingMode batchingMode)
    {
        var batcher = new Batcher<T>(batchPersistence, batchSize, batchingMode);
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
        
        await _batchPersistence.DisposeAsync();
    }
}
