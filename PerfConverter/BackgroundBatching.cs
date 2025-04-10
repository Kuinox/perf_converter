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
        readonly Channel<T> _entries;
        readonly Task _workLoop;
        protected BackgroundBatching(int channelSize)
        {
            _entries = Channel.CreateBounded<T>(new BoundedChannelOptions(channelSize)
            {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false
            });
            _workLoop = Task.Factory.StartNew(WorkLoop, TaskCreationOptions.LongRunning);
        }

        async Task WorkLoop()
        {
            try
            {

                var batch = new List<T>();

                while (await _entries.Reader.WaitToReadAsync())
                {
                    while (_entries.Reader.TryRead(out var item))
                    {
                        batch.Add(item);
                    }
                    BatchSend(batch);
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
