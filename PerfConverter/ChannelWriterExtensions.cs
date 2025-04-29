using System.Threading.Channels;

namespace PerfConverter
{
    public static class ChannelWriterExtensions
    {
        public static void Write<T>(this ChannelWriter<T> @this, T entry)
        {
            while (!@this.TryWrite(entry))
            {
                Thread.Yield();
            }
        }
    }
}
