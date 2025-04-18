using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PerfConverter
{
    public abstract class BackgroundBatching<T> where T : struct
    {
        readonly Channel<T> _entries;
        readonly Task _workLoop;
        readonly int _channelSize;
        unsafe protected BackgroundBatching(int channelSize)
        {
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
                Stopwatch sw = Stopwatch.StartNew();
                while (await _entries.Reader.WaitToReadAsync())
                {
                    while (_entries.Reader.TryRead(out var item) && batch.Count < _channelSize)
                    {
                        batch.Add(item);
                    }
                    SqlLock.Wait();
                    Console.Error.WriteLine($"Sending batch of {batch.Count} {typeof(T)}");
                    sw.Restart();
                    BatchSend(batch);
                    Console.Error.WriteLine($"Sent batch of {batch.Count} {typeof(T)} in {sw.ElapsedMilliseconds}");
                    SqlLock.Release();
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
