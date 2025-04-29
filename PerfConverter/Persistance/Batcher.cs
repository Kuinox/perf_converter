using PerfConverter.Persistance.Sql;
using System.Diagnostics;
using System.Threading.Channels;

namespace PerfConverter.Persistance;

public class Batcher<T> : IPersiter<T>, IDisposable
{
    readonly IBatchPersistance<T> _batchPersistance;
    readonly int _batchSize;
    readonly Channel<T> _channel;
    Task? _workLoop;

    Batcher(IBatchPersistance<T> batchPersistance, int batchSize)
    {
        _batchPersistance = batchPersistance;
        _batchSize = batchSize;
        _channel = Channel.CreateBounded<T>(batchSize);
    }

    void Start()
    {
        _workLoop = Task.Factory.StartNew(WorkLoop, TaskCreationOptions.LongRunning).Unwrap();
    }

    public void Persit(T val)
    {
        while (!_channel.Writer.TryWrite(val))
        {
            Thread.Yield();
        }
    }

    async Task WorkLoop()
    {
        try
        {
            var batch = new List<T>();
            while (await _channel.Reader.WaitToReadAsync())
            {
                SendBatch(batch);
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
        }
    }

    void SendBatch(List<T> batch)
    {
        Stopwatch sw = Stopwatch.StartNew();

        while (_channel.Reader.TryRead(out var item) && batch.Count < _batchSize)
        {
            batch.Add(item);
        }
        SqlLock.Wait();
        sw.Restart();
        _batchPersistance.Persist(batch);
        Console.Error.WriteLine($"Sent batch of {batch.Count} {typeof(T)} in {sw.ElapsedMilliseconds}");
        SqlLock.Release();
        batch.Clear();
    }

    public static Batcher<T> Create(IBatchPersistance<T> batchPersistance, int batchSize)
    {
        var batcher = new Batcher<T>(batchPersistance, batchSize);
        batcher.Start();
        return batcher;
    }

    public void Dispose()
    {
        _channel.Writer.Complete();
        _workLoop?.Wait();
    }
}
