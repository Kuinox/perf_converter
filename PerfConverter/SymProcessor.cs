using PerfConverter.Persistance;
using System.Data.Common;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace PerfConverter;

public class SymProcessor
{
    readonly Dictionary<string, long> _ids = [];
    readonly Channel<SymbolEntry> _channel;
    readonly Task _workThread;

    public SymProcessor(ISymPersistance persistance)
    {
        var batchSize = 1_000_000;
        _channel = Channel.CreateBounded<SymbolEntry>(batchSize);
        _workThread = BackgroundBatching<SymbolEntry>.Run(batchSize, _channel.Reader, persistance.Persist);
    }


    public long Process(string sym)
    {
        var defaultEntry = CollectionsMarshal.GetValueRefOrAddDefault(_ids, sym, out var exists);
        if (!exists)
        {
            defaultEntry = _ids.Count;

            var entry = new SymbolEntry { Id = defaultEntry, Symbol = sym };
            _channel.Writer.Write(entry);
        }
        return defaultEntry;
    }

    public void Flush()
    {
        _channel.Writer.Complete();
        _workThread.Wait();
    }
}
