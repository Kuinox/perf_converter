using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PerfConverter
{
    public abstract class BackgroundBatching<T> where T : struct
    {
        readonly SemaphoreSlim _semaphore;
        readonly Channel<T> _entries;
        readonly Task _workLoop;
        readonly int _channelSize;

        protected BackgroundBatching(int channelSize, SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
            _entries = Channel.CreateBounded<T>(new BoundedChannelOptions(channelSize)
            {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false
            });
            _workLoop = Task.Factory.StartNew(WorkLoop, TaskCreationOptions.LongRunning);
            this._channelSize = channelSize;
        }

        async Task WorkLoop()
        {
            try
            {

                var batch = new List<T>();

                while (await _entries.Reader.WaitToReadAsync())
                {
                    while (_entries.Reader.TryRead(out var item) && batch.Count < _channelSize)
                    {
                        batch.Add(item);
                    }
                    _semaphore.Wait();
                    Console.Error.WriteLine($"Sending lol batch of {batch.Count} {typeof(T)}, {_semaphore.CurrentCount}");
                    BatchSend(batch);
                    Console.Error.WriteLine($"Sent batch of {batch.Count} {typeof(T)}");
                    _semaphore.Release();
                    batch.Clear();
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);
            }
        }

        protected abstract void BatchSend(IReadOnlyCollection<T> batch);

        protected void QueueItem(T item)
        {
            while (!_entries.Writer.TryWrite(item))
            {
                Thread.Yield();
            }
        }

        public void Close()
        {
            _entries.Writer.TryComplete();
            _workLoop.GetAwaiter().GetResult();
        }
    }
}
