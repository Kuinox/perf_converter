using System.Diagnostics;
using System.Threading.Channels;

namespace PerfConverter
{
    public sealed class BackgroundBatching<T> where T : struct
    {
        readonly ChannelReader<T> _channel;
        readonly Task _workLoop;
        readonly int _batchSize;
        readonly Action<IReadOnlyCollection<T>> _batchProcessor;

        BackgroundBatching(int batchSize, ChannelReader<T> channel, Action<IReadOnlyCollection<T>> batchProcessor)
        {
            _channel = channel;
            _workLoop = Task.Factory.StartNew(WorkLoop, TaskCreationOptions.LongRunning).Unwrap();
            _batchSize = batchSize;
            _batchProcessor = batchProcessor;
        }

        async Task WorkLoop()
        {
            try
            {
                var batch = new List<T>();
                Stopwatch sw = Stopwatch.StartNew();
                while (await _channel.WaitToReadAsync())
                {
                    while (_channel.TryRead(out var item) && batch.Count < _batchSize)
                    {
                        batch.Add(item);
                    }
                    SqlLock.Wait();
                    sw.Restart();
                    _batchProcessor(batch);
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

        public static Task Run(int channelSize, ChannelReader<T> channel, Action<IReadOnlyCollection<T>> batchProcessor)
        {
            var batching = new BackgroundBatching<T>(channelSize, channel, batchProcessor);
            return batching._workLoop;
        }
    }
}
