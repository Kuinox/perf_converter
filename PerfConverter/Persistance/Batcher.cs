using PerfConverter.Persistance.Sql;
using System.Diagnostics;
using System.Threading.Channels;

namespace PerfConverter.Persistance;

public class Batcher<T> : IPersiter<T>, IDisposable
{
    readonly IBatchPersistance<T> _batchPersistance;
    readonly int _batchSize;
    readonly BatchingMode _batchingMode;
    readonly Channel<T> _channel;
    Task? _workLoop;

    Batcher(IBatchPersistance<T> batchPersistance, int batchSize, BatchingMode batchingMode)
    {
        _batchPersistance = batchPersistance;
        _batchSize = batchSize;
        _batchingMode = batchingMode;
        _channel = Channel.CreateBounded<T>(batchSize);
    }

    void Start() => _workLoop = Task.Factory.StartNew(WorkLoop, TaskCreationOptions.LongRunning).Unwrap();

    public void Persit(T val)
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

    private async Task SendBatch(List<T> batch)
    {

        Stopwatch sw = Stopwatch.StartNew();
        try
        {
            await _batchPersistance.PersistAsync(batch);
            Console.Error.WriteLine($"Sent batch of {batch.Count} {typeof(T)} in {sw.ElapsedMilliseconds}");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
        }

        sw.Restart();
        batch.Clear();
    }

    private void AccumulateBatch(List<T> batch)
    {
        while (_channel.Reader.TryRead(out var item))
        {
            batch.Add(item);
            if (batch.Count >= _batchSize) break;
        }
    }

    public static Batcher<T> Create(IBatchPersistance<T> batchPersistance, int batchSize, BatchingMode batchingMode)
    {
        var batcher = new Batcher<T>(batchPersistance, batchSize, batchingMode);
        batcher.Start();
        return batcher;
    }

    public void Dispose()
    {
        _channel.Writer.Complete();
        _workLoop?.Wait();
    }
}
